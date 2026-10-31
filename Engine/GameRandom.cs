// Project: Robin.Net
// File: GameRandom.cs
// License: GNU General Public License version 3 (see LICENSE)

namespace RobinHood.Engine;

public sealed class GameRandom(ushort seed)
{
    ushort state = seed;

    public int Next(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        if (maximum == 0) return 0;
        var product = (uint)state * 0x4e35;
        state = unchecked((ushort)product);
        return (int)((product >> 16) % maximum);
    }
}