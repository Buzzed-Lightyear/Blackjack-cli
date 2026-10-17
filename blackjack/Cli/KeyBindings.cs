using System.Text;

namespace blackjack.Cli
{
    internal enum KeyAction
    {
        Hit,
        Stand,
        Double,
        Split,
        Deal,
        BetDown,
        BetUp,
        SideBet1,
        SideBet2,
        Insurance,
        FastForward,
        NextShoe,
        CashOut,
        Book,
        Count,
        Slower,
        Faster
    }

    // One key an action answers to: a printed character (matched by what it types, so it follows the
    // keyboard layout), a named key such as Space or F5, or both.
    internal readonly record struct KeyChord(char? Char, ConsoleKey? Key, string Label)
    {
        private static readonly Dictionary<string, KeyChord> Named = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = new(' ', ConsoleKey.Spacebar, "Space"),
            ["Tab"] = new('\t', ConsoleKey.Tab, "Tab"),
            ["Enter"] = new('\r', ConsoleKey.Enter, "Enter"),
            ["Escape"] = new('\u001b', ConsoleKey.Escape, "Esc"),
            ["Esc"] = new('\u001b', ConsoleKey.Escape, "Esc"),
            ["Backspace"] = new('\b', ConsoleKey.Backspace, "Backspace"),
            ["Up"] = new(null, ConsoleKey.UpArrow, "Up"),
            ["Down"] = new(null, ConsoleKey.DownArrow, "Down"),
            ["Left"] = new(null, ConsoleKey.LeftArrow, "Left"),
            ["Right"] = new(null, ConsoleKey.RightArrow, "Right"),
            ["Home"] = new(null, ConsoleKey.Home, "Home"),
            ["End"] = new(null, ConsoleKey.End, "End"),
            ["PageUp"] = new(null, ConsoleKey.PageUp, "PageUp"),
            ["PageDown"] = new(null, ConsoleKey.PageDown, "PageDown"),
            ["Insert"] = new(null, ConsoleKey.Insert, "Insert"),
            ["Delete"] = new(null, ConsoleKey.Delete, "Delete"),
        };

        // Enter, Escape, Backspace and the digits always mean the same thing when typing a bet.
        public bool IsReserved =>
            Key is ConsoleKey.Enter or ConsoleKey.Escape or ConsoleKey.Backspace || Char is >= '0' and <= '9';

        public bool Matches(ConsoleKeyInfo info) =>
            (Key is { } key && info.Key == key)
            || (Char is { } c && info.KeyChar != '\0' && char.ToUpperInvariant(info.KeyChar) == c);

        // The key event this chord stands for, as the console reports it being pressed.
        public ConsoleKeyInfo ToKeyInfo() => new(Char ?? '\0', Key ?? default, false, false, false);

        public static bool TryParse(string text, out KeyChord chord)
        {
            var name = text.Trim();
            if (name.Length == 1 && !char.IsControl(name[0]) && name[0] != ' ')
            {
                chord = FromChar(name[0]);
                return true;
            }

            if (Named.TryGetValue(name, out chord))
            {
                return true;
            }

            if (name.Length is 2 or 3 && char.ToUpperInvariant(name[0]) == 'F'
                && int.TryParse(name[1..], out var n) && n is >= 1 and <= 12)
            {
                chord = new(null, ConsoleKey.F1 + (n - 1), $"F{n}");
                return true;
            }

            chord = default;
            return false;
        }

        // The chord for a key someone just pressed, if it can be bound.
        public static bool TryFrom(ConsoleKeyInfo info, out KeyChord chord)
        {
            if (info.Key == ConsoleKey.Spacebar || info.KeyChar == ' ')
            {
                chord = Named["Space"];
                return true;
            }

            if (info.KeyChar != '\0' && !char.IsControl(info.KeyChar))
            {
                chord = FromChar(info.KeyChar);
                return true;
            }

            foreach (var named in Named.Values)
            {
                if (named.Key == info.Key)
                {
                    chord = named;
                    return true;
                }
            }

            if (info.Key is >= ConsoleKey.F1 and <= ConsoleKey.F12)
            {
                chord = new(null, info.Key, $"F{info.Key - ConsoleKey.F1 + 1}");
                return true;
            }

            chord = default;
            return false;
        }

