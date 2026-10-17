using System.Text;
using Spectre.Console;

namespace blackjack.Cli
{
    // What a key event from the console turned out to mean once the sequence it began was read: a
    // mouse report, or a key.
    internal readonly record struct TerminalEvent(MouseClick? Click, ConsoleKeyInfo? Key);

    // Decodes what the console hands over as key events without understanding it. Mouse reports
    // always arrive that way (ESC [ < button ; column ; row M, or m for a release). On Windows in
    // virtual terminal input mode, which mouse reporting needs, so do the named keys: arrows, Home,
    // End, Insert, Delete, PageUp, PageDown and F1 to F12 come as ESC [ or ESC O sequences, and
    // Backspace as DEL. Everything else passes through as it is.
    internal static class TerminalInput
    {
        private const char Escape = '\u001b';
        private const char Delete = '\u007f';
        private const int MaxParameters = 16;

        // Button bits of a mouse report: 0-1 the button (0 is the left one), 2-4 the modifier keys,
        // 5 motion, 6 and 7 the wheel and the extra buttons.
        private const int NotALeftPress = 0b1110_0011;

        // The keys ESC [ n ~ stands for, by n.
        private static readonly Dictionary<int, ConsoleKey> TildeKeys = new()
        {
            [1] = ConsoleKey.Home,
            [2] = ConsoleKey.Insert,
            [3] = ConsoleKey.Delete,
            [4] = ConsoleKey.End,
            [5] = ConsoleKey.PageUp,
            [6] = ConsoleKey.PageDown,
            [11] = ConsoleKey.F1,
            [12] = ConsoleKey.F2,
            [13] = ConsoleKey.F3,
            [14] = ConsoleKey.F4,
            [15] = ConsoleKey.F5,
            [17] = ConsoleKey.F6,
            [18] = ConsoleKey.F7,
            [19] = ConsoleKey.F8,
            [20] = ConsoleKey.F9,
            [21] = ConsoleKey.F10,
            [23] = ConsoleKey.F11,
            [24] = ConsoleKey.F12,
        };

        // Reads on from first, the key just read, when it begins something to decode: Escape with
        // the rest of a sequence already waiting, or a control character standing for a key. Null
        // when first is an ordinary key. Keys read that turned out not to belong to a sequence are
        // left in taken, so the caller can still hand them on in order; a lone Escape is Escape.
        public static TerminalEvent? TryDecode(ConsoleKeyInfo first, IAnsiConsoleInput input, List<ConsoleKeyInfo> taken) =>
            first.KeyChar switch
            {
                Escape => DecodeEscape(input, taken),
                Delete or '\b' => Key('\b', ConsoleKey.Backspace),
                '\r' or '\n' => Key('\r', ConsoleKey.Enter),
                '\t' => Key('\t', ConsoleKey.Tab),
                _ => null
            };

        private static TerminalEvent DecodeEscape(IAnsiConsoleInput input, List<ConsoleKeyInfo> taken)
        {
            var escape = Key(Escape, ConsoleKey.Escape);
            if (!Next(input, taken, out var c))
            {
                return escape;
            }

            if (c == 'O')
            {
                return Next(input, taken, out c) && Named(c) is { } key ? Whole(taken, Key('\0', key)) : escape;
            }

            if (c != '[')
            {
                return escape;
            }

            // A control sequence: parameter characters, then the one final character that names it.
            var parameters = new StringBuilder();
            while (Next(input, taken, out c))
            {
                if (c is >= '0' and <= '?' && parameters.Length < MaxParameters)
                {
                    parameters.Append(c);
                }
                else if (c is >= '@' and <= '~')
                {
                    return Sequence(parameters.ToString(), c) is { } decoded ? Whole(taken, decoded) : escape;
                }
                else
                {
                    break;
                }
            }

            return escape;
        }

        private static TerminalEvent? Sequence(string parameters, char final)
        {
            if (parameters.StartsWith('<'))
            {
                return final is 'M' or 'm' ? MouseReport(parameters[1..], final) : null;
            }

            // Modifier forms such as ESC [ 1 ; 2 A give the key without the modifier.
            if (final == '~')
            {
                var number = parameters.Split(';')[0];
                return int.TryParse(number, out var n) && TildeKeys.TryGetValue(n, out var key) ? Key('\0', key) : null;
            }

            return Named(final) is { } named ? Key('\0', named) : null;
        }

        // Arrows, Home, End and F1 to F4 by the letter that ends their sequence.
        private static ConsoleKey? Named(char final) => final switch
        {
            'A' => ConsoleKey.UpArrow,
            'B' => ConsoleKey.DownArrow,
            'C' => ConsoleKey.RightArrow,
            'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home,
            'F' => ConsoleKey.End,
            'P' => ConsoleKey.F1,
            'Q' => ConsoleKey.F2,
            'R' => ConsoleKey.F3,
            'S' => ConsoleKey.F4,
            _ => null
        };

        private static TerminalEvent? MouseReport(string fields, char final)
        {
            var parts = fields.Split(';');
            if (parts.Length != 3
                || !int.TryParse(parts[0], out var button)
                || !int.TryParse(parts[1], out var column)
                || !int.TryParse(parts[2], out var row)
                || column < 1
                || row < 1)
            {
                return null;
            }

            var pressed = final == 'M' && (button & NotALeftPress) == 0;
            return new TerminalEvent(new MouseClick(column - 1, row - 1, pressed), null);
        }

        // Everything read belonged to the sequence, so nothing is handed back.
        private static TerminalEvent Whole(List<ConsoleKeyInfo> taken, TerminalEvent decoded)
        {
            taken.Clear();
            return decoded;
        }

        private static TerminalEvent Key(char c, ConsoleKey key) => new(null, new ConsoleKeyInfo(c, key, false, false, false));

        private static bool Next(IAnsiConsoleInput input, List<ConsoleKeyInfo> taken, out char c)
        {
            c = '\0';
            if (!input.IsKeyAvailable() || input.ReadKey(intercept: true) is not { } key)
            {
                return false;
            }

            taken.Add(key);
            c = key.KeyChar;
            return true;
        }
    }
}
