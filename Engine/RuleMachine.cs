// Project: Robin.Net
// File: RuleMachine.cs
// License: GNU General Public License version 3 (see LICENSE)

using System.Buffers.Binary;
using RobinHood.Assets;

namespace RobinHood.Engine;

public sealed class RuleMachine
{
    const int Then = 0xfff8;
    const int EndRule = 0xfff7;
    const int EndScript = 0xfff6;
    readonly GameWorld world;
    readonly GameRandom random;
    IEnumerator<ScriptWait>? current;
    ScriptWait? waiting;
    long waitStart;
    int owner;
    int it = -1;
    int operations;
    ScriptCursor? inputRules; // kept so in-code counters (EVERY) persist between runs
    bool StartupComplete { get; set; }
    public ushort OwnerSignal { get; private set; } = 0xffff;

    public RuleMachine(GameWorld world, int seed = 1) : this(world, world.Program.StartRules, seed) { }

    internal RuleMachine(GameWorld world, ushort[] startRules, int seed = 1)
    {
        this.world = world;
        random = new GameRandom(unchecked((ushort)seed));
        random.Next(0x32); // the manual-protection prompt (sub_43CB) draws one rnd(50) before the game starts
        current = Execute(new ScriptCursor(startRules, 0, "START"), true, 0).GetEnumerator();
    }

    // Startup advances one step per call. Afterwards this is the original main loop (sub_683A):
    // input rules first, then one alerted behaviour rulebase at a time, bounded per call.
    public void Update(double elapsed)
    {
        operations = 0;
        for (var budget = StartupComplete ? world.Mobiles.Length + 1 : 1; budget > 0; budget--)
        {
            if (world.FadeBusy && waiting?.Fade != true)
            {
                return;
            }
            if (waiting is { Fade: true })
            {
                if (world.FadeBusy) return;
                waiting = null;
            }
            else if (waiting != null)
            {
                if (waiting.Animate) world.AnimateIcons();
                var pressed = world.KeyAvailable;
                var clicked = world.ClickPending;
                if (!pressed && !clicked && (waiting.Intervals == 0 || world.IntervalCount - waitStart < waiting.Intervals)) return;
                if (pressed) world.LastKey = world.TakeKey();
                else if (clicked) world.ConsumeClick();
                waiting = null;
            }
            if (current == null && (current = StartupComplete ? Next() : null) == null) return;
            if (current.MoveNext()) { waiting = current.Current; waitStart = world.IntervalCount; return; }
            current.Dispose();
            current = null;
            if (!StartupComplete) { StartupComplete = true; return; }
        }
    }

    IEnumerator<ScriptWait>? Next()
    {
        if (world.PendingInput != 0 && (!world.CursorActive || world.PendingInput == 3)) return RunInput().GetEnumerator();
        return FindBehaviour(world.NextAlert, world.Mobiles.Length);
    }

    // run_behaviour_rules: the next alerted mobile at or after `start` (scanning `length` entries, wrapping) runs its rulebase.
    IEnumerator<ScriptWait>? FindBehaviour(int start, int length)
    {
        var count = world.Mobiles.Length;
        for (var step = 0; step < length; step++)
        {
            var index = (start + step) % count;
            var mobile = world.Mobiles[index];
            if (!mobile.Alert) continue;
            mobile.Alert = false;
            world.NextAlert = (index + 1) % count;
            proposals.Clear();
            OwnerSignal = world.MobileSignals[index];
            world.MobileSignals[index] = 0xffff;
            return mobile.Behavior == 0xff ? Enumerable.Empty<ScriptWait>().GetEnumerator() : Execute(Rule(mobile.Behavior), true, index).GetEnumerator();
        }
        return null;
    }
    IEnumerable<ScriptWait> RunInput()
    {
        inputRules ??= new ScriptCursor(world.Program.InputRules, 0, "INPUT");
        inputRules.Position = 0;
        foreach (var pause in Execute(inputRules, false, world.Host)) yield return pause;
        world.MobileHit = -1;
        world.MapHit = -1;
        if (world.PendingInput == 3) world.EndTargeting();
        world.PendingInput = 0;
    }

