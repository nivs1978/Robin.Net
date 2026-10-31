// Project: Robin.Net
// File: AdLibSequence.cs
// License: GNU General Public License version 3 (see LICENSE)

using System.Buffers.Binary;

namespace RobinHood.Engine;

internal readonly record struct AdLibEvent(long Frame, byte Status, byte First, byte Second, byte[]? Data);

internal sealed class AdLibSequence
{
    public IReadOnlyList<AdLibEvent> Events { get; }
    public long EndFrame { get; }

    public AdLibSequence(byte[] midi, int sampleRate)
    {
        int division = BinaryPrimitives.ReadUInt16BigEndian(midi.AsSpan(12));
        int position = 22;
        int tempo = 500000;
        double frame = 0;
        byte runningStatus = 0;
        var events = new List<AdLibEvent>();
        while (position < midi.Length)
        {
            frame += ReadVariable(midi, ref position) * (double)tempo * sampleRate / (division * 1000000.0);
            byte status = midi[position++];
            if (status < 0x80) { position--; status = runningStatus; }
            else if (status < 0xf0) runningStatus = status;
            else runningStatus = 0;

            if (status < 0xf0)
            {
                byte first = midi[position++];
                byte second = (status & 0xf0) is 0xc0 or 0xd0 ? (byte)0 : midi[position++];
                events.Add(new((long)frame, status, first, second, null));
            }
            else if (status == 0xff)
            {
                byte type = midi[position++];
                int length = ReadVariable(midi, ref position);
                if (type == 0x51)
                    tempo = (midi[position] << 16) | (midi[position + 1] << 8) | midi[position + 2];
                else if (type == 0x7f)
                    events.Add(new((long)frame, status, type, 0, midi[position..(position + length)]));
                position += length;
                if (type == 0x2f) break;
            }
            else
            {
                int length = ReadVariable(midi, ref position);
                position += length;
            }
        }
        Events = events;
        EndFrame = (long)frame;
    }

    private static int ReadVariable(byte[] midi, ref int position)
    {
        int value = 0;
        byte next;
        do { next = midi[position++]; value = (value << 7) | (next & 0x7f); }
        while ((next & 0x80) != 0);
        return value;
    }
}
