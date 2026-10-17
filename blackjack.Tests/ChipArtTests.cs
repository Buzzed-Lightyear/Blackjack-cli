using blackjack.Cli;
using Spectre.Console;

namespace blackjack.Tests
{
    // The chip stacks in your seat: how a balance breaks into chips, how tall a stack may be,
    // and the width the rack takes.
    public class ChipArtTests
    {
        private static string Plain(string markup) => Markup.Remove(markup);

        [Fact]
        public void Decompose_UsesTheFewestChips_LargestFirst()
        {
            Assert.Equal(new[] { (500, 2), (100, 2), (25, 2) }, ChipArt.Decompose(1250m));
            Assert.Equal(new[] { (5, 1), (1, 2) }, ChipArt.Decompose(7m));
            Assert.Equal(new[] { (5, 1), (1, 2) }, ChipArt.Decompose(7.5m)); // the half chip is left out
            Assert.Empty(ChipArt.Decompose(0m));
            Assert.Empty(ChipArt.Decompose(0.9m));
            Assert.Equal(new[] { (500, 20) }, ChipArt.Decompose(10_000m));
        }

        [Fact]
        public void Stack_IsAFaceAndAnEdgePerChip()
        {
            var stack = ChipArt.Stack(500, 2, 5);

            Assert.Equal(new[] { "(500)", "▀▀▀▀▀", "▀▀▀▀▀" }, stack.Select(Plain));
            Assert.Contains("white on purple", stack[0]);
            Assert.Contains("[purple]", stack[1]);
            Assert.Contains("[mediumorchid]", stack[2]); // every other edge is lighter
            Assert.All(stack, row => Assert.Equal(ChipArt.StackWidth, new Markup(row).Length));
        }

        [Fact]
        public void Stack_FacesShowTheValue_InTheCasinoColour()
        {
            Assert.Equal("(100)", Plain(ChipArt.Stack(100, 1, 5)[0]));
            Assert.Equal("( 25)", Plain(ChipArt.Stack(25, 1, 5)[0]));
            Assert.Equal("( 5 )", Plain(ChipArt.Stack(5, 1, 5)[0]));
            Assert.Equal("( 1 )", Plain(ChipArt.Stack(1, 1, 5)[0]));
            Assert.Contains("black on white", ChipArt.Stack(100, 1, 5)[0]);
            Assert.Contains("white on green", ChipArt.Stack(25, 1, 5)[0]);
            Assert.Contains("white on red", ChipArt.Stack(5, 1, 5)[0]);
            Assert.Contains("black on white", ChipArt.Stack(1, 1, 5)[0]);
        }

        [Fact]
        public void Stack_TallerThanThePanel_StopsShort_AndCountsItsChips()
        {
            var capped = ChipArt.Stack(500, 7, 5);
            Assert.Equal(new[] { "(500)", "▀▀▀▀▀", "▀▀▀▀▀", "▀▀▀▀▀", " ×7  " }, capped.Select(Plain));

            // Four chips fill five rows exactly, so no count is needed.
            Assert.Equal(5, ChipArt.Stack(5, 4, 5).Length);
            Assert.DoesNotContain(ChipArt.Stack(5, 4, 5), row => row.Contains('×'));
            Assert.Equal(new[] { "(100)", "▀▀▀▀▀", "▀▀▀▀▀" }, ChipArt.Stack(100, 2, 3).Select(Plain));
            Assert.Equal(new[] { "(100)", "▀▀▀▀▀", " ×3  " }, ChipArt.Stack(100, 3, 3).Select(Plain));
        }

        [Fact]
        public void Pile_IsTheBetsChips_SmallestOnTop_WithTheAmountUnder()
        {
            var pile = ChipArt.Pile(35m, 5);
            Assert.Equal(new[] { "▀▀▀▀▀ ", "▀▀▀▀▀ ", "▀▀▀▀▀ ", "bet 35" }, pile.Select(Plain));
            Assert.Contains("[red]", pile[0]);
            Assert.Contains("[indianred1]", pile[1]);
            Assert.Contains("[green]", pile[2]);

            // The label keeps its row when the chips would need more.
            Assert.Equal(new[] { "▀▀▀▀▀ ", "▀▀▀▀▀ ", "bet 20" }, ChipArt.Pile(20m, 3).Select(Plain));
            Assert.Equal(8, ChipArt.PileWidth(1000m));
        }

        [Fact]
        public void Width_CountsTheStacksAGapApart_AndThePile()
        {
            Assert.Equal(17, ChipArt.Width(1250m, null));
            Assert.Equal(25, ChipArt.Width(1250m, 20m));
            Assert.Equal(11, ChipArt.Width(7m, null));
            Assert.Equal(0, ChipArt.Width(0m, null));
            Assert.Equal(6, ChipArt.Width(0m, 10m)); // all in: only the pile
            Assert.Equal(17, ChipArt.Width(1250m, 0.5m)); // no whole chip to pile
        }

        [Fact]
        public void Rack_LinesUpTheStacksAndThePile_RowByRow()
        {
            var rack = ChipArt.Rack(1250m, 20m, 5);

            Assert.Equal(5, rack.Length);
            Assert.All(rack, row => Assert.Equal(25, new Markup(row).Length));
            Assert.Equal("(500) (100) ( 25)  ▀▀▀▀▀ ", Plain(rack[0]));
            Assert.Equal("▀▀▀▀▀ ▀▀▀▀▀ ▀▀▀▀▀  ▀▀▀▀▀ ", Plain(rack[1]));
            Assert.Equal("▀▀▀▀▀ ▀▀▀▀▀ ▀▀▀▀▀  ▀▀▀▀▀ ", Plain(rack[2]));
            Assert.Equal("                   ▀▀▀▀▀ ", Plain(rack[3]));
            Assert.Equal("                   bet 20", Plain(rack[4]));

            Assert.Equal(3, ChipArt.Rack(1250m, null, 5).Length);
            Assert.Empty(ChipArt.Rack(0m, null, 5));
            Assert.Equal(new[] { "bet 10" }, ChipArt.Rack(0m, 10m, 5).Select(Plain).Select(row => row.Trim()).Where(row => row.StartsWith("bet")));
        }
    }
}