    IEnumerable<ScriptWait> Execute(ScriptCursor cursor, bool all, int initialOwner)
    {
        var previousOwner = owner;
        owner = initialOwner;
        try
        {
            while (cursor.Peek() != EndScript && !world.Ended)
            {
                var conditionPassed = true;
                while (cursor.Peek() != Then)
                {
                    Guard(cursor);
                    var condition = cursor.Read();
                    var inverted = condition >= 1000;
                    if (inverted) condition -= 1000;
                    var conditionResult = Condition(condition, cursor);
                    if (conditionResult != inverted) continue;
                    conditionPassed = false;
                    while (cursor.Read() != EndRule) Guard(cursor);
                    break;
                }
                if (!conditionPassed) continue;
                cursor.Read();
                var continueRules = false;
                while (cursor.Peek() != EndRule)
                {
                    Guard(cursor);
                    var opcode = cursor.Read();
                    if (opcode == 15) { continueRules = true; continue; }
                    if (opcode is 20 or 21)
                    {
                        var rule = cursor.Read();
                        var nextOwner = ObjectNumber(cursor);
                        foreach (var pause in Execute(Rule(rule), all, nextOwner)) yield return pause;
                        if (opcode == 21) { Skip(cursor, 0); break; }
                        continue;
                    }
                    if (opcode == 77)
                    {
                        world.Frozen = true;
                        world.ClearKeys();
                        yield return new ScriptWait(0, true);
                        world.ConsumeClick();
                        world.Frozen = false;
                        continue;
                    }
                    if (opcode == 85)
                    {
                        world.ClearKeys();
                        world.ConsumeClick();
                        world.LastKey = 0;
                        yield return new ScriptWait((byte)cursor.Read(), false, false, true);
                        continue;
                    }
                    Action(opcode, cursor);
                    while (world.FadeBusy) yield return new ScriptWait(0, false, true);
                }
                cursor.Read();
                if (!all && !continueRules) yield break;
            }
        }
        finally { owner = previousOwner; }
    }

    bool Condition(int opcode, ScriptCursor cursor)
    {
        switch (opcode)
        {
            case 0: return MobileLocation(owner) == Location(cursor);
            case 1: return MobileLocation(ObjectNumber(cursor)) == Location(cursor);
            case 3:
            {
                var attribute = Attribute(cursor);
                return Compare(attribute.Mobile.Attributes[attribute.Index], cursor.Read(), cursor.Read());
            }
            case 7: return world.LastMessage == cursor.Read();
            case 8: return world.FirstTime;
            case 11:
            {
                var first = Attribute(cursor);
                var comparison = cursor.Read();
                var second = Attribute(cursor);
                return Compare(first.Mobile.Attributes[first.Index], comparison, second.Mobile.Attributes[second.Index]);
            }
            case 16: return owner == ObjectNumber(cursor);
            case 2:
            {
                var location = MobileLocation(owner);
                if (location == 0xffff) { cursor.Read(); return false; }
                return (8 >> cursor.Read() & world.Cube(location >> 8, location & 255, 3)) != 0;
            }
            case 4: return ByteCompare(world.LastRandom, cursor.Read(), cursor.Read());
            case 5:
                world.LastRandom = (byte)random.Next(cursor.Read());
                return world.LastRandom == 0;
            case 6:
            {
                var period = (short)cursor.Read();
                var counter = (short)(cursor.Peek() + 1);
                var elapsed = counter >= period;
                cursor.Poke(elapsed ? 0 : counter);
                cursor.Read();
                return elapsed;
            }
            case 9: return ByteCompare(world.ErrorCount, cursor.Read(), cursor.Read());
            case 10:
            {
                var carrier = (byte)ObjectNumber(cursor);
                if (cursor.Peek() != 3000)
                {
                    var carried = ObjectNumber(cursor);
                    if (Mobile(carried).CarriedBy != carrier) return false;
                    it = carried;
                    return true;
                }
                cursor.Read();
                var found = Array.FindIndex(world.Mobiles, mobile => mobile.CarriedBy == carrier);
                if (found < 0) return false;
                it = found;
                return true;
            }
            case 12: return InLocation(world.Program.Locations[cursor.Read()], MobileLocation(owner));
            case 13:
            {
                var location = MobileLocation(ObjectNumber(cursor));
                return InLocation(world.Program.Locations[cursor.Read()], location);
            }
            case 14:
            {
                var target = Location(cursor);
                var current = MobileLocation(owner);
                return ByteCompare(AbsDifference(target >> 8, current >> 8) + AbsDifference(target & 255, current & 255), cursor.Read(), cursor.Read());
            }
            case 15:
            {
                var attribute = Attribute(cursor);
                world.LastRandom = (byte)random.Next(attribute.Mobile.Attributes[attribute.Index] + 1);
                return ByteCompare(world.LastRandom, cursor.Read(), cursor.Read());
            }
            case 17: return Visibility(cursor, (current, _, level) => (sbyte)current >= (sbyte)level);
            case 18: return Visibility(cursor, (current, previous, level) => current >= level && previous < level);
            case 19: return Visibility(cursor, (current, previous, level) => current < level && previous >= level);
            case 20: return ObjectNumber(cursor) == world.Host;
            case 40: return MobileLocation(owner) != 0xffff && world.Stung[owner] != 0;
            case 21: { var sequence = (byte)cursor.Read(); var mobile = Mobile(owner); return mobile.SeqName == sequence && mobile.SeqCount != 0x10; }
            case 22: { var sequence = (byte)cursor.Read(); var mobile = Mobile(owner); return mobile.SeqName == sequence && mobile.SeqCount == 0x10; }
            case 23:
            {
                var location = Location(cursor);
                if (location == 0xffff) { cursor.Read(); cursor.Read(); cursor.Read(); return false; }
                var level = cursor.Read();
                return ByteCompare(world.Cube(location >> 8, location & 255, level), cursor.Read(), cursor.Read());
            }
            case 24: return MobileLocation(ObjectNumber(cursor)) != 0xffff;
            case 25:
            {
                var type = cursor.Read();
                if (OwnerSignal >> 8 != (byte)type) return false;
                it = OwnerSignal & 255;
                return true;
            }
            case 26: return Mobile(owner).Attributes[0] == (byte)cursor.Read() & Mobile(owner).Attributes[1] == (byte)cursor.Read();
            case 27: return Mobile(owner).Attributes[0] == (byte)cursor.Read();
            case 30:
            {
                var mode = (byte)cursor.Read();
                return ByteCompare(world.Mobiles.Count(mobile => mobile.Attributes[0] == mode), cursor.Read(), cursor.Read());
            }
            case 31:
            {
                var location = Location(cursor);
                return (byte)((location >> 8) - world.ViewX) <= 7 && (byte)((location & 255) - world.ViewY) <= 7;
            }
            case 33: cursor.Read(); return true;
            case 34: return Mobile(owner).Attributes[2] == 1;
            case 35: return Mobile(ObjectNumber(cursor)).Behavior == (byte)cursor.Read();
            case 36:
            {
                var attribute = Attribute(cursor);
                return (attribute.Mobile.Attributes[attribute.Index] & (byte)cursor.Read()) != 0;
            }
            case 37: return (byte)cursor.Read() <= Mobile(owner).Attributes[0];
            case 38:
            {
                var carrier = Mobile(ObjectNumber(cursor)).CarriedBy;
                if (carrier == 0xff) return false;
                it = carrier;
                return true;
            }
            case 39: return Mobile(owner).Attributes[1] == (byte)cursor.Read();
            case 41: return Mobile(owner).Attributes[3] == 1;
            case 42: return Mobile(ObjectNumber(cursor)).Direction == (byte)cursor.Read();
            case 43: return world.ButtonStates[cursor.Read()] == (byte)cursor.Read();
            case 45: return !world.CursorActive && world.MobileHit != -1;
            case 46:
            {
                var target = Location(cursor);
                return (Mobile(owner).TargetX << 8 | Mobile(owner).TargetY) == target;
            }
            case 49: return ObjectNumber(cursor) == world.CameraLock;
            case 28:
            case 29:
            {
                var button = cursor.Read();
                return (opcode == 28 ? world.PendingInput is 1 or 3 : world.PendingInput == 2) && world.ButtonNumber == button;
            }
            case 32: return ObjectNumber(cursor) == ObjectNumber(cursor);
            case 44: return world.MobileHit == -1 && world.MapHit != -1;
            case 47: return world.LastKey == FunctionKeys[(byte)(cursor.Read() - 0x30) % FunctionKeys.Length];
            case 48:
            {
                var words = new[] { cursor.Read(), cursor.Read(), cursor.Read() };
                return world.PendingInput == 6 && words.Select((word, index) => DigitKeys[(byte)(word - 0x30) % DigitKeys.Length] == world.CheatValues[index]).All(match => match);
            }
            default: throw Unsupported("condition", opcode, cursor);
        }
    }

