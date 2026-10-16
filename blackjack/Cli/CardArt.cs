using blackjack.Domain;

namespace blackjack.Cli
{
    // Cards as white tiles for the dealer and you (three lines tall, or five on a tall terminal),
    // and a one-line chip everywhere else.
    internal static class CardArt
    {
        public const int TileWidth = 5;
        public const int TileHeight = 3;
        public const int TallTileHeight = 5;

        // Rank in the top-left corner, suit in the middle, rank again bottom-right. The tall tile
        // has a blank line either side of the suit.
        public static string[] Tile(Card card, int height = TileHeight)
        {
            var style = Style(card);
            var blanks = Enumerable.Repeat($"[{style}]{new string(' ', TileWidth)}[/]", (height - TileHeight) / 2).ToArray();
            return new[] { $"[{style}]{card.RankLabel.PadRight(TileWidth)}[/]" }
                .Concat(blanks)
                .Append($"[{style}]  {card.SuitSymbol}  [/]")
                .Concat(blanks)
                .Append($"[{style}]{card.RankLabel.PadLeft(TileWidth)}[/]")
                .ToArray();
        }

        public static string[] Back(int height = TileHeight) => Enumerable.Repeat("[white on blue]▒▒▒▒▒[/]", height).ToArray();

        // Tiles side by side, as one block of markup lines.
        public static string Row(IEnumerable<string[]> tiles)
        {
            var list = tiles.ToList();
            var height = list.Count == 0 ? 0 : list[0].Length;
            return string.Join("\n", Enumerable.Range(0, height).Select(line => string.Join(" ", list.Select(t => t[line]))));
        }

        public static string Chip(Card card) => $"[{Style(card)}]{card}[/]";

        public const string HiddenChip = "[white on blue]▒▒[/]";

        // A chip with a little padding, for the event log.
        public static string LogChip(Card card) => $"[{Style(card)}] {card} [/]";

        private static string Style(Card card) => card.IsRed ? "red on white" : "black on white";
    }
}
