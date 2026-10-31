// Project: Robin.Net
// File: GameAssets.cs
// License: GNU General Public License version 3 (see LICENSE)

using RobinHood.Graphics;

namespace RobinHood.Assets;

public sealed class GameAssets
{
    public string DirectoryPath { get; }
    public byte[] Men { get; }
    public byte[] Men2 { get; }
    public byte[] Icons { get; }
    public byte[] Charset { get; }
    public byte[] Map { get; }
    public GameProgram Program { get; }
    readonly Dictionary<int, byte[]> cubeSets = [];

    public GameAssets(string directory, string language = "E")
    {
        DirectoryPath = Path.GetFullPath(directory);
        if (!"EFGI".Contains(language, StringComparison.Ordinal) || language.Length != 1) throw new ArgumentException("Language must be E, F, G or I.");
        Program = GameProgram.Load(Resolve(language + "RULES.PRG"));
        Men = LoadSprites("MEN.VGA", 61440);
        Men2 = LoadSprites("MEN2.VGA", 61440);
        Icons = LoadSprites("IDEOGRAM.VGA", 25600);
        Charset = LoadSprites("ISOCHARS.VGA", 4096);
        Map = File.ReadAllBytes(Resolve("ISOMAP.DTA"));
        if (Map.Length != 64 * 64 * 4) throw new InvalidDataException("Expected Robin Hood's 64 x 64 map.");
    }

    public string Resolve(string filename)
    {
        if (filename != Path.GetFileName(filename) || filename.Contains(':')) throw new InvalidDataException("Asset name must be a filename.");
        return Path.Combine(DirectoryPath, filename);
    }

    byte[] LoadSprites(string filename, int capacity) => IndexedSurface.Pad(SqueezedFile.Load(Resolve(filename), false, capacity).Pixels, capacity);

    public byte[] Cubes(int set)
    {
        if (set is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(set));
        if (!cubeSets.TryGetValue(set, out var pixels)) cubeSets[set] = pixels = LoadSprites($"CUBES{set}.GFX", 61440);
        return pixels;
    }

    public SqueezedFile Screen(string filename)
    {
        var image = SqueezedFile.Load(Resolve(filename), true, 64000);
        if (image.Palette!.Any(component => component > 63)) throw new InvalidDataException("Invalid VGA palette.");
        return image with { Pixels = IndexedSurface.Pad(image.Pixels, 64000) };
    }
}