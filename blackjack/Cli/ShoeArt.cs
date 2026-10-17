using blackjack.Domain;
using Spectre.Console;

namespace blackjack.Cli
{
    // The shoe seen from the side: a stack of card backs that gets shorter as the cards come out,
    // with the cut card as a yellow row at its height until the deal reaches it, and under it the
    // shoe's number and the decks left. With no room for tiles the shoe is one line instead: a bar
    // that drains from the right towards the cut mark. Both are drawn afresh from the cards left.
    internal static class ShoeArt
    {
        public const int StackWidth = 8;
        public const int BarWidth = 10;
        private const string Back = "[white on blue]▓▓▓▓▓▓▓▓[/]";
        private const string CutCard = "[yellow on blue]▀▀▀▀▀▀▀▀[/]";
        private const string Cut = "[grey]·[/] [yellow]cut[/]";

        // How many of maxRows the cards left fill: at least one while any card is left, so the
        // stack is never gone before the shoe is.
        public static int Rows(int remaining, int total, int maxRows)
        {
            if (remaining <= 0 || total <= 0 || maxRows <= 0)
            {
                return 0;
            }

            var rows = (int)Math.Ceiling(maxRows * (double)remaining / total);
            return Math.Clamp(rows, 1, maxRows);
        }

        // The row the cut card is in, counted from the bottom of a full stack of maxRows; 0 when
        // the whole shoe is dealt and there is no cut card to draw.
        public static int CutRow(decimal penetration, int maxRows) => (int)Math.Ceiling(maxRows * (1m - penetration));

        // Whether the deal has reached the cut card, as the shoe decides it: a reshuffle follows this round.
        public static bool CutPassed(int remaining, int total, decimal penetration) =>
            remaining <= (int)Math.Round(total * (1m - penetration));

        // The stack bottom-aligned on maxRows rows, centred over its label on one more; every row
        // is the width of the widest, so the block sits flush at the edge of a region.
        public static string[] Block(int shoeNumber, int remaining, int total, decimal penetration, int maxRows)
        {
            var label = Label(shoeNumber, remaining, total, penetration);
            var width = Math.Max(StackWidth, new Markup(label).Length);
            var rows = Rows(remaining, total, maxRows);
            var cut = CutPassed(remaining, total, penetration) ? 0 : CutRow(penetration, maxRows);
            var left = (width - StackWidth) / 2;

            var block = new string[maxRows + 1];
            for (var i = 0; i < maxRows; i++)
            {
                var fromBottom = maxRows - i;
                block[i] = fromBottom > rows
                    ? new string(' ', width)
                    : $"{new string(' ', left)}{(fromBottom == cut ? CutCard : Back)}{new string(' ', width - left - StackWidth)}";
            }

            block[maxRows] = $"{new string(' ', width - new Markup(label).Length)}{label}";
            return block;
        }

        public static string Label(int shoeNumber, int remaining, int total, decimal penetration) =>
            $"[grey]shoe {shoeNumber} ·[/] {Decks(remaining, total, penetration)}";

        // The one-line shoe: the cards left fill the bar from the left, the cut card is the yellow
        // mark, and what is dealt is left empty, so the bar drains towards the mark. The short
        // form leaves out the decks, for a line with no room for them.
        public static string Line(int shoeNumber, int remaining, int total, decimal penetration, bool decks = true)
        {
            var filled = Rows(remaining, total, BarWidth);
            var passed = CutPassed(remaining, total, penetration);
            var cut = passed ? 0 : CutRow(penetration, BarWidth);
            var cells = Enumerable.Range(1, BarWidth)
                .Select(i => i == cut ? "[yellow]│[/]" : i <= filled ? "[white on blue]▒[/]" : "[grey]░[/]");
            var tail = decks ? $" {Decks(remaining, total, penetration)}" : passed ? $" {Cut}" : "";
            return $"[grey]shoe {shoeNumber}[/] {string.Concat(cells)}{tail}";
        }

        private static string Decks(int remaining, int total, decimal penetration)
        {
            var decks = $"[grey]{remaining / (decimal)Shoe.CardsPerDeck:0.0} decks[/]";
            return CutPassed(remaining, total, penetration) ? $"{decks} {Cut}" : decks;
        }
    }
}
