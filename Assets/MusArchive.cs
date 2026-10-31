// Project: Robin.Net
// File: MusArchive.cs
// License: GNU General Public License version 3 (see LICENSE)

using System.Buffers.Binary;

namespace RobinHood.Assets;

internal static class MusArchive
{
    public static IReadOnlyList<byte[]> Read(byte[] data)
    {
        if (data.Length < 4)
            throw new InvalidDataException("The MUS offset table is truncated.");

        int count = BinaryPrimitives.ReadUInt16LittleEndian(data);
        int tableEnd = 2 + count * 2;
        if (count == 0 || tableEnd > data.Length)
            throw new InvalidDataException("Invalid MUS resource count or truncated offset table.");

        var offsets = new int[count + 1];
        for (int i = 0; i < count; i++)
        {
            offsets[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2 + i * 2));
            if (offsets[i] < tableEnd || offsets[i] >= data.Length || (i > 0 && offsets[i] <= offsets[i - 1]))
                throw new InvalidDataException($"Resource {i}: invalid or unordered offset 0x{offsets[i]:X4}.");
        }
        if (offsets[0] != tableEnd)
            throw new InvalidDataException("The first resource must immediately follow the offset table.");
        offsets[count] = data.Length;

        var entries = new List<byte[]>(count);
        for (int i = 0; i < count; i++)
        {
            try
            {
                entries.Add(NormalizeMidi(Decode(data[offsets[i]..offsets[i + 1]])));
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"Resource {i} at 0x{offsets[i]:X4}: {ex.Message}", ex);
            }
        }
        return entries;
    }

    private static byte[] Decode(byte[] stored)
    {
        if (stored.AsSpan().StartsWith("MThd"u8))
            return stored;
        if (stored[0] is not (0x43 or 0x63))
            throw new InvalidDataException($"Unsupported resource encoding 0x{stored[0]:X2}.");
        if (stored.Length <= 513)
            throw new InvalidDataException("Truncated byte-pair dictionary or missing compressed payload.");

        byte[] expanded = new byte[(stored.Length - 513) * 2];
        for (int i = 513; i < stored.Length; i++)
        {
            int pair = 1 + stored[i] * 2;
            int destination = (i - 513) * 2;
            expanded[destination] = stored[pair];
            expanded[destination + 1] = stored[pair + 1];
        }
        if (stored[0] == 0x63)
        {
            if (expanded[0] != 0)
                throw new InvalidDataException("Expected a leading zero in a 0x63 resource.");
            return expanded[1..];
        }
        return expanded;
    }

    private static byte[] NormalizeMidi(byte[] data)
    {
        if (data.Length < 26 || !data.AsSpan(0, 4).SequenceEqual("MThd"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)) != 6
            || BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(8)) != 0
            || BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(10)) != 1
            || !data.AsSpan(14, 4).SequenceEqual("MTrk"u8))
            throw new InvalidDataException("Expected a format-0 MIDI header and one MTrk chunk.");

        ushort division = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(12));
        if (division == 0 || (division & 0x8000) != 0)
            throw new InvalidDataException("Expected a nonzero ticks-per-quarter-note MIDI division.");
        uint declaredLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(18));
        int position = 22;
        byte runningStatus = 0;
        while (position < data.Length)
        {
            ReadVariableLength(data, ref position);
            byte first = ReadByte(data, ref position);
            byte status;
            if (first < 0x80)
            {
                if (runningStatus == 0)
                    throw new InvalidDataException($"Running status without a channel status at byte {position - 1}.");
                status = runningStatus;
                position--;
            }
            else status = first;

            if (status is >= 0x80 and <= 0xEF)
            {
                runningStatus = status;
                ReadDataByte(data, ref position);
                if ((status & 0xF0) is not (0xC0 or 0xD0))
                    ReadDataByte(data, ref position);
                continue;
            }

            runningStatus = 0;
            if (status == 0xFF)
            {
                byte type = ReadByte(data, ref position);
                int length = ReadVariableLength(data, ref position);
                RequireBytes(data, position, length);
                int payload = position;
                position += length;
                if (type == 0x2F)
                {
                    if (length != 0)
                        throw new InvalidDataException("End-of-track event must have an empty payload.");
                    int actualLength = position - 22;
                    int padding = data.Length - position;
                    if (padding > 1 || (padding == 1 && data[position] != 0))
                        throw new InvalidDataException("Unexpected bytes after the MIDI end-of-track event.");
                    if (declaredLength != actualLength && declaredLength != actualLength - 4)
                        throw new InvalidDataException($"Unexpected MIDI track length: declared {declaredLength}, actual {actualLength}.");
                    byte[] standard = data[..position];
                    BinaryPrimitives.WriteUInt32BigEndian(standard.AsSpan(18), (uint)actualLength);
                    return standard;
                }
                if (type == 0x51 && (length != 3 || (data[payload] | data[payload + 1] | data[payload + 2]) == 0))
                    throw new InvalidDataException("Tempo event must contain a nonzero three-byte value.");
            }
            else if (status is 0xF0 or 0xF7)
            {
                int length = ReadVariableLength(data, ref position);
                RequireBytes(data, position, length);
                position += length;
            }
            else throw new InvalidDataException($"Unsupported MIDI status 0x{status:X2} at byte {position - 1}.");
        }
        throw new InvalidDataException("MIDI end-of-track event is missing.");
    }

    private static byte ReadDataByte(byte[] data, ref int position)
    {
        byte value = ReadByte(data, ref position);
        if (value >= 0x80)
            throw new InvalidDataException($"Invalid MIDI data byte 0x{value:X2} at byte {position - 1}.");
        return value;
    }

    private static int ReadVariableLength(byte[] data, ref int position)
    {
        int value = 0;
        for (int i = 0; i < 4; i++)
        {
            byte next = ReadByte(data, ref position);
            value = (value << 7) | (next & 0x7F);
            if ((next & 0x80) == 0) return value;
        }
        throw new InvalidDataException("MIDI variable-length value exceeds four bytes.");
    }

    private static byte ReadByte(byte[] data, ref int position)
    {
        RequireBytes(data, position, 1);
        return data[position++];
    }

    private static void RequireBytes(byte[] data, int position, int count)
    {
        if (count < 0 || position > data.Length - count)
            throw new InvalidDataException($"Truncated MIDI event at byte {position}.");
    }
}
