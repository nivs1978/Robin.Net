// Project: Robin.Net
// File: GameRenderer.cs
// License: GNU General Public License version 3 (see LICENSE)

using RobinHood.Assets;
using RobinHood.Engine;

namespace RobinHood.Graphics;

public sealed class GameRenderer(GameWorld world)
{
    public const int ViewLeft = 64;
    public const int ViewTop = 16;
    public IndexedSurface Frame { get; } = new(320, 200);
    public IndexedSurface View { get; } = new(256, 176);
    public List<ProjectedMobile> VisibleMobiles { get; } = [];
    readonly uint[] pixels = new uint[64000];
    readonly uint[] palette = new uint[256];
    int paletteVersion = -1;
    int paletteLevel = -1;

    public uint[] Render()
    {
        UpdatePalette();
        world.Screen.Pixels.CopyTo(Frame.Pixels, 0);
        if (world.FullScreenImage)
        {
            if (world.Scene == GameWorld.AerialScreen)
            {
                DrawAerialBlips();
                DrawCursor();
            }
            return ConvertFrame();
        }
        for (var row = 0; row < 176; row++) Frame.Pixels.AsSpan((row + ViewTop) * 320 + ViewLeft, 256).CopyTo(View.Pixels.AsSpan(row * 256));
        VisibleMobiles.Clear();
        foreach (var mobile in world.Mobiles.Reverse())
        {
            if (!world.ScreenPosition(mobile, out var left, out var top)) continue;
            VisibleMobiles.Add(new(mobile, mobile.CellX - world.ViewX, mobile.CellY - world.ViewY, left, top));
        }
        VisibleMobiles.Sort((first, second) =>
        {
            var order = first.CubeY.CompareTo(second.CubeY);
            if (order == 0) order = first.CubeX.CompareTo(second.CubeX);
            if (order == 0) order = ((sbyte)first.Mobile.DrawnZ).CompareTo((sbyte)second.Mobile.DrawnZ);
            if (order == 0) order = ((sbyte)first.Top).CompareTo((sbyte)second.Top);
            return order;
        });
        for (var cubeY = 0; cubeY < 8; cubeY++)
        for (var cubeX = 0; cubeX < 8; cubeX++) PlotCube(cubeX, cubeY, 0);
        for (var cubeY = 0; cubeY < 8; cubeY++)
        for (var cubeX = 0; cubeX < 8; cubeX++)
        {
            PlotCube(cubeX, cubeY, 1);
            var structure = world.Cube(world.ViewX + cubeX, world.ViewY + cubeY, 1);
            var indoors = structure != 255 && (world.Program.CubeFlags[structure] & 16) == 0;
            foreach (var mobile in VisibleMobiles.Where(mobile => mobile.CubeX == cubeX && mobile.CubeY == cubeY))
            {
                if (mobile.Mobile.Id == world.Speaking) DrawBubbleLine(mobile.Left, mobile.Top);
                if (!indoors) PlotMobile(mobile);
            }
            PlotCube(cubeX, cubeY, 2);
        }
        Frame.Blit(View.Pixels, 256, 176, ViewLeft, ViewTop, false);
        for (var index = 0; index < world.Program.Buttons.Length; index++)
        {
            var button = world.Program.Buttons[index];
            var sprite = index + world.ButtonStates[index] * 20;
            if (sprite < 100) Frame.Blit(world.Assets.Icons.AsSpan(sprite * 256, 256), 16, 16, button.X, button.Y);
        }
        DrawCursor();
        return ConvertFrame();
    }

    void UpdatePalette()
    {
        if (paletteVersion == world.PaletteVersion && paletteLevel == world.PaletteLevel) return;
        paletteVersion = world.PaletteVersion;
        paletteLevel = world.PaletteLevel;
        for (var index = 0; index < 256; index++)
        {
            var red = Expand(Scale(world.Palette[index * 3]));
            var green = Expand(Scale(world.Palette[index * 3 + 1]));
            var blue = Expand(Scale(world.Palette[index * 3 + 2]));
            palette[index] = 0xff000000u | (uint)(red << 16 | green << 8 | blue);
        }
    }

