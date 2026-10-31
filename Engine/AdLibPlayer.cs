// Project: Robin.Net
// File: AdLibPlayer.cs
// License: GNU General Public License version 3 (see LICENSE)

using NukedOPL3Sharp;

namespace RobinHood.Engine;

internal sealed class AdLibPlayer
{
    private static readonly int[] Operators = [0, 1, 2, 8, 9, 10, 16, 17, 18];
    private readonly AdLibSequence sequence;
    private readonly Opl3Chip chip = new();
    private readonly byte[][] patches = new byte[9][];
    private readonly int[] notes = new int[9];
    private readonly int[] velocities = new int[9];
    private readonly int[] bends = new int[9];
    private readonly bool loop;
    private int bendRange = 1;
    private int eventIndex;
    private long position;

    public bool Finished { get; private set; }

    public AdLibPlayer(AdLibSequence sequence, int sampleRate, bool loop)
    {
        this.sequence = sequence;
        this.loop = loop && sequence.EndFrame > 0;
        chip.Reset((uint)sampleRate);
        chip.WriteRegister(0x01, 0x20);
        Array.Fill(notes, -1);
        Array.Fill(velocities, 127);
        Array.Fill(bends, 8192);
    }

    public void Render(Span<short> output)
    {
        output.Clear();
        int offset = 0;
        while (offset < output.Length && !Finished)
        {
            while (eventIndex < sequence.Events.Count && sequence.Events[eventIndex].Frame <= position)
                Apply(sequence.Events[eventIndex++]);

            if (position >= sequence.EndFrame)
            {
                if (!loop) { Finished = true; break; }
                position = 0;
                eventIndex = 0;
                for (int channel = 0; channel < 9; channel++) KeyOff(channel);
                continue;
            }
            long next = eventIndex < sequence.Events.Count ? sequence.Events[eventIndex].Frame : sequence.EndFrame;
            int frames = (int)Math.Min((output.Length - offset) / 2, Math.Max(0, next - position));
            if (frames == 0) continue;
            chip.GenerateStream(output.Slice(offset, frames * 2));
            offset += frames * 2;
            position += frames;
        }
    }

    private void Apply(AdLibEvent current)
    {
        if (current.Status == 0xff)
        {
            byte[]? data = current.Data;
            if (data is null || data.Length < 6 || data[0] != 0 || data[1] != 0 || data[2] != 0x3f || data[3] != 0)
                return;
            if (data[4] == 1 && data.Length == 34 && data[5] < 9)
            {
                int channel = data[5];
                patches[channel] = data[6..];
                ProgramPatch(channel);
            }
            else if (data[4] == 3) bendRange = Math.Clamp((int)data[5], 1, 12);
            return;
        }

        int voice = current.Status & 15;
        if (voice >= 9) return;
        switch (current.Status & 0xf0)
        {
            case 0x80:
                KeyOff(voice);
                break;
            case 0x90:
                if (current.Second == 0) { KeyOff(voice); break; }
                KeyOff(voice);
                notes[voice] = current.First;
                velocities[voice] = current.Second;
                SetLevels(voice);
                SetFrequency(voice);
                break;
            case 0xa0:
                velocities[voice] = current.Second;
                SetLevels(voice);
                break;
            case 0xd0:
                velocities[voice] = current.First;
                SetLevels(voice);
                break;
            case 0xe0:
                bends[voice] = current.First | (current.Second << 7);
                if (notes[voice] >= 0) SetFrequency(voice);
                break;
        }
    }

    private void ProgramPatch(int channel)
    {
        byte[] patch = patches[channel];
        for (int op = 0; op < 2; op++)
        {
            int p = op * 13;
            int address = Operators[channel] + op * 3;
            int control = (patch[p + 9] != 0 ? 0x80 : 0)
                | (patch[p + 10] != 0 ? 0x40 : 0)
                | (patch[p + 5] != 0 ? 0x20 : 0)
                | (patch[p + 11] != 0 ? 0x10 : 0) | (patch[p + 1] & 15);
            Write(0x20 + address, control);
            Write(0x60 + address, (patch[p + 3] << 4) | (patch[p + 6] & 15));
            Write(0x80 + address, (patch[p + 4] << 4) | (patch[p + 7] & 15));
            Write(0xe0 + address, patch[26 + op] & 3);
        }
        Write(0xc0 + channel, ((patch[2] & 7) << 1) | (patch[12] == 0 ? 1 : 0));
        SetLevels(channel);
    }

    private void SetLevels(int channel)
    {
        byte[]? patch = patches[channel];
        if (patch is null) return;
        for (int op = 0; op < 2; op++)
        {
            int p = op * 13;
            int level = 63 - (patch[p + 8] & 63);
            if (op == 1 || patch[12] == 0)
                level = (level * velocities[channel] + 63) / 127;
            Write(0x40 + Operators[channel] + op * 3, ((patch[p] & 3) << 6) | (63 - level));
        }
    }

    private void SetFrequency(int channel)
    {
        double note = notes[channel] + (bends[channel] - 8192) * bendRange / 8192.0;
        double frequency = 440.0 * Math.Pow(2, (note - 69) / 12.0);
        int block = Math.Clamp((int)Math.Floor(note / 12) - 1, 0, 7);
        int fNumber = Math.Clamp((int)Math.Round(frequency * 1048576.0 / (49716.0 * (1 << block))), 0, 1023);
        Write(0xa0 + channel, fNumber & 255);
        Write(0xb0 + channel, 0x20 | (block << 2) | (fNumber >> 8));
    }

    private void KeyOff(int channel)
    {
        if (notes[channel] < 0) return;
        double note = notes[channel] + (bends[channel] - 8192) * bendRange / 8192.0;
        double frequency = 440.0 * Math.Pow(2, (note - 69) / 12.0);
        int block = Math.Clamp((int)Math.Floor(note / 12) - 1, 0, 7);
        int fNumber = Math.Clamp((int)Math.Round(frequency * 1048576.0 / (49716.0 * (1 << block))), 0, 1023);
        Write(0xb0 + channel, (block << 2) | (fNumber >> 8));
        notes[channel] = -1;
    }

    private void Write(int register, int value) => chip.WriteRegister((ushort)register, (byte)value);
}
