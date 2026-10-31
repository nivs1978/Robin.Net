// Project: Robin.Net
// File: Movement.cs
// License: GNU General Public License version 3 (see LICENSE)

using RobinHood.Assets;

namespace RobinHood.Engine;

// Sequencer, forward movement, route finding and spatial interactions (ISOVGA 5E26-5E85, 5EB0-6362, 638F-6693, 66BC).
public sealed partial class GameWorld
{
    static readonly byte[] AvoidEntry = [1, 2, 4, 8];
    static readonly byte[] AvoidExit = [8, 4, 2, 1];
    static readonly int[] DisplaceX = [1, 0, 0, -1];
    static readonly int[] DisplaceY = [0, -1, 1, 0];
    static readonly byte[] TurnLeft = [1, 3, 0, 2];
    static readonly byte[] TurnRight = [2, 0, 3, 1];
    static readonly byte[] WayTable = [0, 2, 0, 1, 3, 2, 3, 1];
    ushort[]? sequenceLibrary;

    ushort[] SequenceLibrary => sequenceLibrary ??= Enumerable.Range(0, Program.Sequences.Length / 2)
        .Select(index => (ushort)(Program.Sequences[index * 2] | Program.Sequences[index * 2 + 1] << 8)).ToArray();

    public ushort Interaction(int source, int target) => (ushort)(InteractionData[source * 80 + target * 2] | InteractionData[source * 80 + target * 2 + 1] << 8);

    public void ClearInteractions(int mobile)
    {
        for (var index = 0; index < 40; index++) InteractionData[mobile * 80 + index * 2] = 0;
        for (var index = 0; index < 40; index++) InteractionData[mobile * 2 + index * 40] = 0;
    }

    // set_sequence / set_sequence2: copy the sequence's words into the mobile's own mutable buffer and restart it.
    public void SetSequence(int index, int number)
    {
        if (index >= Mobiles.Length) return;
        var mobile = Mobiles[index];
        mobile.SeqName = (byte)number;
        var library = SequenceLibrary;
        var position = 0;
        for (var skipped = 0; skipped < number; skipped++)
            while (position < library.Length && library[position++] != 0xffff) { }
        mobile.SeqCount = 0;
        for (var word = 0; word < 16; word++) mobile.SeqBuffer[word] = position + word < library.Length ? library[position + word] : (ushort)0;
    }

    public void SetPose(int index, int alias)
    {
        var flat = index * 32 + alias;
        var table = Mobiles[flat / 32 % Mobiles.Length].PoseAliases;
        Mobiles[index].Pose = table[flat % 32];
    }

    public void UpdateCells()
    {
        foreach (var mobile in Mobiles)
        {
            mobile.CellX = (byte)((ushort)mobile.X >> 3);
            mobile.CellY = (byte)((ushort)mobile.Y >> 3);
            var cubeX = ((ushort)mobile.X >> 3) - ViewX;
            var cubeY = ((ushort)mobile.Y >> 3) - ViewY;
            mobile.OnScreen = cubeX is >= 0 and <= 7 && cubeY is >= 0 and <= 7;
            mobile.ScreenX = mobile.ScreenY = 0xff;
            if (!mobile.OnScreen) continue;
            var normalX = mobile.X - ViewX * 8;
            var normalY = mobile.Y - ViewY * 8;
            mobile.ScreenX = (byte)((60 + normalX - normalY) * 2);
            mobile.ScreenY = (byte)(20 + normalX + normalY - mobile.Z);
            mobile.DrawnZ = mobile.Z;
            mobile.DrawnDirection = mobile.Direction;
            mobile.DrawnPose = mobile.Pose;
            mobile.DrawnBase = mobile.BaseSprite;
            mobile.DrawnAbout = mobile.AboutFace;
        }
    }

