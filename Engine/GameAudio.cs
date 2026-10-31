// Project: Robin.Net
// File: GameAudio.cs
// License: GNU General Public License version 3 (see LICENSE)

using System.Runtime.InteropServices;
using RobinHood.Assets;
using RobinHood.Platform;

namespace RobinHood.Engine;

public sealed class GameAudio : IDisposable
{
    private const int SampleRate = 44100;
    private const int OutputGain = 10;
    private const uint AudioSubsystem = 0x10;
    private const int SoundCount = 40;
    private readonly object sync = new();
    private readonly List<SoundSource> sources = [];
    private AdLibSequence[] sequences = [];
    private byte[] overlay = [];
    private SdlAudioCallback? callback;
    private uint device;
    private bool initialized;
    private int listenerX;
    private int listenerY;
    private AdLibPlayer? music;
    private AdLibPlayer? transient;
    private AdLibPlayer? ambient;
    private SoundSource? currentSource;
    private int transientVolume = 128;
    private int ambientVolume = 128;
    private short[] mixed = [];
    private short[] scratch = [];
    private int callbackCount;

    public int SoundRequests { get; private set; }
    public int MusicRequests { get; private set; }
    internal bool Enabled => device != 0;
    internal int CallbackCount => Volatile.Read(ref callbackCount);

    public void Initialize(string assets)
    {
        string musicPath = Path.Combine(assets, "ROBIN.MUS");
        string overlayPath = Path.Combine(assets, "ADLIB.OVL");
        if (!File.Exists(musicPath) || !File.Exists(overlayPath))
        {
            Console.Error.WriteLine("Audio disabled: ROBIN.MUS and ADLIB.OVL are required beside the executable.");
            return;
        }

        byte[] sourceOverlay = File.ReadAllBytes(overlayPath);
        if (sourceOverlay.Length < 0x18c + SoundCount || !sourceOverlay.AsSpan(0x60, 10).SequenceEqual("ROBIN.MUS\0"u8))
            throw new InvalidDataException("Unsupported ADLIB.OVL sound table.");
        var midi = MusArchive.Read(File.ReadAllBytes(musicPath));
        for (int i = 0; i < SoundCount; i++)
        {
            int resource = sourceOverlay[0x18c + i];
            if (resource != 0xff && resource >= midi.Count)
                throw new InvalidDataException($"ADLIB.OVL sound {i} refers to missing MUS resource {resource}.");
        }
        sequences = midi.Select(data => new AdLibSequence(data, SampleRate)).ToArray();
        overlay = sourceOverlay;

        if (Sdl2.SDL_InitSubSystem(AudioSubsystem) < 0)
            throw new InvalidOperationException($"Could not initialize SDL audio: {Sdl2.Error}");
        initialized = true;
        callback = RenderAudio;
        var desired = new SdlAudioSpec
        {
            Frequency = SampleRate,
            Format = 0x8010,
            Channels = 2,
            Samples = 1024,
            Callback = Marshal.GetFunctionPointerForDelegate(callback)
        };
        device = Sdl2.SDL_OpenAudioDevice(IntPtr.Zero, 0, ref desired, out _, 0);
        if (device == 0)
            throw new InvalidOperationException($"Could not open SDL audio device: {Sdl2.Error}");
        Sdl2.SDL_PauseAudioDevice(device, 0);
    }

    public void StartSound(int sound, int emitter, int cubeX, int cubeY)
    {
        lock (sync)
        {
            SoundRequests++;
            if (!Supported(sound) || device == 0) return;
            if (overlay[0x13c + sound] == 0)
            {
                int volume = Audible(sound, cubeX, cubeY);
                if (volume == 0 || (transient is not null && !transient.Finished)) return;
                transient = CreatePlayer(sound, false);
                transientVolume = volume;
                return;
            }
            sources.RemoveAll(source => source.Emitter == emitter);
            if (currentSource?.Emitter == emitter) { ambient = null; currentSource = null; }
            if (sources.Count < 20) sources.Add(new SoundSource(sound, emitter, cubeX, cubeY));
            RefreshAmbient();
        }
    }

