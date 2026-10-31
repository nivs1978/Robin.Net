// Project: Robin.Net
// File: MessageText.cs
// License: GNU General Public License version 3 (see LICENSE)

using System.Text;

namespace RobinHood.Engine;

// decode_it / wrap_it: expand the original message codes and wrap to two 60 column lines ('|' marks a line break).
public static class MessageText
{
    const int Columns = 0x3d;
    static readonly string[] Phrases = "I am |You are |you are |hou art |in the |is the |is a |in a |To the |to the |by |going |here |The|the|and |some |build|not |way|I |a |an |from |of |him|her|by |his |ing |tion|have |you|I've |can't |up |to |he |she |down |what|What|with|are |and|ent|ian|ome|ed |me|my|ai|it|is|of|oo|ea|er|es|th|we|ou|ow|or|gh|go|er|st|ee|th|sh|ch|ct|on|ly|ng|nd|nt|ty|ll|le|de|as|ie|in|ss|'s |'t |re|gg|tt|pp|nn|ay|ar|wh|".Split('|');

    public static string Prepare(string raw, int number) => Wrap(Decode(raw, number));

    public static string Decode(string raw, int number)
    {
        var text = new StringBuilder();
        for (var index = 0; index < raw.Length; index++)
        {
            var character = raw[index];
            if (character == ']') break;
            if (character >= 0x80)
            {
                var phrase = 0xff - character;
                if (phrase < Phrases.Length - 1) text.Append(Phrases[phrase]);
            }
            else if (character == '@')
            {
                var variable = ++index < raw.Length ? raw[index] : '\0';
                if (variable == '#') text.Append(number);
            }
            else text.Append(character);
        }
        return text.ToString();
    }

    public static string Wrap(string text)
    {
        var buffer = new StringBuilder(text);
        var line = 0;
        var column = 0;
        for (var position = 0; position < buffer.Length;)
        {
            var character = buffer[position++];
            if (character == '|') { line++; column = 0; continue; }
            if (++column < Columns) continue;
            if (line == 1) { buffer.Length = position - 1; break; }
            var space = position - 1;
            while (space >= 0 && buffer[space] != ' ') space--;
            if (space < 0) { buffer.Length = position - 1; break; }
            buffer[space] = '|';
            line++;
            column = 0;
            position = space + 1;
        }
        return buffer.ToString();
    }
}