    // sub_5E26: run each mobile's sequence, highest number first, until an operation yields.
    public void RunSequencers()
    {
        var budget = 100000;
        for (var index = Mobiles.Length - 1; index >= 0; index--)
        {
            var mobile = Mobiles[index];
            while (mobile.SeqCount != 0x10)
            {
                if (mobile.SeqCount > 0x10 || --budget < 0) throw new InvalidDataException($"Sequence {mobile.SeqName} of mobile {index} left its buffer.");
                var result = SequenceOperation(index, mobile, mobile.SeqBuffer[mobile.SeqCount]);
                if ((result & 1) == 0 && ++mobile.SeqCount == 0x10) mobile.Alert = true;
                if ((result & 2) == 0) break;
            }
        }
    }

    int SequenceOperation(int index, Mobile mobile, ushort word)
    {
        var high = word >> 8;
        var low = word & 255;
        switch (high >> 4)
        {
            case 0:
                SetPose(index, low);
                SequenceMotion(index, mobile, high);
                return 0;
            case 1:
                mobile.Direction = (byte)(high & 3);
                SetPose(index, low);
                return 0;
            case >= 2 and <= 9: return 0;
            case 10:
            {
                var target = Mobiles[mobile.Follow];
                if (mobile.SubTargetX != 0xff && mobile.SubTargetX == mobile.TargetX && mobile.SubTargetY == mobile.TargetY)
                {
                    mobile.SubTargetX = target.CellX;
                    mobile.SubTargetY = target.CellY;
                }
                mobile.TargetX = target.CellX;
                mobile.TargetY = target.CellY;
                return SequenceHome(index, mobile, high);
            }
            case 11:
                Audio.StartSound(low, 0xff00 | index, mobile.CellX, mobile.CellY);
                return 2;
            case 12: return SequenceHome(index, mobile, high);
            case 13: mobile.Mobility = (byte)low; return 2;
            case 14:
            {
                if (low != 0)
                {
                    if ((low & 0xf0) == 0) low |= low << 4;
                    low -= 0x10;
                    mobile.SeqBuffer[mobile.SeqCount] = (ushort)(high << 8 | low);
                    if ((low & 0xf0) == 0) return 2;
                }
                mobile.SeqCount -= (byte)(high & 15);
                return 3;
            }
            default:
                mobile.SeqCount = 0x10;
                mobile.Alert = true;
                return 1;
        }
    }

    void SequenceMotion(int index, Mobile mobile, int kind)
    {
        switch (kind)
        {
            case 0: return;
            case 1: Forward(mobile, 2); return;
            case 2: Forward(mobile, 4); return;
            case 3: Forward(mobile, -2); return;
            case 4: mobile.Direction = TurnLeft[mobile.Direction & 3]; return;
            case 5: mobile.Direction = TurnRight[mobile.Direction & 3]; return;
            case 6: mobile.Z += 1; return;
            case 7: mobile.Z += 2; return;
            case 8: mobile.Z -= 1; return;
            case 9: mobile.Z -= 2; return;
            case 10: Forward(mobile, 3); return;
            default: throw new InvalidDataException($"Sequence motion {kind} is outside the original jump table.");
        }
    }

    void Forward(Mobile mobile, int distance)
    {
        var x = mobile.X;
        var y = mobile.Y;
        switch (mobile.Direction)
        {
            case 0: x += distance; break;
            case 1: y -= distance; break;
            case 2: y += distance; break;
            default: x -= distance; break;
        }
        AvoidCollision(mobile, (ushort)x, (ushort)y);
    }

    // avoid_collision: only commit a move that leaves the cell through an open edge into a walkable cube.
    void AvoidCollision(Mobile mobile, int x, int y)
    {
        if ((byte)(x >> 3) == mobile.CellX && (byte)(y >> 3) == mobile.CellY) { mobile.X = x; mobile.Y = y; return; }
        if (x >= 0x200 || y >= 0x200) return;
        var direction = mobile.Direction & 3;
        var current = (mobile.CellY * 64 + mobile.CellX) * 4;
        if (current + 3 >= Map.Length || (AvoidExit[direction] & Map[current + 3]) == 0) return;
        var destination = ((y >> 3) * 64 + (x >> 3)) * 4;
        if ((AvoidEntry[direction] & Map[destination + 3]) == 0) return;
        var flags = Map[destination] < Program.CubeFlags.Length ? Program.CubeFlags[Map[destination]] : 0;
        if (((mobile.Mobility & 7 ^ 7) & flags) != 0) return;
        mobile.X = x;
        mobile.Y = y;
    }

