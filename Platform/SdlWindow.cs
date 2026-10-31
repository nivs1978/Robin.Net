// Project: Robin.Net
// File: SdlWindow.cs
// License: GNU General Public License version 3 (see LICENSE)

using System.Diagnostics;

namespace RobinHood.Platform;

public sealed class SdlWindow : IDisposable
{
    const uint PixelFormat = 0x16362004;
    IntPtr window;
    IntPtr renderer;
    IntPtr texture;
    bool initialized;
    bool fullscreen;

    public SdlWindow()
    {
        try
        {
            Check(Sdl2.SDL_Init(0x20));
            initialized = true;
            Sdl2.SDL_SetHint("SDL_RENDER_SCALE_QUALITY", "0");
            window = Require(Sdl2.SDL_CreateWindow("The Adventures of Robin Hood - Native C# Port", 0x2fff0000, 0x2fff0000, 960, 600, 0x24u));
            Check(Sdl2.SDL_SetWindowFullscreen(window, 0x1001u));
            fullscreen = true;
            Sdl2.SDL_ShowCursor(0);
            Sdl2.SDL_SetWindowMinimumSize(window, 320, 200);
            renderer = Sdl2.SDL_CreateRenderer(window, -1, 6u);
            if (renderer == IntPtr.Zero) renderer = Require(Sdl2.SDL_CreateRenderer(window, -1, 1));
            Check(Sdl2.SDL_RenderSetLogicalSize(renderer, 320, 200));
            Check(Sdl2.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255));
            texture = Require(Sdl2.SDL_CreateTexture(renderer, PixelFormat, 1, 320, 200));
        }
        catch { Dispose(); throw; }
    }

    public void Run(Func<double, uint[]?> update, Action<GameInput> input)
    {
        var timer = Stopwatch.StartNew();
        var previous = timer.Elapsed.TotalSeconds;
        var running = true;
        while (running)
        {
            while (Sdl2.SDL_PollEvent(out var current) != 0)
            {
                if (current.Type == 0x100) running = false;
                if (current.Type == 0x301) input(new(InputKind.KeyUp, current.Key, 0, 0));
                if (current.Type == 0x300 && current.Repeat == 0)
                {
                    if (current.Key == 1073741892) { fullscreen = !fullscreen; Check(Sdl2.SDL_SetWindowFullscreen(window, fullscreen ? 0x1001u : 0)); }
                    input(new(InputKind.Key, current.Key, 0, 0));
                }
                if (current.Type is 0x401 or 0x402 or 0x400)
                {
                    input(new(current.Type == 0x400 ? InputKind.Motion : current.Type == 0x401 ? InputKind.Press : InputKind.Release, current.Button, current.X, current.Y));
                }
            }
            if (!running) break;
            var now = timer.Elapsed.TotalSeconds;
            var pixels = update(Math.Min(now - previous, 0.1));
            if (pixels is null) break;
            previous = now;
            Check(Sdl2.SDL_UpdateTexture(texture, IntPtr.Zero, pixels, 320 * 4));
            Check(Sdl2.SDL_RenderClear(renderer));
            Check(Sdl2.SDL_RenderCopy(renderer, texture, IntPtr.Zero, IntPtr.Zero));
            Sdl2.SDL_RenderPresent(renderer);
            Sdl2.SDL_Delay(1);
        }
    }

    static void Check(int result) { if (result < 0) throw new InvalidOperationException(Sdl2.Error); }
    static IntPtr Require(IntPtr handle) => handle != IntPtr.Zero ? handle : throw new InvalidOperationException(Sdl2.Error);

    public void Dispose()
    {
        if (initialized) Sdl2.SDL_ShowCursor(1);
        if (texture != IntPtr.Zero) { Sdl2.SDL_DestroyTexture(texture); texture = IntPtr.Zero; }
        if (renderer != IntPtr.Zero) { Sdl2.SDL_DestroyRenderer(renderer); renderer = IntPtr.Zero; }
        if (window != IntPtr.Zero) { Sdl2.SDL_DestroyWindow(window); window = IntPtr.Zero; }
        if (initialized) { Sdl2.SDL_Quit(); initialized = false; }
    }
}

public enum InputKind { Key, KeyUp, Motion, Press, Release }
public readonly record struct GameInput(InputKind Kind, int Value, int X, int Y);