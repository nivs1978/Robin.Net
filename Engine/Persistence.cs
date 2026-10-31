// Project: Robin.Net
// File: Persistence.cs
// License: GNU General Public License version 3 (see LICENSE)

using System.Buffers.Binary;

namespace RobinHood.Engine;

// STATUS.DTA as the original writes it: the 16 KB map followed by the first 0x2117 bytes of its data segment.
// Everything outside that range is deliberately not saved: view position, random seed, mutable script bytecode,
// lists, speech, cursor and input state (restoring sets first_time to false only).
public sealed partial class GameWorld
{
    public const int StatusMapBytes = 0x4000;
    public const int StatusDataBytes = 0x2117;
    public const string StatusFileName = "STATUS.DTA";
    const int Slots = 40;
    public Action<GameWorld>? SaveStatus { get; set; }
    public Action? DeleteStatus { get; set; }

    public byte[] ExportStatus()
    {
        var file = new byte[StatusMapBytes + StatusDataBytes];
        Map.CopyTo(file, 0);
        var data = file.AsSpan(StatusMapBytes);
        data.Fill(0);
        Word(data, 0, Program.TimeFrame);
        Word(data, 2, (ushort)CameraLock);
        Word(data, 4, (ushort)Host);
        data[6] = CubeSetCode;
        Word(data, 7, (ushort)Mobiles.Length);
        for (var slot = 0; slot < Slots; slot++)
        {
            var mobile = slot < Mobiles.Length ? Mobiles[slot] : null;
            Word(data, 0x09 + slot * 2, mobile == null ? (ushort)0xffff : (ushort)mobile.X);
            Word(data, 0x59 + slot * 2, mobile == null ? (ushort)0xffff : (ushort)mobile.Y);
            data[0xa9 + slot] = mobile?.Z ?? 0;
            data[0xd1 + slot] = mobile?.Direction ?? 0;
            data[0xf9 + slot] = mobile?.CarriedBy ?? 0xff;
            data[0x121 + slot] = (byte)(mobile?.Behind ?? 4);
            data[0x149 + slot] = mobile?.Above ?? 0;
            data[0x171 + slot] = mobile?.Type ?? 0;
            data[0x199 + slot] = mobile?.TargetX ?? 0;
            data[0x1c1 + slot] = mobile?.TargetY ?? 0;
            data[0x1e9 + slot] = mobile?.SubTargetX ?? 0xff;
            data[0x211 + slot] = mobile?.SubTargetY ?? 0xff;
            data[0x239 + slot] = mobile?.Follow ?? 0;
            Word(data, 0x261 + slot * 2, mobile?.BaseSprite ?? 0);
            data[0x2b1 + slot] = mobile?.Pose ?? 0;
            data[0x2d9 + slot] = mobile?.AboutFace ?? 0x14;
            data[0x301 + slot] = mobile?.Mobility ?? 0;
            data[0x329 + slot] = (byte)(mobile == null || mobile.Alert ? 1 : 0);
            if (mobile != null)
            {
                mobile.Attributes.CopyTo(data[(0xfd1 + slot * 32)..]);
                mobile.PoseAliases.CopyTo(data[(0x1599 + slot * 32)..]);
                for (var word = 0; word < 16; word++) Word(data, 0x1b11 + slot * 32 + word * 2, mobile.SeqBuffer[word]);
            }
            else for (var word = 0; word < 16; word++) Word(data, 0x1b11 + slot * 32 + word * 2, 0xffff);
            data[0x14d1 + slot] = mobile?.Behavior ?? 0;
            data[0x14f9 + slot] = mobile?.HomeX ?? 0;
            data[0x1521 + slot] = mobile?.HomeY ?? 0;
            Word(data, 0x1549 + slot * 2, slot < Mobiles.Length ? MobileSignals[slot] : (ushort)0xffff);
            data[0x1a99 + slot] = mobile?.Puff ?? 0xff;
            data[0x1ac1 + slot] = mobile?.SavedMode ?? 0;
            data[0x2011 + slot] = mobile?.SeqCount ?? 0x10;
            data[0x2039 + slot] = mobile?.SeqName ?? 0xff;
            Word(data, 0x209f + slot * 2, slot < Mobiles.Length ? signalStack[slot] : (ushort)0xffff);
            data[0x20ef + slot] = mobile?.Speed ?? 0x0f;
        }
        InteractionData.CopyTo(data[0x351..]);
        ButtonStates.CopyTo(data[0x1ae9..]);
        ButtonRegen.CopyTo(data[0x1afd..]);
        for (var entry = 0; entry < 10; entry++)
        {
            var offset = 0x2061 + entry * 6;
            Word(data, offset, signalDue[entry]);
            Word(data, offset + 2, signalTarget[entry]);
            Word(data, offset + 4, signalValue[entry]);
        }
        Word(data, 0x209d, signalTime);
        return file;
    }

