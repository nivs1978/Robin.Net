// Project: Robin.Net
// File: GameProgram.cs
// License: GNU General Public License version 3 (see LICENSE)

using System.Text;

namespace RobinHood.Assets;

public sealed class Mobile
{
    public int Id { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public byte Z { get; set; }
    public ushort BaseSprite { get; set; }
    public byte CarriedBy { get; set; }
    public sbyte Behind { get; set; }
    public byte Above { get; set; }
    public byte AboutFace { get; set; }
    public byte Direction { get; set; }
    public byte Type { get; set; }
    public byte Mobility { get; set; }
    public byte Behavior { get; set; }
    public byte HomeX { get; set; }
    public byte HomeY { get; set; }
    public byte Pose { get; set; }
    public int Mode => Attributes[0];
    public bool Alert { get; set; } = true;
    public byte SeqCount { get; set; } = 0x10;
    public byte SeqName { get; set; } = 0xff;
    public ushort[] SeqBuffer { get; } = Enumerable.Repeat((ushort)0xffff, 16).ToArray();
    public byte TargetX { get; set; }
    public byte TargetY { get; set; }
    public byte SubTargetX { get; set; } = 0xff;
    public byte SubTargetY { get; set; } = 0xff;
    public byte Follow { get; set; }
    public byte SavedMode { get; set; }
    public byte Puff { get; set; } = 0xff;
    public byte PuffFrame { get; set; } = 0xff;
    public byte CellX { get; set; }
    public byte CellY { get; set; }
    // What the last lay_floor drew (mob_sx/sy and the sprite state); clicks and rendering use these, not the live fields.
    public bool OnScreen { get; set; }
    public byte ScreenX { get; set; } = 0xff;
    public byte ScreenY { get; set; } = 0xff;
    public byte DrawnZ { get; set; }
    public byte DrawnDirection { get; set; }
    public byte DrawnPose { get; set; }
    public ushort DrawnBase { get; set; } = 0xffff;
    public byte DrawnAbout { get; set; }
    public byte Speed { get; set; } = 0x0f;
    public required byte[] Attributes { get; init; }
    public required byte[] PoseAliases { get; init; }

    public void SetMode(int mode)
    {
        Attributes[0] = (byte)mode;
        Attributes.AsSpan(1, 3).Clear();
        Alert = true;
    }
}

public sealed record Location(ushort LeftRight, ushort TopBottom, ushort Key, ushort Portal);
public sealed record GameButton(byte Type, ushort X, ushort Y, byte Key);

public sealed class GameProgram
{
    public required ushort TimeFrame { get; init; }
    public required byte[] Sequences { get; init; }
    public required Mobile[] Mobiles { get; init; }
    public required string[] Messages { get; init; }
    public required ushort[] StartRules { get; init; }
    public required ushort[] InputRules { get; init; }
    public required ushort[] RuleOffsets { get; init; }
    public required byte[] RuleCode { get; init; }
    public required byte[] CubeFlags { get; init; }
    public required byte[][] Lists { get; init; }
    public required Location[] Locations { get; init; }
    public required GameButton[] Buttons { get; init; }

    public static GameProgram Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var timeFrame = reader.ReadUInt16();
        var sequences = Bytes(reader, reader.ReadUInt16());
        var mobiles = new Mobile[Count(reader.ReadUInt16(), 40, "mobiles")];
        for (var index = 0; index < mobiles.Length; index++)
        {
            var cubeX = reader.ReadUInt16();
            var cubeY = reader.ReadUInt16();
            mobiles[index] = new Mobile
            {
                Id = index,
                X = cubeX == ushort.MaxValue ? -1 : cubeX * 8 + 4,
                Y = cubeY == ushort.MaxValue ? -1 : cubeY * 8 + 4,
                Z = (byte)reader.ReadUInt16(),
                BaseSprite = reader.ReadUInt16(),
                CarriedBy = reader.ReadByte(),
                Behind = reader.ReadSByte(),
                Above = reader.ReadByte(),
                AboutFace = reader.ReadByte(),
                Direction = reader.ReadByte(),
                Mobility = reader.ReadByte(),
                Type = reader.ReadByte(),
                Behavior = reader.ReadByte(),
                HomeX = reader.ReadByte(),
                HomeY = reader.ReadByte(),
                Attributes = Bytes(reader, 32),
                PoseAliases = Bytes(reader, 32)
            };
        }
        var messageCount = Count(reader.ReadUInt16(), 8192, "messages");
        var messageLength = reader.ReadUInt16();
        var messageOffsets = Words(reader, messageCount);
        var messageData = Bytes(reader, messageLength);
        var messages = messageOffsets.Select(offset => ReadMessage(messageData, offset)).ToArray();
        var startRules = Words(reader, reader.ReadUInt16());
        var inputRules = Words(reader, reader.ReadUInt16());
        var ruleOffsets = Words(reader, reader.ReadUInt16());
        var ruleCode = Bytes(reader, reader.ReadUInt16());
        if (ruleOffsets.Any(offset => offset >= ruleCode.Length)) throw new InvalidDataException("Rule offset outside bytecode.");
        var cubeFlags = Bytes(reader, 60);
        var listSizes = Bytes(reader, reader.ReadByte());
        var lists = listSizes.Select(size => Bytes(reader, size)).ToArray();
        var locations = new Location[Count(reader.ReadUInt16(), 40, "locations")];
        for (var index = 0; index < locations.Length; index++)
            locations[index] = new(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
        var buttonCount = Count(reader.ReadUInt16(), 20, "buttons");
        var types = Bytes(reader, 20);
        var buttonX = Words(reader, 20);
        var buttonY = Words(reader, 20);
        var keys = Bytes(reader, 20);
        var buttons = Enumerable.Range(0, buttonCount).Select(index => new GameButton(types[index], buttonX[index], buttonY[index], keys[index])).ToArray();
        if (stream.Position != stream.Length) throw new InvalidDataException($"Rule file has {stream.Length - stream.Position} unparsed bytes.");
        return new()
        {
            TimeFrame = timeFrame, Sequences = sequences, Mobiles = mobiles, Messages = messages,
            StartRules = startRules, InputRules = inputRules, RuleOffsets = ruleOffsets, RuleCode = ruleCode,
            CubeFlags = cubeFlags, Lists = lists, Locations = locations, Buttons = buttons
        };
    }

    static int Count(int value, int maximum, string field) => value <= maximum ? value : throw new InvalidDataException($"Invalid {field} count: {value}.");

    static byte[] Bytes(BinaryReader reader, int count)
    {
        var bytes = reader.ReadBytes(count);
        return bytes.Length == count ? bytes : throw new EndOfStreamException("Truncated game program.");
    }

    static ushort[] Words(BinaryReader reader, int count)
    {
        var words = new ushort[count];
        for (var index = 0; index < count; index++) words[index] = reader.ReadUInt16();
        return words;
    }

    static string ReadMessage(byte[] bytes, int offset)
    {
        if (offset >= bytes.Length) throw new InvalidDataException("Message offset outside text data.");
        var end = Array.IndexOf(bytes, (byte)0, offset);
        if (end < 0) throw new InvalidDataException("Unterminated game message.");
        return Encoding.Latin1.GetString(bytes, offset, end - offset);
    }
}
