// Project: Robin.Net
// File: IndexedSurface.cs
// License: GNU General Public License version 3 (see LICENSE)

namespace RobinHood.Graphics;

public sealed class IndexedSurface(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = new byte[checked(width * height)];

    public void Blit(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, int left, int top, bool transparent = true, bool reflect = false)
    {
        if (source.Length < checked(sourceWidth * sourceHeight)) throw new ArgumentException("Truncated sprite.");
        for (var row = Math.Max(0, -top); row < Math.Min(sourceHeight, Height - top); row++)
        for (var column = Math.Max(0, -left); column < Math.Min(sourceWidth, Width - left); column++)
        {
            var pixel = source[row * sourceWidth + (reflect ? sourceWidth - column - 1 : column)];
            if (!transparent || pixel != 0) Pixels[(top + row) * Width + left + column] = pixel;
        }
    }

    public static byte[] Pad(byte[] source, int size)
    {
        if (source.Length > size) throw new InvalidDataException("Image exceeds the original buffer capacity.");
        var result = new byte[size];
        source.CopyTo(result, 0);
        return result;
    }
}