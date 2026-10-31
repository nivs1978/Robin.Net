// Project: Robin.Net
// File: SqueezedFile.cs
// License: GNU General Public License version 3 (see LICENSE)

namespace RobinHood.Assets;

public sealed record SqueezedFile(byte[] Pixels, byte[]? Palette)
{
    public static SqueezedFile Load(string path, bool containsPalette, int capacity = 65536) =>
        Decode(File.ReadAllBytes(path), containsPalette, capacity);

    public static SqueezedFile Decode(ReadOnlySpan<byte> input, bool containsPalette, int capacity = 65536)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        var position = containsPalette ? 768 : 0;
        if (input.Length < position) throw new InvalidDataException("Truncated VGA palette.");
        var palette = containsPalette ? input[..768].ToArray() : null;
        var output = new byte[capacity];
        var written = 0;
        while (position < input.Length)
        {
            var header = input[position++];
            if (header == 255) return new(output[..written], palette);
            var count = header & 127;
            if (count == 0) throw new InvalidDataException("Zero-length squeezed block.");
            var remaining = Math.Min(count, capacity - written);
            if (header < 128)
            {
                if (position + remaining > input.Length) throw new InvalidDataException("Truncated literal block.");
                input.Slice(position, remaining).CopyTo(output.AsSpan(written));
                position += remaining;
            }
            else
            {
                if (position >= input.Length) throw new InvalidDataException("Truncated repeat block.");
                output.AsSpan(written, remaining).Fill(input[position++]);
            }
            written += remaining;
            if (written == capacity) return new(output, palette);
        }
        throw new InvalidDataException("Missing squeezed-file terminator.");
    }
}