    // The fade scales each 6-bit component by level / 256 (mul bl; keep ah).
    int Scale(byte component) => paletteLevel >= 256 ? component : component * paletteLevel >> 8;

    uint[] ConvertFrame()
    {
        for (var index = 0; index < pixels.Length; index++) pixels[index] = palette[Frame.Pixels[index]];
        return pixels;
    }

    void DrawCursor() => Frame.Blit(world.Assets.Icons.AsSpan((80 + world.CursorMode) * 256, 256), 16, 16, world.CursorX, world.CursorY);

    // sub_56AE: one pixel per mobile on the aerial map, four columns and three rows per cube.
    void DrawAerialBlips()
    {
        foreach (var mobile in world.Mobiles)
        {
            if ((mobile.Type & 2) != 0 || mobile.CellY == 0xff) continue;
            var x = mobile.CellX * 4 + 1;
            var y = mobile.CellY * 3 + 1;
            if (x < 320 && y < 200) Frame.Pixels[y * 320 + x] = mobile.Speed;
        }
    }

    // plot_bubble_line: a wedge of caption colour from the top of the view down to the speaker's sprite.
    void DrawBubbleLine(int left, int top)
    {
        var column = left + 8;
        var height = top;
        do VerticalLine(column--, height); while ((height >>= 1) > 0);
        column = left + 9;
        height = top >> 1;
        for (; height > 0; height >>= 1) VerticalLine(column++, height);
    }
    void VerticalLine(int x, int bottom)
    {
        if (x is < 0 or >= 256) return;
        for (var y = 0; y <= Math.Min(bottom, 175); y++) View.Pixels[y * 256 + x] = 0x11;
    }

    void PlotCube(int cubeX, int cubeY, int level)
    {
        var tile = (int)world.Cube(cubeX + world.ViewX, cubeY + world.ViewY, level);
        if (tile == 255) return;
        if (tile >= 60) throw new InvalidDataException($"Invalid map cube {tile}.");
        if (level > 0 && (world.Program.CubeFlags[tile] & 128) != 0) tile += world.AnimationCount;
        if (tile >= 60) throw new InvalidDataException("Animated cube exceeds the original cube set.");
        View.Blit(world.Assets.Cubes(world.CubeSet).AsSpan(tile * 1024, 1024), 32, 32,
            (7 + cubeX - cubeY) * 16, (4 + cubeX + cubeY - level * 2) * 8);
    }

    void PlotMobile(ProjectedMobile projected)
    {
        var mobile = projected.Mobile;
        if (mobile.DrawnBase == ushort.MaxValue) return;
        var reflect = (mobile.DrawnDirection & 2) != 0;
        if (mobile.PuffFrame != 0xff)
        {
            View.Blit(world.Assets.Icons.AsSpan((0x52 + mobile.PuffFrame) * 256, 256), 16, 16, projected.Left, projected.Top, true, reflect);
            return;
        }
        var sprite = mobile.DrawnBase + mobile.DrawnPose + ((mobile.DrawnDirection & 1) != 0 ? mobile.DrawnAbout : 0);
        var bank = sprite >= 240 ? world.Assets.Men2 : world.Assets.Men;
        if (sprite >= 240) sprite -= 240;
        if (sprite >= 240) throw new InvalidDataException("Mobile sprite exceeds the original sprite bank.");
        View.Blit(bank.AsSpan(sprite * 256, 256), 16, 16, projected.Left, projected.Top, true, reflect);
    }

    static int Expand(int component) => component << 2 | component >> 4;
}

public sealed record ProjectedMobile(Mobile Mobile, int CubeX, int CubeY, int Left, int Top);
