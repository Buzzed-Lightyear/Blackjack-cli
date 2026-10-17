using Spectre.Console;

namespace blackjack.Cli
{
    // Your chips as stacks seen from the front, one per denomination in the casino colours: a
    // face with the value on top, then an edge row per chip, every other one lighter so the chips
    // can be told apart. A stack taller than the panel stops a row short and says how many chips it
    // holds. The bet on the table is a pile of its own chips with the amount under it.
    internal static class ChipArt
    {
        public const int StackWidth = 5;
        public static readonly int[] Denominations = { 500, 100, 25, 5, 1 };
        private const string Edge = "▀▀▀▀▀";

        // The whole chips as the fewest chips of each denomination, largest first; a fraction of
        // a chip is left out.
        public static IReadOnlyList<(int Denomination, int Count)> Decompose(decimal chips)
        {
            var left = (long)Math.Floor(Math.Max(0m, chips));
            var stacks = new List<(int, int)>();
            foreach (var denomination in Denominations)
            {
                var count = left / denomination;
                if (count > 0)
                {
                    stacks.Add((denomination, (int)Math.Min(count, int.MaxValue)));
                    left -= count * denomination;
                }
            }

            return stacks;
        }

        // One stack from the top down, at most rows tall: the face, then an edge per chip; with
        // more chips than rows the edges stop one row short to make room for the count.
        public static string[] Stack(int denomination, int count, int rows)
        {
            var (face, edge, light) = Colours(denomination);
            var lines = new List<string> { $"[bold {face}]{Face(denomination)}[/]" };
            var capped = count > rows - 1;
            var edges = capped ? Math.Max(0, rows - 2) : count;
            for (var i = 0; i < edges; i++)
            {
                lines.Add($"[{(i % 2 == 0 ? edge : light)}]{Edge}[/]");
            }

            if (capped)
            {
                lines.Add(Centre($"[grey]×{count}[/]", StackWidth));
            }

            return lines.Take(rows).ToArray();
        }

        // The bet from the top down, smallest chips on top, no face, and the amount under it.
        public static string[] Pile(decimal bet, int rows)
        {
            var label = $"[grey]bet {bet:0.##}[/]";
            var width = PileWidth(bet);
            var chips = Decompose(bet).Reverse().SelectMany(s => Enumerable.Repeat(s.Denomination, s.Count)).ToList();
            var lines = chips.Take(Math.Max(0, rows - 1))
                .Select((denomination, i) => Pad($"[{(i % 2 == 0 ? Colours(denomination).Edge : Colours(denomination).Light)}]{Edge}[/]", width))
                .ToList();
            lines.Add(Pad(label, width));
            return lines.Take(rows).ToArray();
        }

        public static int PileWidth(decimal bet) => Math.Max(StackWidth, $"bet {bet:0.##}".Length);

        // The columns the rack takes: the stacks a space apart, and the pile two spaces on.
        public static int Width(decimal chips, decimal? bet)
        {
            var stacks = Decompose(chips).Count;
            var width = stacks == 0 ? 0 : stacks * StackWidth + stacks - 1;
            if (bet is { } amount && Decompose(amount).Count > 0)
            {
                width += (stacks == 0 ? 0 : 2) + PileWidth(amount);
            }

            return width;
        }

        // The stacks side by side, then the pile, as rows of the same width so they line up;
        // nothing at all with nothing to draw.
        public static string[] Rack(decimal chips, decimal? bet, int rows)
        {
            var columns = Decompose(chips).Select(s => (Lines: Stack(s.Denomination, s.Count, rows), Width: StackWidth, Gap: 1)).ToList();
            if (bet is { } amount && Decompose(amount).Count > 0)
            {
                columns.Add((Pile(amount, rows), PileWidth(amount), 2));
            }

            if (columns.Count == 0)
            {
                return Array.Empty<string>();
            }

            var height = columns.Max(c => c.Lines.Length);
            return Enumerable.Range(0, height)
                .Select(row => string.Concat(columns.Select((c, i) =>
                    $"{(i == 0 ? "" : new string(' ', c.Gap))}{Pad(row < c.Lines.Length ? c.Lines[row] : "", c.Width)}")))
                .ToArray();
        }

        private static string Face(int denomination) =>
            denomination < 10 ? $"( {denomination} )" : denomination < 100 ? $"( {denomination})" : $"({denomination})";

        private static (string Face, string Edge, string Light) Colours(int denomination) => denomination switch
        {
            500 => ("white on purple", "purple", "mediumorchid"),
            100 => ("black on white", "grey30", "grey54"),
            25 => ("white on green", "green", "green3"),
            5 => ("white on red", "red", "indianred1"),
            _ => ("black on white", "white", "grey74")
        };

        private static string Pad(string markup, int width) => $"{markup}{new string(' ', Math.Max(0, width - new Markup(markup).Length))}";

        private static string Centre(string markup, int width)
        {
            var left = Math.Max(0, (width - new Markup(markup).Length) / 2);
            return Pad($"{new string(' ', left)}{markup}", width);
        }
    }
}