    // which_way (byte deltas) and the 16-bit variant used by a_faceobj: quantise a delta to one of four directions.
    public static int WhichWay(int dx, int dy, bool wide = false)
    {
        var way = 0;
        var a = wide ? (short)dx : (sbyte)dx;
        var b = wide ? (short)dy : (sbyte)dy;
        if (a < 0) { way |= 4; a = (short)-a; }
        if (b < 0) { way |= 2; b = (short)-b; }
        if (!wide) { a = (sbyte)a; b = (sbyte)b; }
        if (a < b) way |= 1;
        return WayTable[way];
    }

    // seq_home: walk the route (via enclosure portals) toward the target, looping the sequence until arrival.
    int SequenceHome(int index, Mobile mobile, int high)
    {
        bool recalculate;
        if (mobile.SubTargetX == 0xff) recalculate = true;
        else if (mobile.SubTargetX != mobile.CellX || mobile.SubTargetY != mobile.CellY) recalculate = false;
        else if (mobile.SubTargetX != mobile.TargetX || mobile.SubTargetY != mobile.TargetY) recalculate = true;
        else return 2;
        if (recalculate)
        {
            CalculateRoute(mobile);
            mobile.Direction = (byte)WhichWay(mobile.SubTargetX - mobile.CellX, mobile.SubTargetY - mobile.CellY);
        }
        FaceBestDirection(mobile);
        mobile.SeqCount -= (byte)(high & 15);
        return 3;
    }

    int Enclosure(int x, int y)
    {
        for (var index = 0; index < Program.Locations.Length; index++)
        {
            var location = Program.Locations[index];
            if ((sbyte)x >= (sbyte)(location.LeftRight >> 8) && (sbyte)x <= (sbyte)location.LeftRight &&
                (sbyte)y >= (sbyte)(location.TopBottom >> 8) && (sbyte)y <= (sbyte)location.TopBottom) return index;
        }
        return -1;
    }

    int OuterEnclosure(int x, int y)
    {
        for (var index = Program.Locations.Length - 1; index >= 0; index--)
        {
            var location = Program.Locations[index];
            if ((sbyte)x >= (sbyte)(location.LeftRight >> 8) && (sbyte)x <= (sbyte)location.LeftRight &&
                (sbyte)y >= (sbyte)(location.TopBottom >> 8) && (sbyte)y <= (sbyte)location.TopBottom) return index;
        }
        return -1;
    }

    static bool Inside(Location location, int x, int y) =>
        (byte)x >= location.LeftRight >> 8 && (byte)x <= (location.LeftRight & 255) && (byte)y >= location.TopBottom >> 8 && (byte)y <= (location.TopBottom & 255);

