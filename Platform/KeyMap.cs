// Project: Robin.Net
// File: KeyMap.cs
// License: GNU General Public License version 3 (see LICENSE)

namespace RobinHood.Platform;

// SDL keycodes to the PC XT scancodes the original keyboard handler stored.
public static class KeyMap
{
    static readonly byte[] Letters =
    [
        0x1e, 0x30, 0x2e, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32, 0x31, 0x18, 0x19, 0x10, 0x13, 0x1f, 0x14, 0x16, 0x2f, 0x11, 0x2d, 0x15, 0x2c
    ];

    public static byte ToScancode(int key)
    {
        if (key is >= 'a' and <= 'z') return Letters[key - 'a'];
        if (key is >= '1' and <= '9') return (byte)(key - '1' + 2);
        return key switch
        {
            '0' => 0x0b, 27 => 0x01, '-' => 0x0c, '=' => 0x0d, 8 => 0x0e, 9 => 0x0f, '[' => 0x1a, ']' => 0x1b, 13 => 0x1c,
            ';' => 0x27, '\'' => 0x28, '`' => 0x29, '\\' => 0x2b, ',' => 0x33, '.' => 0x34, '/' => 0x35, ' ' => 0x39,
            1073742048 or 1073742052 => 0x1d, 1073742049 => 0x2a, 1073742053 => 0x36, 1073742050 or 1073742054 => 0x38,
            >= 1073741882 and <= 1073741891 => (byte)(0x3b + key - 1073741882), 1073741892 => 0x57, 1073741893 => 0x58,
            1073741906 => 0x48, 1073741904 => 0x4b, 1073741903 => 0x4d, 1073741905 => 0x50,
            _ => 0
        };
    }
}