    public void StopSound(int emitter)
    {
        lock (sync)
        {
            sources.RemoveAll(source => source.Emitter == emitter);
            if (currentSource?.Emitter == emitter) { ambient = null; currentSource = null; }
            RefreshAmbient();
        }
    }

    public void UpdateListener(int cubeX, int cubeY)
    {
        lock (sync) { listenerX = cubeX; listenerY = cubeY; RefreshAmbient(); }
    }

    public void PlayMusic(int sound)
    {
        lock (sync)
        {
            MusicRequests++;
            if (!Supported(sound) || device == 0) return;
            transient = null;
            ambient = null;
            currentSource = null;
            music = CreatePlayer(sound, (overlay[0x164 + sound] & 0x80) != 0);
        }
    }

    public void StopMusic()
    {
        lock (sync) { music = null; transient = null; ambient = null; currentSource = null; }
    }

    private bool Supported(int sound) => sound >= 0 && sound < SoundCount && overlay.Length >= 0x18c + SoundCount && overlay[0x18c + sound] != 0xff;

    private AdLibPlayer CreatePlayer(int sound, bool loop) => new(sequences[overlay[0x18c + sound]], SampleRate, loop);

    private int Audible(int sound, int x, int y)
    {
        int distance = x >= listenerX && x < listenerX + 8 && y >= listenerY && y < listenerY + 8 ? 2
            : x >= listenerX - 6 && x < listenerX + 14 && y >= listenerY - 6 && y < listenerY + 14 ? 1 : 0;
        int range = overlay[0x114 + sound];
        if (range == 0) distance &= 2;
        else if (range == 2) distance |= 1;
        else if (range == 3) distance = 3;
        return distance == 0 ? 0 : distance == 1 ? 90 : 128;
    }

    private void RefreshAmbient()
    {
        if (overlay.Length == 0 || device == 0 || music is not null) return;
        if (ambient is not null && ambient.Finished && currentSource is not null)
        {
            sources.Remove(currentSource);
            currentSource = null;
            ambient = null;
        }
        SoundSource? best = null;
        int volume = 0;
        foreach (var source in sources)
        {
            int audible = Audible(source.Sound, source.X, source.Y);
            if (audible == 0 || (best is not null && overlay[0xec + source.Sound] <= overlay[0xec + best.Sound])) continue;
            best = source;
            volume = audible;
        }
        if (best != currentSource)
        {
            currentSource = best;
            ambient = best is null ? null : CreatePlayer(best.Sound, (overlay[0x164 + best.Sound] & 0x80) != 0);
        }
        ambientVolume = volume;
    }

    private void RenderAudio(IntPtr userdata, IntPtr stream, int length)
    {
        Interlocked.Increment(ref callbackCount);
        int count = length / sizeof(short);
        if (mixed.Length != count) { mixed = new short[count]; scratch = new short[count]; }
        Array.Clear(mixed);
        try
        {
            lock (sync)
            {
                Mix(music, 128);
                Mix(transient, transientVolume);
                Mix(ambient, ambientVolume);
                if (music?.Finished == true) music = null;
                if (transient?.Finished == true) transient = null;
                if (ambient?.Finished == true) RefreshAmbient();
            }
            ApplyOutputGain(mixed);
        }
        catch (Exception ex)
        {
            Array.Clear(mixed);
            Console.Error.WriteLine($"Audio playback failed: {ex.Message}");
        }
        Marshal.Copy(mixed, 0, stream, count);
    }

    internal static void ApplyOutputGain(Span<short> samples)
    {
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)Math.Clamp(samples[i] * OutputGain, short.MinValue, short.MaxValue);
    }

    private void Mix(AdLibPlayer? player, int volume)
    {
        if (player is null) return;
        player.Render(scratch);
        for (int i = 0; i < mixed.Length; i++)
            mixed[i] = (short)Math.Clamp(mixed[i] + scratch[i] * volume / 128, short.MinValue, short.MaxValue);
    }

    public void Dispose()
    {
        if (device != 0) { Sdl2.SDL_CloseAudioDevice(device); device = 0; }
        if (initialized) { Sdl2.SDL_QuitSubSystem(AudioSubsystem); initialized = false; }
        callback = null;
    }

    private sealed record SoundSource(int Sound, int Emitter, int X, int Y);
}
