// Project: Robin.Net
// File: GameWorld.cs
// License: GNU General Public License version 3 (see LICENSE)

using RobinHood.Assets;

namespace RobinHood.Engine;

public sealed partial class GameWorld
{
    public const double UpdateInterval = 0x5d37 * 11 / 1193182.0;
    double pendingTime;
    public GameAssets Assets { get; }
    public GameProgram Program => Assets.Program;
    public Mobile[] Mobiles => Program.Mobiles;
    public byte[] Map { get; }
    public int ViewX { get; private set; }
    public int ViewY { get; private set; }
    // cubeset (offset 6): 0 until a script chooses a set, then the file letter '0'-'3'.
    public byte CubeSetCode { get; set; }
    public int CubeSet => CubeSetCode >= 0x30 ? CubeSetCode - 0x30 : 0;
    public int AnimationCount { get; set; }
    public long IntervalCount { get; private set; }
    public int Host { get; set; }
    public int CameraLock { get; set; } = -1;
    public byte LastKey { get; set; }
    public byte[] CheatValues { get; } = new byte[3];
    public int MobileHit { get; set; } = -1;
    public int MapHit { get; set; } = -1;
    public int ButtonNumber { get; set; } = -1;
    public byte[] ButtonStates { get; } = new byte[20];
    public byte[] ButtonRegen { get; } = new byte[20];
    public int PendingInput { get; set; }
    // 40 rows of 40 words: low byte current state, high byte previous state (check_interactions).
    public byte[] InteractionData { get; } = new byte[3200];
    public byte[] Stung { get; }
    public int HighlightObject { get; set; } = -1;
    public byte[] HighlightBytes { get; } = new byte[4];
    public bool AerialView { get; set; }
    public byte SpeechCount { get; set; }
    public byte LastRandom { get; set; }
    public byte ErrorCount { get; set; }
    public int NextAlert { get; set; }
    public ushort[] MobileSignals { get; }
    ushort[] signalStack;
    // The original's 10 slot signal store (sig_store): a slot is free while its target word is 0xffff.
    readonly ushort[] signalDue = Enumerable.Repeat((ushort)0xffff, 10).ToArray();
    readonly ushort[] signalTarget = Enumerable.Repeat((ushort)0xffff, 10).ToArray();
    readonly ushort[] signalValue = Enumerable.Repeat((ushort)0xffff, 10).ToArray();
    ushort signalTime;
    public GameAudio Audio { get; } = new();
    public bool FullScreenImage { get; set; }
    public bool FirstTime { get; set; } = true;
    public bool Frozen { get; set; }
    public bool Ended { get; set; }
    public int LastMessage { get; set; } = -1;
    public int MessageObject { get; set; } = -1;
    public int MessageNumber { get; set; }
    public int Speaking { get; set; } = -1;
    public string Speech { get; set; } = "";
    public int IntroFrame { get; set; }
    public Dictionary<int, IntroAnimation> IntroAnimations { get; } = [];

    public GameWorld(GameAssets assets)
    {
        Assets = assets;
        Map = (byte[])assets.Map.Clone();
        MobileSignals = Enumerable.Repeat((ushort)0xffff, Mobiles.Length).ToArray();
        Stung = new byte[Mobiles.Length];
        signalStack =(ushort[])MobileSignals.Clone();
        var first = Mobiles.FirstOrDefault(mobile => mobile.X >= 0 && mobile.Y >= 0);
        LoadScene("SCREEN.GFX");
    }

    public void PanTo(int cubeX, int cubeY)
    {
        var x = Math.Clamp(cubeX, 0, 56);
        var y = Math.Clamp(cubeY, 0, 56);
        // pan_to scrolls one cube per step on both axes, redrawing (lay_floor) after every step.
        while (x != ViewX || y != ViewY)
        {
            ViewX += Math.Sign(x - ViewX);
            ViewY += Math.Sign(y - ViewY);
            LayFloor();
        }
        Audio.UpdateListener(ViewX, ViewY);
    }

    // a_sceneopens sets the view directly, without the clamping or stepping of pan_to.
    public void SetView(int cubeX, int cubeY)
    {
        ViewX = cubeX;
        ViewY = cubeY;
        LayFloor();
        Audio.UpdateListener(ViewX, ViewY);
    }

    // lay_floor starts with sub_5A17, which brings carried mobiles and the cell caches up to date.
    public void LayFloor()
    {
        UpdateCarriedMobiles();
        UpdateCells();
        UpdatePuffs();
    }

    public byte Cube(int cubeX, int cubeY, int level) => Map[(cubeY * 64 + cubeX) * 4 + level];