    void Action(int opcode, ScriptCursor cursor)
    {
        switch (opcode)
        {
            case 0: world.MessageObject = ObjectNumber(cursor); return;
            case 2: { var message = cursor.Read(); if (OkToSpeak()) ShowMessage(message); return; }
            case 8:
            {
                var attribute = Attribute(cursor);
                var operation = cursor.Read();
                attribute.Mobile.Attributes[attribute.Index] = Assign(attribute.Mobile.Attributes[attribute.Index], operation, cursor.Read());
                return;
            }
            case 9:
            {
                var attribute = Attribute(cursor);
                var maximum = cursor.Read();
                attribute.Mobile.Attributes[attribute.Index] = maximum == 0 ? (byte)0 : (byte)random.Next(maximum);
                return;
            }
            case 10:
            {
                var mobile = Mobile(ObjectNumber(cursor));
                var location = Location(cursor);
                mobile.X = (location >> 8) * 8 + 4;
                mobile.Y = (location & 255) * 8 + 4;
                return;
            }
            case 11:
            {
                var number = ObjectNumber(cursor);
                if (number == world.Host) world.CameraLock = -1;
                var mobile = Mobile(number);
                mobile.X = -1; mobile.Y = -1;
                return;
            }
            case 12: world.SaveStatus?.Invoke(world); world.Ended = true; Skip(cursor, 0); return;
            case 13: Skip(cursor, cursor.Read()); return;
            case 18:
            {
                var destination = Attribute(cursor);
                var operation = cursor.Read();
                var source = Attribute(cursor);
                destination.Mobile.Attributes[destination.Index] = Assign(destination.Mobile.Attributes[destination.Index], operation, source.Mobile.Attributes[source.Index]);
                return;
            }
            case 19:
            {
                var attribute = Attribute(cursor);
                world.MessageNumber = attribute.Mobile.Attributes[attribute.Index];
                return;
            }
            case 1:
            {
                var location = Location(cursor);
                var bit = cursor.Read();
                var set = cursor.Read() != 0;
                var offset = ((location & 255) * 64 + (location >> 8)) * 4 + 3;
                var mask = (byte)(8 >> bit);
                world.Map[offset] = set ? (byte)(world.Map[offset] | mask) : (byte)(world.Map[offset] & ~mask);
                return;
            }
            case 3:
            {
                var attribute = Attribute(cursor);
                var divisor = (byte)cursor.Read();
                var submessage = divisor == 0 ? 0 : attribute.Mobile.Attributes[attribute.Index] / divisor;
                var message = cursor.Read();
                if (OkToSpeak()) ShowMessage(message, submessage);
                return;
            }
            case 4:
            {
                var message = cursor.Read();
                var submessage = cursor.Peek();
                var next = submessage + 1;
                cursor.Poke(next >= SubmessageCount(message) ? 0 : next);
                cursor.Read();
                if (OkToSpeak()) ShowMessage(message, submessage);
                return;
            }
            case 5: if (world.Speaking == -1) Action(2, cursor); else cursor.Read(); return;
            case 6: if (world.Speaking == -1) Action(3, cursor); else for (var skip = 0; skip < 4; skip++) cursor.Read(); return;
            case 7: if (world.Speaking == -1) Action(4, cursor); else { cursor.Read(); cursor.Read(); } return;
            case 14: if (OkToSpeak()) ShowMessage(5); return;
            case 15: return;
            case 16: world.DeleteStatus?.Invoke(); world.Ended = true; Skip(cursor, 0); return;
            case 17: world.ErrorCount++; return;
            case 22:
            {
                var location = Location(cursor);
                var mobile = Mobile(owner);
                mobile.TargetX = (byte)(location >> 8);
                mobile.TargetY = (byte)location;
                mobile.SubTargetX = 0xff;
                return;
            }
            case 23: world.ErrorCount = 0; return;
            case 24: world.SetSequence(owner, cursor.Read()); return;
            case 25: world.SetSequence(owner + 1, cursor.Read()); return;
            case 26: world.Host = ObjectNumber(cursor); return;
            case 27:
            {
                var attributes = Mobile(owner).Attributes;
                var offset = (attributes[5] * 64 + attributes[4]) * 4;
                world.Map[offset + attributes[6]] = attributes[7];
                world.Map[offset + 3] = attributes[8];
                return;
            }
            case 28:
            {
                var carrier = ObjectNumber(cursor);
                var carried = Mobile(ObjectNumber(cursor));
                carried.CarriedBy = (byte)carrier;
                carried.Behind = (sbyte)cursor.Read();
                carried.Above = (byte)cursor.Read();
                return;
            }
            case 29:
            {
                var mobile = Mobile(ObjectNumber(cursor));
                mobile.CarriedBy = 0xff;
                mobile.Z = 0;
                mobile.Alert = true;
                return;
            }
            case 30: owner = ObjectNumber(cursor); return;
            case 31: { var delay = cursor.Read(); world.IssueSignal(0x021f, (ushort)((byte)cursor.Read() << 8 | owner), delay); return; }
            case 32: { var delay = cursor.Read(); world.IssueSignal(0x0120, (ushort)((byte)cursor.Read() << 8 | owner), delay); return; }
            case 33:
            {
                var delay = cursor.Read();
                var target = ObjectNumber(cursor);
                world.IssueSignal((ushort)target, (ushort)((byte)cursor.Read() << 8 | owner), delay);
                return;
            }
            case 34: { var delay = cursor.Read(); world.IssueSignal(0x0322, (ushort)((byte)cursor.Read() << 8 | owner), delay); return; }
            case 35: OwnerSignal = 0xffff; return;
            case 36: Mobile(owner).SetMode(cursor.Read()); Skip(cursor, 0); return;
            case 37: Mobile(owner).Attributes[1]++; return;
            case 38:
            {
                var operation = cursor.Read();
                var location = Location(cursor);
                var attributes = Mobile(owner).Attributes;
                attributes[4] = Assign(attributes[4], operation, location >> 8);
                attributes[5] = Assign(attributes[5], operation, location & 255);
                return;
            }
            case 39: Mobile(owner).Behavior = unchecked((byte)(cursor.Read() - 2000)); return;
            case 40:
                Mobile(owner).BaseSprite = (ushort)cursor.Read();
                Mobile(owner).AboutFace = (byte)cursor.Read();
                return;
            case 41:
            {
                var attribute = cursor.Read();
                var operation = cursor.Read();
                var list = cursor.Read();
                var indexAttribute = cursor.Read();
                var attributes = Mobile(owner).Attributes;
                var index = attributes[indexAttribute];
                attributes[attribute] = Assign(attributes[attribute], operation, world.Program.Lists[list][index]);
                return;
            }
            case 42:
            {
                var list = world.Program.Lists[cursor.Read()];
                var attributes = Mobile(owner).Attributes;
                var index = attributes[cursor.Read()];
                var operation = cursor.Read();
                list[index] = Assign(list[index], operation, attributes[cursor.Read()]);
                return;
            }
            case 43:
            {
                var target = Location(cursor);
                var from = MobileLocation(owner);
                Mobile(owner).Direction = (byte)GameWorld.WhichWay((byte)(target >> 8) - (byte)(from >> 8), (byte)target - (byte)from);
                return;
            }
            case 44:
            {
                var target = Mobile(ObjectNumber(cursor));
                var source = Mobile(owner);
                Mobile(owner).Direction = (byte)GameWorld.WhichWay((ushort)target.X - (ushort)source.X, (ushort)target.Y - (ushort)source.Y, true);
                return;
            }
            case 45:
                Mobile(owner).Follow = (byte)ObjectNumber(cursor);
                Mobile(owner).SubTargetX = 0xff;
                return;
            case 46:
            {
                if (world.CameraLock != owner) return;
                var camera = Mobile(owner);
                var direction = camera.Direction & 3;
                world.Frozen = true;
                world.PanTo(Math.Clamp(((ushort)camera.X >> 3) + SeeViewX[direction], 0, 0x38), Math.Clamp(((ushort)camera.Y >> 3) + SeeViewY[direction], 0, 0x38));
                world.Frozen = false;
                return;
            }
            case 47: cursor.Read(); return;
            case 48: Mobile(owner).Attributes[6] = (byte)ObjectNumber(cursor); return;
            case 49:
            {
                var alias = cursor.Read();
                world.SetPose(owner, alias);
                Mobile(owner).SeqCount = 0x10;
                return;
            }
            case 53:
            {
                var mobile = Mobile(ObjectNumber(cursor));
                mobile.X = ((ushort)mobile.X & 0xfff8) + cursor.Read();
                mobile.Y = ((ushort)mobile.Y & 0xfff8) + cursor.Read();
                mobile.Z = (byte)cursor.Read();
                mobile.Direction = (byte)cursor.Read();
                return;
            }
            case 54:
                world.HighlightObject = ObjectNumber(cursor);
                for (var index = 0; index < 4; index++) world.HighlightBytes[index] = (byte)cursor.Read();
                return;
            case 58:
            {
                var direction = cursor.Read();
                world.CameraLock = -1;
                world.PanTo(world.ViewX + PanX[direction], world.ViewY + PanY[direction]);
                return;
            }
            case 61: Propose(cursor.Read(), cursor.Read()); return;
            case 62:
            {
                var mode = (byte)cursor.Read();
                var operation = cursor.Read();
                var weight = Mobile(owner).Attributes[cursor.Read()];
                var scaled = operation switch
                {
                    '-' => 255 - weight,
                    '>' => (byte)(Math.Max(0, weight - 0x80) << 1),
                    '<' => (byte)(Math.Max(0, 255 - weight - 0x80) << 1),
                    _ => (int)weight
                };
                var factor = (byte)cursor.Read();
                Propose(mode, (factor * scaled + scaled & 0xffff) >> 8);
                return;
            }
            case 63:
            {
                var fallback = (byte)cursor.Read();
                var best = 0;
                var chosen = fallback;
                foreach (var proposal in proposals)
                    if ((proposal & 255) > best) { best = proposal & 255; chosen = (byte)(proposal >> 8); }
                Mobile(owner).SetMode(chosen);
                return;
            }
            case 64: Mobile(ObjectNumber(cursor)).Puff = 4; return;
            case 65:
            {
                var mobile = Mobile(ObjectNumber(cursor));
                var screen = Location(cursor);
                PlaceInView(mobile, screen >> 8, screen & 255);
                return;
            }
            case 66:
            {
                var attribute = Attribute(cursor);
                var mask = (byte)cursor.Read();
                if (cursor.Read() != 0) attribute.Mobile.Attributes[attribute.Index] |= mask;
                else attribute.Mobile.Attributes[attribute.Index] &= (byte)~mask;
                return;
            }
            case 67: world.Frozen = true; world.FadeOut(); world.AfterFade(() => world.Frozen = false); return;
            case 68: world.Frozen = true; world.FadeIn(); world.AfterFade(() => world.Frozen = false); return;
            case 79: world.ShowAerialView(); return;
            case 87:
            {
                var attribute = Attribute(cursor);
                var colour = cursor.Read();
                var divisor = (byte)cursor.Read();
                var length = divisor == 0 ? 0 : attribute.Mobile.Attributes[attribute.Index] * 0x46 / divisor & 255;
                var x = cursor.Read();
                world.PlotBar(x, cursor.Read(), length, colour & 255);
                return;
            }
            case 92:
            {
                var shown = Mobile(ObjectNumber(cursor));
                var x = cursor.Read();
                world.ShowObjectIcon(shown, x, cursor.Read());
                return;
            }
            case 97: return;
            case 71: Mobile(owner).SavedMode = (byte)cursor.Read(); return;
            case 72:
            {
                var mobile = Mobile(owner);
                mobile.Alert = true;
                mobile.SetMode(mobile.SavedMode);
                return;
            }
            case 50: Mobile(ObjectNumber(cursor)).Alert = true; return;
            case 51: Mobile(owner).Attributes[2] = (byte)cursor.Read(); return;
            case 52: Mobile(owner).Attributes[2] = 0; return;
            case 55: Mobile(owner).PoseAliases[cursor.Read()] = (byte)cursor.Read(); return;
            case 56: Mobile(owner).Direction = (byte)cursor.Read(); return;
            case 57:
            {
                var button = cursor.Read();
                var word = cursor.Read();
                world.ButtonStates[button] = (byte)word;
                world.ButtonRegen[button] = (byte)(word >> 8);
                return;
            }
            case 59:
            {
                var location = Location(cursor);
                world.CameraLock = -1;
                world.SetView(location >> 8, location & 255);
                return;
            }
            case 60: Mobile(owner).Z = (byte)cursor.Read(); return;
            case 69: world.CubeSetCode = (byte)(0x30 + cursor.Read()); world.LayFloor(); return;
            case 70: Mobile(owner).Attributes[3] = (byte)cursor.Read(); return;
            case 73: Mobile(ObjectNumber(cursor)).SetMode(cursor.Read()); return;
            case 74: Mobile(ObjectNumber(cursor)).Mobility = (byte)cursor.Read(); return;
            case 75:
            case 76: return; // set flags consumed only by sub_4036, which is a bare ret in this build
            case 78:
            {
                var button = cursor.Read();
                world.ButtonRegen[button] = (byte)cursor.Read();
                world.ButtonStates[button] = 1;
                return;
            }
            case 80: { var message = cursor.Read(); if (OkToSpeak()) ShowMessage(message); return; }
            case 81:
            {
                var number = ObjectNumber(cursor);
                Mobile(number).Type = (byte)cursor.Read();
                world.ClearInteractions(number);
                return;
            }
            case 82:
            {
                var mobile = ObjectNumber(cursor);
                var attribute = cursor.Read();
                var divisor = (byte)cursor.Read();
                var x = cursor.Read();
                var y = cursor.Read();
                if (!world.AerialView)
                    world.PrintText((divisor == 0 ? 0 : Mobile(mobile).Attributes[attribute] / divisor).ToString().PadRight(3), x, y);
                return;
            }
            case 83:
            {
                var image = world.Program.Messages[cursor.Read()];
                world.Frozen = true;
                world.Speaking = -1;
                world.FadeOut();
                world.AfterFade(() =>
                {
                    world.LoadScene(image);
                    world.FullScreenImage = true;
                    world.Speech = "";
                    world.SpeechCount = 0;
                });
                world.FadeIn();
                return;
            }
            case 84: ShowMessage(cursor.Read(), null, false); return;
            case 86:
            case 101: world.ReturnToGameScreen(true); return;
            case 88:
            {
                var slot = cursor.Read();
                var left = cursor.Read();
                var top = cursor.Read();
                world.IntroAnimations[slot] = new(left, top, Enumerable.Range(0, 8).Select(_ => cursor.Read()).ToArray());
                return;
            }
            case 89:
            {
                var mobile = ObjectNumber(cursor);
                var attribute = cursor.Read();
                var x = cursor.Read();
                world.SetStatusIndicator(Mobile(mobile), attribute, x, cursor.Read());
                return;
            }
            case 90:
            {
                var mobile = Mobile(ObjectNumber(cursor));
                var location = Location(cursor);
                mobile.HomeX = (byte)(location >> 8); mobile.HomeY = (byte)location;
                return;
            }
            case 91: world.CameraLock = ObjectNumber(cursor); return;
            case 93:
            {
                var mobile = ObjectNumber(cursor);
                var sound = cursor.Read();
                var location = MobileLocation(mobile);
                world.Audio.StartSound(sound, 0xff00 | mobile, location >> 8, location & 255);
                return;
            }
            case 94:
            {
                var location = Location(cursor);
                world.Audio.StartSound(cursor.Read(), location, location >> 8, location & 255);
                return;
            }
            case 95: world.Audio.StopSound(0xff00 | ObjectNumber(cursor)); return;
            case 96: world.Audio.StopSound(Location(cursor)); return;
            case 98: world.Audio.PlayMusic(cursor.Read()); return;
            case 99: world.Audio.StopMusic(); return;
            case 100: Mobile(ObjectNumber(cursor)).Speed = (byte)cursor.Read(); return;
            default: throw Unsupported("action", opcode, cursor);
        }
    }

