// Project: Robin.Net
// File: Sdl2.cs
// License: GNU General Public License version 3 (see LICENSE)

using System.Runtime.InteropServices;

namespace RobinHood.Platform;

internal static class Sdl2
{
    const string Library = "SDL2.dll";
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_Init(uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_InitSubSystem(uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_QuitSubSystem(uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_Quit();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetError();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint SDL_OpenAudioDevice(IntPtr device, int isCapture, ref SdlAudioSpec desired, out SdlAudioSpec obtained, int allowedChanges);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_PauseAudioDevice(uint device, int pause);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_CloseAudioDevice(uint device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int x, int y, int width, int height, uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_DestroyWindow(IntPtr window);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_SetWindowMinimumSize(IntPtr window, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_SetWindowFullscreen(IntPtr window, uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_ShowCursor(int toggle);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_CreateRenderer(IntPtr window, int index, uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_DestroyRenderer(IntPtr renderer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_RenderSetLogicalSize(IntPtr renderer, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_SetRenderDrawColor(IntPtr renderer, byte red, byte green, byte blue, byte alpha);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_RenderClear(IntPtr renderer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_RenderCopy(IntPtr renderer, IntPtr texture, IntPtr source, IntPtr destination);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_RenderPresent(IntPtr renderer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_CreateTexture(IntPtr renderer, uint format, int access, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_DestroyTexture(IntPtr texture);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_UpdateTexture(IntPtr texture, IntPtr rectangle, [In] uint[] pixels, int pitch);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_PollEvent(out SdlEvent currentEvent);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_Delay(uint milliseconds);
    internal static string Error => Marshal.PtrToStringUTF8(SDL_GetError()) ?? "Unknown SDL error";
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SdlAudioCallback(IntPtr userdata, IntPtr stream, int length);

[StructLayout(LayoutKind.Sequential)]
internal struct SdlAudioSpec
{
    public int Frequency;
    public ushort Format;
    public byte Channels;
    public byte Silence;
    public ushort Samples;
    public ushort Padding;
    public uint Size;
    public IntPtr Callback;
    public IntPtr UserData;
}

[StructLayout(LayoutKind.Explicit, Size = 56)]
internal struct SdlEvent
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(13)] public byte Repeat;
    [FieldOffset(16)] public byte Button;
    [FieldOffset(20)] public int Key;
    [FieldOffset(20)] public int X;
    [FieldOffset(24)] public int Y;
}