        private static KeyChord FromChar(char c)
        {
            var upper = char.ToUpperInvariant(c);
            ConsoleKey? key = upper switch
            {
                >= 'A' and <= 'Z' => ConsoleKey.A + (upper - 'A'),
                >= '0' and <= '9' => ConsoleKey.D0 + (upper - '0'),
                _ => null
            };
            return new(upper, key, upper.ToString());
        }
    }

    // Which key does what, loaded from and saved to an ini file.
    internal sealed class KeyBindings
    {
        public const string FileName = "keys.ini";

        // Keys only have to be unique among actions read at the same moment.
        private static readonly KeyAction[][] Contexts =
        {
            new[] { KeyAction.Deal, KeyAction.BetDown, KeyAction.BetUp, KeyAction.SideBet1, KeyAction.SideBet2, KeyAction.FastForward, KeyAction.NextShoe, KeyAction.CashOut, KeyAction.Book, KeyAction.Count },
            new[] { KeyAction.Hit, KeyAction.Stand, KeyAction.Double, KeyAction.Split, KeyAction.Book, KeyAction.Count },
            new[] { KeyAction.Insurance, KeyAction.Deal, KeyAction.Book, KeyAction.Count },
            new[] { KeyAction.Slower, KeyAction.Faster, KeyAction.NextShoe, KeyAction.Book, KeyAction.Count },
        };

        private readonly Dictionary<KeyAction, KeyChord[]> keys;

        private KeyBindings(Dictionary<KeyAction, KeyChord[]> keys)
        {
            this.keys = keys;
        }

        // Everything sits under the left hand: home row for your move, with A and W beside and
        // above it for double and split, Q/E either side for less/more, T/G for the side bets,
        // Z for insurance (the deal key declines), the thumb on Space to deal.
        public static KeyBindings Defaults() => new(new Dictionary<KeyAction, KeyChord[]>
        {
            [KeyAction.Hit] = Chords("D"),
            [KeyAction.Stand] = Chords("S"),
            [KeyAction.Double] = Chords("A"),
            [KeyAction.Split] = Chords("W"),
            [KeyAction.Deal] = Chords("Space"),
            [KeyAction.BetDown] = Chords("Q"),
            [KeyAction.BetUp] = Chords("E"),
            [KeyAction.SideBet1] = Chords("T"),
            [KeyAction.SideBet2] = Chords("G"),
            [KeyAction.Insurance] = Chords("Z"),
            [KeyAction.FastForward] = Chords("F"),
            [KeyAction.NextShoe] = Chords("R"),
            [KeyAction.CashOut] = Chords("X"),
            [KeyAction.Book] = Chords("B"),
            [KeyAction.Count] = Chords("C"),
            [KeyAction.Slower] = Chords("Q"),
            [KeyAction.Faster] = Chords("E"),
        });

        public static string IniName(KeyAction action) => action switch
        {
            KeyAction.BetDown => "bet_down",
            KeyAction.BetUp => "bet_up",
            KeyAction.SideBet1 => "side_bet_1",
            KeyAction.SideBet2 => "side_bet_2",
            KeyAction.FastForward => "fast_forward",
            KeyAction.NextShoe => "next_shoe",
            KeyAction.CashOut => "cash_out",
            _ => action.ToString().ToLowerInvariant()
        };

        public static string Describe(KeyAction action) => action switch
        {
            KeyAction.Hit => "hit",
            KeyAction.Stand => "stand",
            KeyAction.Double => "double down",
            KeyAction.Split => "split a pair",
            KeyAction.Deal => "deal (place your bet, or decline insurance)",
            KeyAction.BetDown => "lower your bet",
            KeyAction.BetUp => "raise your bet",
            KeyAction.SideBet1 => "add to the first side bet",
            KeyAction.SideBet2 => "add to the second side bet",
            KeyAction.Insurance => "take insurance",
            KeyAction.FastForward => "autopilot (the book plays your seat)",
            KeyAction.NextShoe => "skip to the next shoe",
            KeyAction.CashOut => "cash out",
            KeyAction.Book => "show or hide the book's advice",
            KeyAction.Count => "show or hide the count",
            KeyAction.Slower => "autopilot slower",
            KeyAction.Faster => "autopilot faster",
            _ => action.ToString()
        };

        // An explicit path wins; otherwise keys.ini in the current folder, then next to the program.
        // Saving with no file found creates one in the current folder.
        public static string ResolvePath(string? explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }

            var here = Path.Combine(Environment.CurrentDirectory, FileName);
            if (File.Exists(here))
            {
                return here;
            }

            var beside = Path.Combine(AppContext.BaseDirectory, FileName);
            return File.Exists(beside) ? beside : here;
        }

        public bool Is(KeyAction action, ConsoleKeyInfo info) => keys[action].Any(k => k.Matches(info));

        public string Label(KeyAction action) => keys[action][0].Label;

        public IReadOnlyList<KeyChord> Get(KeyAction action) => keys[action];

        public void Set(KeyAction action, KeyChord chord) => keys[action] = new[] { chord };

        // The action that already answers to this key at the same moment as the given one, if any.
        public KeyAction? Conflict(KeyAction action, KeyChord chord)
        {
            foreach (var context in Contexts.Where(c => c.Contains(action)))
            {
                foreach (var other in context.Where(a => a != action))
                {
                    if (keys[other].Any(k => Overlaps(k, chord)))
                    {
                        return other;
                    }
                }
            }

            return null;
        }

        public static KeyBindings Load(string path, List<string> warnings)
        {
            var bindings = Defaults();
            if (!File.Exists(path))
            {
                return bindings;
            }

            var byName = Enum.GetValues<KeyAction>().ToDictionary(IniName, a => a, StringComparer.OrdinalIgnoreCase);
            var section = "";
            var lineNumber = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                lineNumber++;
                var line = raw.Trim();
                if (line.Length == 0 || line[0] is ';' or '#')
                {
                    continue;
                }

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1].Trim();
                    continue;
                }

                if (!section.Equals("keys", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var equals = line.IndexOf('=');
                if (equals < 0 || !byName.TryGetValue(line[..equals].Trim(), out var action))
                {
                    warnings.Add($"{FileName} line {lineNumber}: unknown setting \"{line}\".");
                    continue;
                }

                var chords = new List<KeyChord>();
                foreach (var name in SplitKeys(line[(equals + 1)..]))
                {
                    if (!KeyChord.TryParse(name, out var chord))
                    {
                        warnings.Add($"{FileName} line {lineNumber}: \"{name}\" isn't a key name.");
                    }
                    else if (chord.IsReserved)
                    {
                        warnings.Add($"{FileName} line {lineNumber}: {chord.Label} is reserved for typing bets.");
                    }
                    else
                    {
                        chords.Add(chord);
                    }
                }

                if (chords.Count > 0)
                {
                    bindings.keys[action] = chords.ToArray();
                }
            }

            bindings.ResolveConflicts(warnings);
            return bindings;
        }

        public void Save(string path)
        {
            var text = new StringBuilder()
                .AppendLine("; Blackjack key bindings. Edit this file, or change them from the game's setup.")
                .AppendLine("; Each action takes one or more keys, separated by commas.")
                .AppendLine("; A key is a letter or symbol, or one of: Space, Tab, Up, Down, Left, Right,")
                .AppendLine("; Home, End, PageUp, PageDown, Insert, Delete, F1-F12.")
                .AppendLine("; Enter, Escape, Backspace and the digits are reserved for typing bets.")
                .AppendLine("; Keys only need to be different from the others read at the same time.")
                .AppendLine()
                .AppendLine("[keys]");

            void Group(string comment, params KeyAction[] actions)
            {
                text.AppendLine($"; {comment}");
                foreach (var action in actions)
                {
                    text.AppendLine($"{IniName(action)} = {string.Join(", ", keys[action].Select(k => k.Label))}");
                }
            }

            Group("your turn", KeyAction.Hit, KeyAction.Stand, KeyAction.Double, KeyAction.Split);
            Group("between hands", KeyAction.Deal, KeyAction.BetDown, KeyAction.BetUp, KeyAction.SideBet1, KeyAction.SideBet2, KeyAction.FastForward, KeyAction.NextShoe, KeyAction.CashOut);
            Group("when the dealer shows an ace (the deal key declines)", KeyAction.Insurance);
            Group("whenever the game is waiting for you", KeyAction.Book, KeyAction.Count);
            Group("while on autopilot (any other key stops it)", KeyAction.Slower, KeyAction.Faster);

            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(path, text.ToString());
        }

        private static KeyChord[] Chords(string names) =>
            SplitKeys(names).Select(n => KeyChord.TryParse(n, out var chord) ? chord : throw new ArgumentException(n)).ToArray();

        // "Q, E" -> Q and E; a lone comma is a key in its own right.
        private static IEnumerable<string> SplitKeys(string value)
        {
            var trimmed = value.Trim();
            if (trimmed == ",")
            {
                return new[] { "," };
            }

            return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static bool Overlaps(KeyChord a, KeyChord b) =>
            (a.Char is { } ac && ac == b.Char) || (a.Key is { } ak && ak == b.Key);

        // A clash puts the later action back on its default key, or everything back if that clashes too.
        private void ResolveConflicts(List<string> warnings)
        {
            var defaults = Defaults();
            foreach (var action in Enum.GetValues<KeyAction>())
            {
                foreach (var chord in keys[action])
                {
                    if (Conflict(action, chord) is { } other)
                    {
                        warnings.Add($"{FileName}: {chord.Label} is set for both {IniName(other)} and {IniName(action)}; {IniName(action)} is back on {defaults.Label(action)}.");
                        keys[action] = defaults.keys[action];
                        break;
                    }
                }
            }

            if (Enum.GetValues<KeyAction>().Any(a => keys[a].Any(c => Conflict(a, c) is not null)))
            {
                warnings.Add($"{FileName}: the bindings still clash, so the defaults are in use.");
                foreach (var action in Enum.GetValues<KeyAction>())
                {
                    keys[action] = defaults.keys[action];
                }
            }
        }
    }
}