    int ObjectNumber(ScriptCursor cursor) => cursor.Read() switch
    {
        1000 => world.MobileHit, 1001 => owner, 1002 => it, 1003 => Mobile(owner).Attributes[6], 1004 => world.Host,
        var number => number
    };

    int Location(ScriptCursor cursor)
    {
        var value = cursor.Read();
        return (value >> 8) switch
        {
            255 => Mobile(owner).HomeX << 8 | Mobile(owner).HomeY,
            254 => Mobile(value & 255).HomeX << 8 | Mobile(value & 255).HomeY,
            253 => MobileLocation(owner), 252 => MobileLocation(value & 255), 251 => MobileLocation(it),
            250 => Mobile(owner).TargetX << 8 | Mobile(owner).TargetY,
            249 => Mobile(owner).Attributes[4] << 8 | Mobile(owner).Attributes[5],
            248 => world.Program.Locations[value & 255].Key,
            247 => MobileLocation(Mobile(owner).Attributes[6]), 246 => world.MapHit, _ => value
        };
    }

    int MobileLocation(int number)
    {
        var mobile = Mobile(number);
        return mobile.X < 0 || mobile.Y < 0 ? 0xffff : (mobile.X >> 3) << 8 | mobile.Y >> 3;
    }

    // MOB_HIT is 0xff when nothing was clicked; the original then reads past its tables. Treat it as an off-map object whose attributes are zero.
    readonly Mobile noObject = new() { X = -1, Y = -1, Attributes = new byte[32], PoseAliases = new byte[32] };

