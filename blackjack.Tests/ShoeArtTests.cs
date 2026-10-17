using blackjack.Cli;
using Spectre.Console;

namespace blackjack.Tests
{
    // The shoe as the dealer's region draws it: a stack whose height follows the cards left, with
    // the cut card marked, and the one-line form for a terminal with no tiles.
    public class ShoeArtTests
    {
        private const int SixDecks = 312;

        private static string Plain(string markup) => Markup.Remove(markup);

        [Theory]
        [InlineData(312, 3, 3)]
        [InlineData(312, 5, 5)]
        [InlineData(234, 3, 3)] // three quarters: 2.25 rows round up
        [InlineData(234, 5, 4)]
        [InlineData(78, 3, 1)]  // just past the cut card
        [InlineData(78, 5, 2)]
        [InlineData(1, 3, 1)]   // nearly empty, never 0 while a card is left
        [InlineData(1, 5, 1)]
        [InlineData(0, 5, 0)]
        [InlineData(400, 5, 5)] // never taller than the height
        public void Rows_FollowTheCardsLeft(int remaining, int height, int rows)
        {
            Assert.Equal(rows, ShoeArt.Rows(remaining, SixDecks, height));
        }

        [Fact]
        public void CutRow_IsAQuarterOfTheWayUp_AtTheDefaultPenetration()
        {
            Assert.Equal(1, ShoeArt.CutRow(0.75m, 3));
            Assert.Equal(2, ShoeArt.CutRow(0.75m, 5));
            Assert.Equal(2, ShoeArt.CutRow(0.5m, 4));
            Assert.Equal(0, ShoeArt.CutRow(1m, 5)); // dealt to the end: no cut card
        }

        [Fact]
        public void CutPassed_WhenTheShoeWouldReshuffle()
        {
            Assert.False(ShoeArt.CutPassed(79, SixDecks, 0.75m));
            Assert.True(ShoeArt.CutPassed(78, SixDecks, 0.75m));
            Assert.False(ShoeArt.CutPassed(1, SixDecks, 1m));
        }

        [Fact]
        public void Block_StandsAFullShoeOnEveryRow_WithTheCutCardAtTheBottom()
        {
            var block = ShoeArt.Block(1, SixDecks, SixDecks, 0.75m, 3);

            Assert.Equal(4, block.Length);
            Assert.All(block, row => Assert.Equal(18, new Markup(row).Length));
            Assert.Contains("▓▓▓▓▓▓▓▓", Plain(block[0]));
            Assert.Contains("▓▓▓▓▓▓▓▓", Plain(block[1]));
            Assert.Contains("▀▀▀▀▀▀▀▀", Plain(block[2]));
            Assert.Contains("[yellow on blue]", block[2]);
            Assert.Equal("shoe 1 · 6.0 decks", Plain(block[3]));
            Assert.DoesNotContain("cut", Plain(block[3]));
        }

        [Fact]
        public void Block_ShortensFromTheTop_AndKeepsTheCutCardAtItsHeight()
        {
            var block = ShoeArt.Block(2, 234, SixDecks, 0.75m, 5);

            Assert.Equal(6, block.Length);
            Assert.True(string.IsNullOrWhiteSpace(Plain(block[0])));
            Assert.Contains("▓▓▓▓▓▓▓▓", Plain(block[1]));
            Assert.Contains("▓▓▓▓▓▓▓▓", Plain(block[2]));
            Assert.Contains("▀▀▀▀▀▀▀▀", Plain(block[3])); // second row from the bottom
            Assert.Contains("▓▓▓▓▓▓▓▓", Plain(block[4]));
            Assert.Equal("shoe 2 · 4.5 decks", Plain(block[5]));
        }

        [Fact]
        public void Block_PastTheCut_HasNoCutRow_AndSaysSo()
        {
            var block = ShoeArt.Block(1, 78, SixDecks, 0.75m, 5);

            Assert.All(block.Take(3), row => Assert.True(string.IsNullOrWhiteSpace(Plain(row))));
            Assert.Contains("▓▓▓▓▓▓▓▓", Plain(block[3]));
            Assert.Contains("▓▓▓▓▓▓▓▓", Plain(block[4]));
            Assert.DoesNotContain(block, row => row.Contains("▀"));
            Assert.Equal("shoe 1 · 1.5 decks · cut", Plain(block[5]));
            Assert.All(block, row => Assert.Equal(24, new Markup(row).Length));
        }

        [Fact]
        public void Block_KeepsOneRow_ForTheLastCard()
        {
            var block = ShoeArt.Block(1, 1, SixDecks, 0.75m, 3);

            Assert.True(string.IsNullOrWhiteSpace(Plain(block[0])));
            Assert.True(string.IsNullOrWhiteSpace(Plain(block[1])));
            Assert.Contains("▓▓▓▓▓▓▓▓", Plain(block[2]));
            Assert.Equal("shoe 1 · 0.0 decks · cut", Plain(block[3]));
        }

        [Fact]
        public void Line_DrainsTowardsTheCutMark()
        {
            Assert.Equal("shoe 1 ▒▒│▒▒▒▒▒▒▒ 6.0 decks", Plain(ShoeArt.Line(1, SixDecks, SixDecks, 0.75m)));
            Assert.Equal("shoe 1 ▒▒│▒▒░░░░░ 3.0 decks", Plain(ShoeArt.Line(1, 156, SixDecks, 0.75m)));
            Assert.Equal("shoe 2 ▒▒▒░░░░░░░ 1.5 decks · cut", Plain(ShoeArt.Line(2, 78, SixDecks, 0.75m)));
            Assert.Equal("shoe 2 ▒░░░░░░░░░ 0.0 decks · cut", Plain(ShoeArt.Line(2, 1, SixDecks, 0.75m)));
            Assert.Equal("shoe 1 ▒▒▒▒▒▒▒▒▒▒ 1.0 decks", Plain(ShoeArt.Line(1, 52, 52, 1m)));
        }

        [Fact]
        public void Line_ShortForm_LeavesOutTheDecks()
        {
            Assert.Equal("shoe 1 ▒▒│▒▒▒▒▒▒▒", Plain(ShoeArt.Line(1, SixDecks, SixDecks, 0.75m, decks: false)));
            Assert.Equal("shoe 2 ▒▒▒░░░░░░░ · cut", Plain(ShoeArt.Line(2, 78, SixDecks, 0.75m, decks: false)));
        }
    }
}
