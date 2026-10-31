// Project: Robin.Net
// File: Display.cs
// License: GNU General Public License version 3 (see LICENSE)

using RobinHood.Assets;
using RobinHood.Graphics;

namespace RobinHood.Engine;

// The persistent screen (VGA memory), direct draws, palette fades, puffs, the status gauge and the aerial view.
public sealed partial class GameWorld
{
    public const string GameScreen = "SCREEN.GFX";
    public const string AerialScreen = "AERIAL.GFX";
    const double FadeStepSeconds = 2 / 70.0;
    const int FadeSteps = 32;
    const int BubbleLeft = 66, BubbleWidth = 252, BubbleHeight = 16;
    static readonly int[] BubbleWidths = [0xf4, 0xf8, 0xfa, 0xfa, 0xfc, 0xfc, 0xfc, 0xfc, 0xfc, 0xfc, 0xfc, 0xfc, 0xfa, 0xfa, 0xf8, 0xf4];

    sealed record FadeStep(FadeKind Kind, Action? Run = null);
    enum FadeKind { Out, In, Do }

    readonly byte[] bubbleBackdrop = new byte[BubbleWidth * BubbleHeight];
    readonly Queue<FadeStep> fadeSteps = [];
    bool fadeStarted;
    bool interruptFade;
    int fadeIndex;
    double fadeClock;

    public IndexedSurface Screen { get; } = new(320, 200);
    public byte[] Palette { get; } = new byte[768];
    public int PaletteVersion { get; private set; }
    public string Scene { get; private set; } = "";
    public bool Faded { get; private set; } = true;
    // Brightness numerator of the fade in progress (0-255), or 256 for the unscaled palette.
    public int PaletteLevel { get; private set; }
    public bool FadeBusy => fadeSteps.Count > 0;
    public Mobile? StatusMobile { get; private set; }
    public int StatusIndex { get; private set; }
    public int StatusX { get; private set; }
    public int StatusY { get; private set; }
    public byte StatusCurrent { get; private set; }

    // Loading a full-screen image replaces the screen and (for 256 colour images) the palette.
    public void LoadScene(string name)
    {
        var image = Assets.Screen(name);
        image.Pixels.CopyTo(Screen.Pixels, 0);
        image.Palette!.CopyTo(Palette, 0);
        PaletteVersion++;
        Scene = name;
        if (name == GameScreen)
            for (var row = 0; row < BubbleHeight; row++)
                Screen.Pixels.AsSpan(row * 320 + BubbleLeft, BubbleWidth).CopyTo(bubbleBackdrop.AsSpan(row * BubbleWidth, BubbleWidth));
    }

    // plotchar: glyphs are copied raw (colour zero included) in 4x8 cells.
    public void PrintText(string text, int x, int y)
    {
        foreach (var character in text)
        {
            if (character < 128) Screen.Blit(Assets.Charset.AsSpan(character * 32, 32), 4, 8, x, y, false);
            x += 4;
        }
    }

    // draw_bubble / print_speech: a rounded caption bar in the top 16 rows with up to two centred lines.
    public void ShowSpeech(string text)
    {
        for (var row = 0; row < BubbleHeight; row++)
        {
            var half = BubbleWidths[row] >> 1;
            Screen.Pixels.AsSpan(row * 320 + 192 - half, half << 1).Fill(0x11);
        }
        var lines = text.Split('|');
        for (var index = 0; index < Math.Min(2, lines.Length); index++)
            PrintText(lines[index], 70 + (0x3d - lines[index].Length) * 2, lines.Length > 1 ? index * 8 : 4);
    }

    // sub_51E7: put back the title bar saved when the game screen was loaded.
    public void ClearSpeech()
    {
        for (var row = 0; row < BubbleHeight; row++)
            bubbleBackdrop.AsSpan(row * BubbleWidth, BubbleWidth).CopyTo(Screen.Pixels.AsSpan(row * 320 + BubbleLeft, BubbleWidth));
    }