    Mobile Mobile(int number) => number == -1 ? noObject : number >= 0 && number < world.Mobiles.Length ? world.Mobiles[number] : throw new InvalidDataException($"Invalid object reference {number}.");
    (Mobile Mobile, int Index) Attribute(ScriptCursor cursor)
    {
        var mobile = Mobile(ObjectNumber(cursor));
        var attribute = cursor.Read();
        if (attribute >= 32) throw new InvalidDataException("Attribute index outside original 32-byte attribute block.");
        return (mobile, attribute);
    }

    static bool Compare(int left, int operation, int right) => operation switch
    {
        '=' => left == right, '>' => left > right, '<' => left < right, _ => throw new NotSupportedException($"Comparison {operation} is not translated.")
    };

    // Visibility predicates (sub_6DC8/6E40/6EC4): level word, then one object, any object (3000), or any object of type word-2000.
    bool Visibility(ScriptCursor cursor, Func<byte, byte, byte, bool> test)
    {
        var level = (byte)cursor.Read();
        if (cursor.Peek() < 2000)
        {
            var single = ObjectNumber(cursor);
            var state = world.Interaction(owner, single);
            if (!test((byte)state, (byte)(state >> 8), level)) return false;
            it = single;
            return true;
        }
        var selector = cursor.Read();
        for (var index = 0; index < world.Mobiles.Length; index++)
        {
            var state = world.Interaction(owner, index);
            if (!test((byte)state, (byte)(state >> 8), level)) continue;
            if (selector != 3000 && world.Mobiles[index].Behavior != (byte)(selector - 2000)) continue;
            it = index;
            return true;
        }
        return false;
    }