    void CalculateRoute(Mobile mobile)
    {
        var source = Enclosure(mobile.CellX, mobile.CellY);
        var destination = Enclosure(mobile.TargetX, mobile.TargetY);
        if (destination == source)
        {
            mobile.SubTargetX = mobile.TargetX;
            mobile.SubTargetY = mobile.TargetY;
            return;
        }
        if (source == -1)
        {
            var outer = OuterEnclosure(mobile.TargetX, mobile.TargetY);
            if (outer < 0) return;
            mobile.SubTargetX = (byte)(Program.Locations[outer].Portal >> 8);
            mobile.SubTargetY = (byte)Program.Locations[outer].Portal;
            return;
        }
        if (destination != -1 && Inside(Program.Locations[source], mobile.TargetX, mobile.TargetY))
        {
            mobile.SubTargetX = (byte)(Program.Locations[destination].Portal >> 8);
            mobile.SubTargetY = (byte)Program.Locations[destination].Portal;
            return;
        }
        var from = Program.Locations[source];
        var x = from.Portal >> 8;
        var y = from.Portal & 255;
        var left = from.LeftRight >> 8;
        var right = from.LeftRight & 255;
        var top = from.TopBottom >> 8;
        var bottom = from.TopBottom & 255;
        if (left == right || (x != left && x != right && top == bottom))
        {
            var gate = Map[(y * 64 + x) * 4 + 3];
            if ((gate & 8) != 0) x++;
            else if ((gate & 4) != 0) y--;
            else if ((gate & 2) != 0) y++;
            else x--;
        }
        else if (x == left) x--;
        else if (x == right) x++;
        else if (y == top) y--;
        else y++;
        mobile.SubTargetX = (byte)x;
        mobile.SubTargetY = (byte)y;
    }

    // sub_613A / weight_dirns / avoid_culdesacs: rank the four directions and pick the best open one.
    void FaceBestDirection(Mobile mobile)
    {
        var homeX = mobile.CellX;
        var homeY = mobile.CellY;
        var score = new sbyte[4];
        var distance = new int[4];
        for (var direction = 0; direction < 4; direction++)
        {
            var dx = (sbyte)(homeX + DisplaceX[direction] - mobile.SubTargetX);
            var dy = (sbyte)(homeY + DisplaceY[direction] - mobile.SubTargetY);
            distance[direction] = dx * dx + dy * dy;
        }
        for (var rank = 6; rank > 0; rank -= 2)
        {
            var best = 0x7fff;
            var chosen = 0;
            for (var direction = 0; direction < 4; direction++)
                if (distance[direction] < best) { best = distance[direction]; chosen = direction; }
            distance[chosen] = 0x7fff;
            score[chosen] = (sbyte)rank;
        }
        score[mobile.Direction & 3 ^ 3] -= 8;
        var blocked = 0;
        var here = (homeY * 64 + homeX) * 4;
        for (var direction = 3; direction >= 0; direction--)
        {
            var ahead = here + (direction switch { 0 => 4, 1 => -256, 2 => 256, _ => -4 });
            var open = ahead >= 0 && ahead + 3 < Map.Length && (AvoidEntry[direction] & Map[ahead + 3]) != 0 && (AvoidExit[direction] & Map[here + 3]) != 0;
            if (open)
            {
                if ((Map[ahead + 3] & 0x80) != 0 && AvoidsCulDeSac(mobile, direction)) score[direction] -= 20;
                var flags = Map[ahead] < Program.CubeFlags.Length ? Program.CubeFlags[Map[ahead]] : 0;
                open = ((mobile.Mobility & 7 ^ 7) & flags) == 0;
            }
            if (open) continue;
            score[direction] = unchecked((sbyte)0x9e);
            blocked++;
        }
        if (blocked != 0) score[mobile.Direction & 3] += 3;
        var bestScore = unchecked((sbyte)0x9d);
        var bestDirection = 0;
        for (var direction = 3; direction >= 0; direction--)
            if (bestScore < score[direction]) { bestScore = score[direction]; bestDirection = direction; }
        mobile.Direction = (byte)bestDirection;
    }

    bool AvoidsCulDeSac(Mobile mobile, int direction)
    {
        var neighbour = Enclosure(mobile.CellX + DisplaceX[direction], mobile.CellY + DisplaceY[direction]);
        if (neighbour == -1) return true;
        var bound = Program.Locations[neighbour];
        if (Inside(bound, mobile.CellX, mobile.CellY)) return false;
        if (Inside(bound, mobile.SubTargetX, mobile.SubTargetY)) return false;
        return true;
    }