    public void AdvanceTime(double elapsed)
    {
        var fading = FadeBusy;
        if (fading) AdvanceFade(elapsed);
        // A fade run by the aerial-view exit happens inside the timer handler, which ignores nested ticks meanwhile.
        if (fading && interruptFade) { interruptFade = FadeBusy; return; }
        pendingTime += elapsed;
        while (pendingTime >= UpdateInterval)
        {
            pendingTime -= UpdateInterval;
            AnimationCount ^= 1;
            IntervalCount++;
            IntroFrame = (IntroFrame + 1) & 7;
            if (!Frozen && AerialView)
            {
                // Aerial update path (loc_2714): no drawing or button input, only the simulation and the exit check.
                if (CheckAerialExit()) { pendingTime = 0; return; }
                RunSequencers();
                DecrementTimers();
                CheckInteractions();
                UpdateStung();
                ReceiveSignals();
                UpdateCarriedMobiles();
                UpdateCells();
            }
            else if (!Frozen)
            {
                FollowCamera();
                LayFloor();
                if (SpeechCount != 0 && --SpeechCount == 0) { Speaking = -1; Speech = ""; ClearSpeech(); }
                RunSequencers();
                DecrementTimers();
                CheckInteractions();
                UpdateStung();
                ReceiveSignals();
                ProcessInput();
                if (AnimationCount == 1) RegenerateButtons();
                UpdateStatusIndicator();
            }
        }
    }

    // dec_timers: attribute 2 (timeout) and attribute 3 (panic) count down on alternate ticks, selected by animate_count;
    // 1 is the alert threshold and is cleared on the next tick.
    void DecrementTimers()
    {
        foreach (var mobile in Mobiles)
        {
            ref var timer = ref mobile.Attributes[2 + AnimationCount];
            if (timer == 0) continue;
            if (timer == 1) { timer = 0; continue; }
            if (--timer == 1) mobile.Alert = true;
        }
    }

    void RegenerateButtons()
    {
        for (var index = 0; index < Program.Buttons.Length; index++)
            if (ButtonRegen[index] != 0 && --ButtonRegen[index] == 0) ButtonStates[index] = 2;
    }

    // issue_signal: a free slot of the original 10-entry store, delivered when the signal clock reaches time + delay.
    public void IssueSignal(ushort target, ushort value, int delay)
    {
        var slot = Array.IndexOf(signalTarget, (ushort)0xffff);
        if (slot < 0) return;
        signalTarget[slot] = target;
        signalValue[slot] = value;
        signalDue[slot] = (ushort)(signalTime + delay);
    }

    void ReceiveSignals()
    {
        for (var index = 0; index < Mobiles.Length; index++)
        {
            if (signalStack[index] == 0xffff) continue;
            MobileSignals[index] = signalStack[index];
            signalStack[index] = 0xffff;
            Mobiles[index].Alert = true;
        }
        signalTime++;
        for (var index = 0; index < 10; index++)
        {
            var target = signalTarget[index];
            var value = signalValue[index];
            if (target == 0xffff || signalDue[index] != signalTime) continue;
            signalTarget[index] = 0xffff;
            switch (target >> 8)
            {
                case 0: SendSignal(target, value); break;
                case 3: for (var mobile = Mobiles.Length - 1; mobile >= 0; mobile--) SendSignal(mobile, value); break;
                default:
                    // Types 1 and 2 reach everyone whose interaction state with the sender is at least that type.
                    for (var mobile = 0; mobile < Mobiles.Length; mobile++)
                        if (InteractionData[mobile * 80 + (value & 255) * 2] >= target >> 8) SendSignal(mobile, value);
                    break;
            }
        }
    }

    void SendSignal(int mobile, ushort value)
    {
        if (MobileSignals[mobile] == 0xffff) { Mobiles[mobile].Alert = true; MobileSignals[mobile] = value; }
        else signalStack[mobile] = value;
    }

    // sub_5B8C: keep the camera-locked mobile inside the view, scrolling four cubes at a time.
    void FollowCamera()
    {
        if (CameraLock < 0 || CameraLock >= Mobiles.Length) return;
        var mobile = Mobiles[CameraLock];
        if (mobile.X < 0 || mobile.Y < 0) return;
        var relativeX = (mobile.X >> 3) - ViewX;
        var relativeY = (mobile.Y >> 3) - ViewY;
        var deltaX = relativeX < 1 ? -4 : relativeX > 6 ? 4 : 0;
        var deltaY = relativeY < 1 ? -4 : relativeY > 6 ? 4 : 0;
        if (deltaX != 0 || deltaY != 0) PanTo(ViewX + deltaX, ViewY + deltaY);
    }

    public void UpdateCarriedMobiles()
    {
        for (var index = Mobiles.Length - 1; index >= 0; index--)
        {
            var mobile = Mobiles[index];
            if (mobile.CarriedBy == 255) continue;
            if (mobile.CarriedBy >= Mobiles.Length) throw new InvalidDataException("Invalid carrier.");
            var carrier = Mobiles[mobile.CarriedBy];
            mobile.Z = unchecked((byte)(carrier.Z + mobile.Above));
            mobile.Direction = carrier.Direction;
            mobile.X = carrier.X;
            mobile.Y = carrier.Y;
            switch (carrier.Direction)
            {
                case 0: mobile.X -= mobile.Behind; break;
                case 1: mobile.Y += mobile.Behind; break;
                case 2: mobile.Y -= mobile.Behind; break;
                default: mobile.X += mobile.Behind; break;
            }
        }
    }
}

public sealed record IntroAnimation(int X, int Y, int[] Frames);