    // Original byte comparison (loc_6C46): '<' and '>' are tested, anything else means equality.
    static bool ByteCompare(int left, int operation, int right) => ((byte)operation) switch
    {
        0x3c => (byte)left < (byte)right, 0x3e => (byte)left > (byte)right, _ => (byte)left == (byte)right
    };

    static bool InLocation(Location area, int cell) =>
        (byte)(cell >> 8) >= area.LeftRight >> 8 && (byte)(cell >> 8) <= (area.LeftRight & 255) &&
        (byte)cell >= area.TopBottom >> 8 && (byte)cell <= (area.TopBottom & 255);

    static int AbsDifference(int left, int right)
    {
        var difference = (byte)(left - right);
        return (sbyte)difference < 0 ? (byte)-difference : difference;
    }

    static byte Assign(int previous, int operation, int value)
    {
        var operand = unchecked((byte)value);
        return operation switch
        {
            '=' => operand, '+' => (byte)Math.Min(255, previous + operand), '-' => (byte)Math.Max(0, previous - operand),
            '*' => unchecked((byte)(previous * operand)), '/' => operand == 0 ? (byte)previous : (byte)(previous / operand),
            '%' => operand == 0 ? (byte)previous : (byte)(previous % operand), _ => throw new NotSupportedException($"Assignment {operation} is not translated.")
        };
    }