    // Returns false (changing nothing) when the file is not a complete status file for this program.
    public bool ImportStatus(byte[] file)
    {
        if (file.Length != StatusMapBytes + StatusDataBytes) return false;
        var data = file.AsSpan(StatusMapBytes);
        if (Read(data, 7) != Mobiles.Length) return false;
        file.AsSpan(0, StatusMapBytes).CopyTo(Map);
        CameraLock = (short)Read(data, 2);
        Host = Read(data, 4);
        CubeSetCode = data[6];
        for (var slot = 0; slot < Mobiles.Length; slot++)
        {
            var mobile = Mobiles[slot];
            mobile.X = (short)Read(data, 0x09 + slot * 2);
            mobile.Y = (short)Read(data, 0x59 + slot * 2);
            mobile.Z = data[0xa9 + slot];
            mobile.Direction = data[0xd1 + slot];
            mobile.CarriedBy = data[0xf9 + slot];
            mobile.Behind = (sbyte)data[0x121 + slot];
            mobile.Above = data[0x149 + slot];
            mobile.Type = data[0x171 + slot];
            mobile.TargetX = data[0x199 + slot];
            mobile.TargetY = data[0x1c1 + slot];
            mobile.SubTargetX = data[0x1e9 + slot];
            mobile.SubTargetY = data[0x211 + slot];
            mobile.Follow = data[0x239 + slot];
            mobile.BaseSprite = Read(data, 0x261 + slot * 2);
            mobile.Pose = data[0x2b1 + slot];
            mobile.AboutFace = data[0x2d9 + slot];
            mobile.Mobility = data[0x301 + slot];
            mobile.Alert = data[0x329 + slot] != 0;
            data.Slice(0xfd1 + slot * 32, 32).CopyTo(mobile.Attributes);
            data.Slice(0x1599 + slot * 32, 32).CopyTo(mobile.PoseAliases);
            for (var word = 0; word < 16; word++) mobile.SeqBuffer[word] = Read(data, 0x1b11 + slot * 32 + word * 2);
            mobile.Behavior = data[0x14d1 + slot];
            mobile.HomeX = data[0x14f9 + slot];
            mobile.HomeY = data[0x1521 + slot];
            MobileSignals[slot] = Read(data, 0x1549 + slot * 2);
            mobile.Puff = data[0x1a99 + slot];
            mobile.SavedMode = data[0x1ac1 + slot];
            mobile.SeqCount = data[0x2011 + slot];
            mobile.SeqName = data[0x2039 + slot];
            signalStack[slot] = Read(data, 0x209f + slot * 2);
            mobile.Speed = data[0x20ef + slot];
        }
        data.Slice(0x351, InteractionData.Length).CopyTo(InteractionData);
        data.Slice(0x1ae9, ButtonStates.Length).CopyTo(ButtonStates);
        data.Slice(0x1afd, ButtonRegen.Length).CopyTo(ButtonRegen);
        for (var entry = 0; entry < 10; entry++)
        {
            var offset = 0x2061 + entry * 6;
            signalDue[entry] = Read(data, offset);
            signalTarget[entry] = Read(data, offset + 2);
            signalValue[entry] = Read(data, offset + 4);
        }
        signalTime = Read(data, 0x209d);
        FirstTime = false;
        return true;
    }

    static void Word(Span<byte> data, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], value);
    static ushort Read(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
}
