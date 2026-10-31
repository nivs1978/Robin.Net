// Project: Robin.Net
// File: Input.cs
// License: GNU General Public License version 3 (see LICENSE)

using RobinHood.Assets;

namespace RobinHood.Engine;

// Keyboard buffer, mouse clicks, sidebar buttons and hit testing (ISOVGA 2542-25BD, 27E5-2A5C, 3F39-3FD0, 3FD7-4012).
public sealed partial class GameWorld
{
    const int KeyboardSlots = 8;
    readonly byte[] keyboard = new byte[KeyboardSlots];
    int keyboardHead, keyboardTail;
    byte lastScancode;
    bool swallowNextKey;
    byte click;
    bool releasePending;
    int clickX, clickY, lastMouseButtons;
    bool cheatAlt;
    int cheatPointer;

    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public int CursorMode { get; private set; }
    public bool CursorActive { get; private set; }
    public bool KeyAvailable => keyboardHead != keyboardTail;
    public bool ClickPending => click == 1;
    public int ClickX => clickX;
    public int ClickY => clickY;

    // kbd_handler: remember every new scancode (make and break) in an 8 slot ring that holds 7.
    public void StoreScancode(byte scancode)
    {
        if (swallowNextKey) { swallowNextKey = false; return; }
        if (scancode == lastScancode) return;
        lastScancode = scancode;
        var next = (keyboardHead + 1) % KeyboardSlots;
        if (next == keyboardTail) return;
        keyboard[keyboardHead] = scancode;
        keyboardHead = next;
    }

    // getkey as used by the wait actions: the original then blocks for the matching break code, so the next code is dropped.
    public byte TakeKey()
    {
        var key = keyboard[keyboardTail];
        keyboardTail = (keyboardTail + 1) % KeyboardSlots;
        if (KeyAvailable) keyboardTail = (keyboardTail + 1) % KeyboardSlots;
        else swallowNextKey = true;
        return key;
    }

    public void ClearKeys()
    {
        keyboardHead = keyboardTail = 0;
        swallowNextKey = false;
    }

    public void ConsumeClick() => click = 0;

    // update_cursor: clamp the pointer and turn a button change into a press (1) or release (2) event.
    public void Mouse(int x, int y, bool pressed)
    {
        x = Math.Clamp(x, 0, 0x130);
        y = Math.Clamp(y, 0, 0xb8);
        var buttons = pressed ? 1 : 0;
        if (buttons != lastMouseButtons)
        {
            lastMouseButtons = buttons;
            if (click == 1) releasePending = true;
            else
            {
                click = 2;
                if (buttons != 0)
                {
                    click = 1;
                    clickX = x + 5;
                    clickY = y + 1;
                }
            }
        }
        CursorX = x;
        CursorY = y;
    }

    // sub_28EE: cheat code keys, button keys, then mouse clicks.
    public void ProcessInput()
    {
        CheckCheat();
        if (KeyAvailable)
        {
            var key = keyboard[keyboardTail];
            keyboardTail = (keyboardTail + 1) % KeyboardSlots;
            var button = Array.FindIndex(Program.Buttons, candidate => candidate.Key == (key & 0x7f));
            if (button >= 0) { ButtonUpdate(button, (key & 0x80) == 0 ? 1 : 2); return; }
        }
        if (click == 0)
        {
            if (!releasePending) return;
            releasePending = false;
            click = 2;
        }
        var state = click;
        click = 0;
        if (state == 2)
        {
            if (ButtonNumber != -1) ButtonUpdate(ButtonNumber, 2);
            return;
        }
        for (var index = Program.Buttons.Length - 1; index >= 0; index--)
        {
            var button = Program.Buttons[index];
            if (clickX >= button.X && clickY >= button.Y && clickX <= button.X + 16 && clickY <= button.Y + 16)
            {
                ButtonUpdate(index, 1);
                return;
            }
        }
        var x = clickX - 64;
        var y = clickY - 16;
        if (x < 0 || x > 0xff || y < 0 || y > 0xb0) return;
        if (!HitMobile(x, y)) HitCube(x, y);
    }

    void CheckCheat()
    {
        if (!KeyAvailable) return;
        var key = keyboard[keyboardTail];
        if (key == 0x38) { cheatAlt = true; cheatPointer = 0; return; }
        if (!cheatAlt) return;
        if (key == 0xb8)
        {
            cheatAlt = false;
            if (cheatPointer == 3) PendingInput = 6;
        }
        else if (cheatPointer < 3 && key is >= 2 and <= 0xb) CheatValues[cheatPointer++] = key;
    }

    void ButtonUpdate(int button, int state)
    {
        if (ButtonStates[button] < 2) return;
        ButtonNumber = button;
        if (state == 2)
        {
            if (CursorActive) return;
            ButtonStates[button] = 2;
            PendingInput = 2;
            return;
        }
        if (CursorActive) { EndTargeting(); return; }
        DeselectAll();
        ButtonStates[button] = 3;
        if (Program.Buttons[button].Type == 1) { CursorActive = true; CursorMode = 1; }
        else PendingInput = 1;
    }

    void DeselectAll()
    {
        for (var index = 0; index < ButtonStates.Length; index++)
            if (ButtonStates[index] == 3) ButtonStates[index] = 2;
    }

    // sub_28DD: leave object targeting mode.
    public void EndTargeting()
    {
        CursorActive = false;
        CursorMode = 0;
        DeselectAll();
    }

    // check_objects: the 17x17 box of each mobile in the 256x176 view, excluding the host.
    bool HitMobile(int x, int y)
    {
        for (var index = 0; index < Mobiles.Length; index++)
        {
            if (index == Host || !ScreenPosition(Mobiles[index], out var left, out var top)) continue;
            if (x < left || x > left + 0x11 || y < top || y > top + 0x11) continue;
            MobileHit = index;
            PendingInput = CursorActive ? 3 : 4;
            return true;
        }
        return false;
    }

    public bool ScreenPosition(Mobile mobile, out int left, out int top)
    {
        left = mobile.ScreenX;
        top = mobile.ScreenY;
        return mobile.OnScreen;
    }

    // check_cubes: invert the isometric projection to find the clicked map cube.
    void HitCube(int x, int y)
    {
        x = (x - 8 >> 4) - 7;
        y = (y - 4 >> 3) - 4;
        var cubeY = y - x >> 1;
        var cubeX = y - cubeY;
        if (cubeX is < 0 or >= 8 || cubeY is < 0 or >= 8) return;
        MapHit = cubeX + ViewX << 8 | cubeY + ViewY;
        PendingInput = 5;
    }
}