    static readonly byte[] FunctionKeys = [0x44, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x41, 0x42, 0x43];
    static readonly byte[] DigitKeys = [0x0b, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a];
    static readonly int[] SeeViewX = [-1, -3, -3, -6];
    static readonly int[] SeeViewY = [-3, -6, -1, -3];
    static readonly int[] PanX = [6, 0, 0, -6];
    static readonly int[] PanY = [0, -6, 6, 0];
    readonly List<ushort> proposals = [];

    // a_propose: accumulate a mode's score (saturating at 255); a mode is listed once.
    void Propose(int mode, int score)
    {
        var index = proposals.FindIndex(entry => entry >> 8 == (byte)mode);
        if (index < 0) { proposals.Add((ushort)((byte)mode << 8 | (byte)score)); return; }
        proposals[index] = (ushort)((byte)mode << 8 | Math.Min(255, (proposals[index] & 255) + (byte)score));
    }

    // sub_7A60: put the object at a screen-relative cube, or at the nearest free cube of the 8x8 view.
    void PlaceInView(Mobile mobile, int screenX, int screenY)
    {
        bool Free(int x, int y) => world.Map[((y + world.ViewY) * 64 + x + world.ViewX) * 4 + 1] == 0xff;
        int x = screenX, y = screenY;
        if (!Free(screenX, screenY))
        {
            var best = 0xff;
            x = 0; y = 0;
            for (var column = 7; column >= 0; column--)
                for (var row = 7; row >= 0; row--)
                {
                    if (!Free(column, row)) continue;
                    var distance = (byte)(Math.Abs(column - screenX) + Math.Abs(row - screenY));
                    if (distance > best) continue;
                    best = distance; x = column; y = row;
                }
        }
        mobile.X = (x + world.ViewX) << 3;
        mobile.Y = (y + world.ViewY) << 3;
    }