    // plot_bar: four rows of colour 2; the '-' style grows leftwards past 35.
    public void PlotBar(int x, int y, int length, int style)
    {
        if (style == '-')
        {
            x += 35;
            length -= 35;
            if (length < 0) { x += length; length = -length; }
        }
        if (length == 0) length = 1;
        for (var row = 0; row < 4; row++)
            for (var column = 0; column < length; column++)
                if (x + column is >= 0 and < 320 && y + row is >= 0 and < 200) Screen.Pixels[(y + row) * 320 + x + column] = 2;
    }

    // icon_clear + icon_plot for a mobile's base sprite (a_showobj).
    public void ShowObjectIcon(Mobile mobile, int x, int y)
    {
        for (var row = 0; row < 16; row++)
            for (var column = 0; column < 16; column++)
                if (x + column is >= 0 and < 320 && y + row is >= 0 and < 200) Screen.Pixels[(y + row) * 320 + x + column] = 0x10;
        var sprite = (int)mobile.BaseSprite;
        var bank = sprite >= 240 ? Assets.Men2 : Assets.Men;
        if (sprite >= 240) sprite -= 240;
        if (sprite < 240) Screen.Blit(bank.AsSpan(sprite * 256, 256), 16, 16, x, y);
    }

    public void SetStatusIndicator(Mobile mobile, int attribute, int x, int y)
    {
        StatusMobile = mobile;
        StatusIndex = attribute;
        StatusX = x;
        StatusY = y;
    }

    // sub_57FF: step the gauge one unit (4 rows) per tick toward attribute * 25 / 256.
    void UpdateStatusIndicator()
    {
        if (StatusMobile == null) return;
        var target = StatusMobile.Attributes[StatusIndex] * 25 >> 8;
        if (target == StatusCurrent) return;
        var increasing = target > StatusCurrent;
        StatusCurrent = (byte)(StatusCurrent + (increasing ? 1 : -1));
        byte colour = increasing ? (byte)0x96 : (byte)0x28;
        var row = StatusY;
        for (var step = 0; step < StatusCurrent * 4; step++, row--) FillRow(row, colour);
        for (var step = 0; step < (25 - StatusCurrent) * 4; step++, row--) FillRow(row, 0x17);
    }

    void FillRow(int y, byte colour)
    {
        if (y is < 0 or >= 200) return;
        for (var column = 0; column < 3; column++)
            if (StatusX + column is >= 0 and < 320) Screen.Pixels[y * 320 + StatusX + column] = colour;
    }

    public void ResetStatusIndicator() => StatusCurrent = 0;

    // Puffs replace a drawn mobile's sprite with icons 0x52..0x56 for five frames, counting down each time it is drawn.
    void UpdatePuffs()
    {
        foreach (var mobile in Mobiles)
        {
            mobile.PuffFrame = 0xff;
            if (!ScreenPosition(mobile, out _, out _) || mobile.DrawnBase == ushort.MaxValue || IsIndoors(mobile)) continue;
            mobile.PuffFrame = mobile.Puff;
            if (mobile.Puff != 0xff) mobile.Puff--;
        }
    }

    public bool IsIndoors(Mobile mobile)
    {
        var offset = (mobile.CellY * 64 + mobile.CellX) * 4 + 1;
        if (offset >= Map.Length || Map[offset] == 0xff) return false;
        return Map[offset] < Program.CubeFlags.Length && (Program.CubeFlags[Map[offset]] & 0x10) == 0;
    }

    int lastAnimate, iconPhase;

    // sub_7DB4: while a wait runs, each change of animate_count draws the next of eight frames of every registered icon (transparent, persistent).
    public void AnimateIcons()
    {
        if (AnimationCount == lastAnimate) return;
        lastAnimate = AnimationCount;
        foreach (var (_, animation) in IntroAnimations.OrderBy(entry => entry.Key))
            Screen.Blit(Assets.Icons.AsSpan(animation.Frames[iconPhase] * 256, 256), 16, 16, animation.X, animation.Y);
        iconPhase = iconPhase + 1 & 7;
    }