    // check_interactions: for every source mobile and every other mobile, record whether it is near, in front, in line, or blocked.
    public void CheckInteractions()
    {
        for (var source = Mobiles.Length - 1; source >= 0; source--)
        {
            var viewer = Mobiles[source];
            if ((viewer.Type & 1) != 0) continue;
            for (var target = Mobiles.Length - 1; target >= 0; target--)
            {
                var other = Mobiles[target];
                var state = 0;
                if (source != target && target != viewer.CarriedBy && source != other.CarriedBy && (other.Type & 2) == 0 && other.CellX != 0xff)
                    state = InteractionState(viewer, other);
                var offset = source * 80 + target * 2;
                var current = InteractionData[offset];
                if (current != state) viewer.Alert = true;
                InteractionData[offset + 1] = current;
                InteractionData[offset] = (byte)state;
            }
        }
    }

    int InteractionState(Mobile viewer, Mobile other)
    {
        var dx = (sbyte)(viewer.CellX - other.CellX);
        var dy = (sbyte)(viewer.CellY - other.CellY);
        if (dx >= 6 || dx <= -6 || dy >= 6 || dy <= -6) return 0;
        if (viewer.CellX == other.CellX && viewer.CellY == other.CellY) return 4;
        if ((viewer.Type & 4) != 0) return 0;
        int state;
        switch (viewer.Direction)
        {
            case 0:
                if ((sbyte)other.CellX <= (sbyte)viewer.CellX) return 1;
                state = viewer.CellY != other.CellY ? 2 : 3;
                break;
            case 1:
                if ((sbyte)other.CellY >= (sbyte)viewer.CellY) return 1;
                state = viewer.CellX != other.CellX ? 2 : 3;
                break;
            case 2:
                if ((sbyte)other.CellY <= (sbyte)viewer.CellY) return 1;
                state = viewer.CellX != other.CellX ? 2 : 3;
                break;
            default:
                if ((sbyte)other.CellX >= (sbyte)viewer.CellX) return 1;
                state = viewer.CellY != other.CellY ? 2 : 3;
                break;
        }
        return Obstructed(viewer.CellX, viewer.CellY, other.CellX, other.CellY) ? 1 : state;
    }

    // obstruction_test: walk the cells between two points (Bresenham) looking for a cube whose occlusion byte is not 0xff.
    bool Obstructed(int fromX, int fromY, int toX, int toY)
    {
        var major = (sbyte)(toX - fromX);
        var minor = (sbyte)(toY - fromY);
        int stepX, stepY;
        if (major < 0) { major = (sbyte)-major; stepX = -4; } else stepX = 4;
        if (minor < 0) { minor = (sbyte)-minor; stepY = -256; } else stepY = 256;
        int straight;
        if (major >= minor) straight = stepX;
        else { (major, minor) = (minor, major); straight = stepY; }
        var diagonal = stepX + stepY;
        var doubled = (sbyte)(minor * 2);
        var error = (sbyte)(doubled - major);
        var straightIncrement = doubled;
        var diagonalIncrement = (sbyte)(doubled - major - major);
        var position = fromY * 256 + fromX * 4 + 1;
        for (var step = 0; step <= major; step++)
        {
            // Off-map sources read past the end of the map buffer in the original, where the byte is not 0xff.
            if (position < 0 || position >= Map.Length || Map[position] != 0xff) return true;
            if (error < 0) { position += straight; error = (sbyte)(error + straightIncrement); }
            else { position += diagonal; error = (sbyte)(error + diagonalIncrement); }
        }
        return false;
    }

    // sub_66BC: remember whether each mobile stands on a hazardous cube (bit 0x40) and alert on entry.
    public void UpdateStung()
    {
        for (var index = Mobiles.Length - 1; index >= 0; index--)
        {
            var mobile = Mobiles[index];
            var offset = (mobile.CellY * 64 + mobile.CellX) * 4 + 3;
            var flag = (byte)(offset < Map.Length ? Map[offset] & 0x40 : 0);
            if (flag == Stung[index]) continue;
            Stung[index] = flag;
            if (flag != 0) mobile.Alert = true;
        }
    }
}