    // is_ok_to_speak: nothing is said while the aerial view is up or when the speaker is outside the 8x8 view.
    bool OkToSpeak()
    {
        var speaker = Mobile(owner);
        return !world.AerialView && (uint)(speaker.CellX - world.ViewX) <= 7 && (uint)(speaker.CellY - world.ViewY) <= 7;
    }

    int SubmessageCount(int number)
    {
        var text = world.Program.Messages[number];
        return text.TakeWhile(character => character == '[').Count() + 1;
    }

    // print_message / sub_837C: a message may hold alternatives ("[[a]b]c"); pick one at random or by index.
    void ShowMessage(int number, int? submessage = null, bool speaker = true)
    {
        world.LastMessage = number;
        if (speaker) world.Speaking = owner;
        var text = world.Program.Messages[number];
        var position = 0;
        while (position < text.Length && text[position] == '[') position++;
        var skip = submessage ?? (position == 0 ? 0 : random.Next(position + 1));
        for (var index = 0; index < skip; index++)
            while (position < text.Length && text[position++] != ']') { }
        var end = text.IndexOf(']', position);
        world.Speech = MessageText.Prepare(end < 0 ? text[position..] : text[position..end], world.MessageNumber);
        world.SpeechCount = (byte)(world.Speech.Length / 3 + 4);
        world.ShowSpeech(world.Speech);
    }

    ScriptCursor Rule(int number)
    {
        if (number >= world.Program.RuleOffsets.Length) throw new InvalidDataException("Rule number outside original table.");
        return new(world.Program.RuleCode, world.Program.RuleOffsets[number], $"RULE {number}");
    }

    void Skip(ScriptCursor cursor, int count)
    {
        if (count == 0)
        {
            while (cursor.Peek() != EndScript) { cursor.Read(); Guard(cursor); }
            cursor.Position -= 2;
            return;
        }
        for (var index = 0; index <= count; index++) while (cursor.Read() != EndRule) Guard(cursor);
        cursor.Position -= 2;
    }

    void Guard(ScriptCursor cursor) { if (++operations > 100000) throw new InvalidDataException($"Script operation budget exceeded in {cursor.Name}."); }
    static Exception Unsupported(string kind, int opcode, ScriptCursor cursor) => new NotSupportedException($"Untranslated {kind} {opcode} in {cursor.Name} at byte 0x{cursor.Position - 2:X4}. Script execution stopped; it was not skipped.");
}

// Intervals counts timer intervals (insert_timer ticks); zero waits for a key or click.
public sealed record ScriptWait(int Intervals, bool UntilKey = false, bool Fade = false, bool Animate = false);

sealed class ScriptCursor
{
    readonly byte[] bytes;
    public string Name { get; }
    public int Position { get; set; }
    public ScriptCursor(byte[] bytes, int position, string name) { this.bytes = bytes; Position = position; Name = name; }
    public ScriptCursor(ushort[] words, int position, string name) : this(new byte[words.Length * 2], position, name)
    {
        for (var index = 0; index < words.Length; index++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2), words[index]);
    }
    public int Peek() => Position >= 0 && Position + 2 <= bytes.Length ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(Position)) : throw new InvalidDataException($"Script {Name} read outside its bytecode at {Position}.");
    public int Read() { var value = Peek(); Position += 2; return value; }
    public void Poke(int value) { Peek(); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(Position), unchecked((ushort)value)); }
}