    // Palette fades run on the simulation clock and freeze everything else while they last.
    public void FadeOut() { fadeSteps.Enqueue(new(FadeKind.Out)); AdvanceFade(0); }
    public void FadeIn() { fadeSteps.Enqueue(new(FadeKind.In)); AdvanceFade(0); }
    public void AfterFade(Action run) { fadeSteps.Enqueue(new(FadeKind.Do, run)); AdvanceFade(0); }

    void AdvanceFade(double elapsed)
    {
        while (fadeSteps.Count > 0)
        {
            var step = fadeSteps.Peek();
            if (step.Kind == FadeKind.Do) { fadeSteps.Dequeue(); step.Run!(); continue; }
            if (!fadeStarted)
            {
                fadeStarted = true;
                fadeIndex = 0;
                fadeClock = 0;
                if (step.Kind == FadeKind.Out ? Faded : !Faded) { FinishFadeStep(); continue; }
                if (step.Kind == FadeKind.Out) { Faded = true; IntroAnimations.Clear(); }
                else Faded = false;
                PaletteLevel = FadeLevel(step.Kind);
            }
            var needed = FadeStepSeconds - fadeClock;
            if (elapsed < needed) { fadeClock += elapsed; return; }
            elapsed -= needed;
            fadeClock = 0;
            if (++fadeIndex >= FadeSteps) { FinishFadeStep(); continue; }
            PaletteLevel = FadeLevel(step.Kind);
        }
    }

    int FadeLevel(FadeKind kind) => kind == FadeKind.Out ? 0xf8 - 8 * fadeIndex : 4 + 8 * fadeIndex;

    void FinishFadeStep()
    {
        var step = fadeSteps.Dequeue();
        fadeStarted = false;
        PaletteLevel = step.Kind == FadeKind.Out ? 0 : 256;
        if (step.Kind == FadeKind.Out) Faded = true; else Faded = false;
    }

    // sub_7BFC: fade out, show the aerial map with a crosshair cursor, fade in, then switch to the aerial update path.
    public void ShowAerialView()
    {
        Speaking = -1;
        Frozen = true;
        FadeOut();
        AfterFade(() =>
        {
            CursorMode = 1;
            LoadScene(AerialScreen);
            FullScreenImage = true;
        });
        FadeIn();
        AfterFade(() =>
        {
            AerialView = true;
            ClearKeys();
            Frozen = false;
        });
    }

    // sub_5731: any key, or a click that also reports the map cell under it, leaves the aerial view.
    bool CheckAerialExit()
    {
        if (KeyAvailable) TakeKey();
        else if (ClickPending)
        {
            ConsumeClick();
            MobileHit = -1;
            MapHit = -1;
            var x = ClickX >> 2;
            var y = ClickY / 3;
            if (x < 64 && y < 0x40)
            {
                MapHit = x << 8 | y;
                PendingInput = 5;
            }
        }
        else return false;
        AerialView = false;
        interruptFade = true;
        ReturnToGameScreen(false);
        return true;
    }

    // sub_7D3B / sub_54BC: fade out, redraw the game screen, fade in.
    public void ReturnToGameScreen(bool unfreeze)
    {
        FadeOut();
        AfterFade(() =>
        {
            AerialView = false;
            CursorMode = 0;
            ResetStatusIndicator();
            DeselectAll();
            LoadScene(GameScreen);
            FullScreenImage = false;
            IntroAnimations.Clear();
            LayFloor();
            Audio.UpdateListener(ViewX, ViewY);
        });
        FadeIn();
        if (unfreeze) AfterFade(() => Frozen = false);
    }
}

