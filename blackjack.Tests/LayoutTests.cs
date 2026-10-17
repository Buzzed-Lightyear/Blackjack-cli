using System.Text.RegularExpressions;
using blackjack.Cli;
using blackjack.Domain;
using blackjack.Engine;
using blackjack.Engine.SideBets;
using blackjack.Strategies;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    // How the table fits terminals of every size, follows a resize, and keeps its regions still.
    // Frames rendered outside a live display follow each other in the output with no separator,
    // so each test slices off the part written by its own Refresh.
    public class LayoutTests
    {
        private static TestConsole NewConsole(int width, int height) =>
            new TestConsole().Width(width).Height(height).Interactive();

        private static ConsoleKeyInfo Press(ConsoleKey key) => new(char.ToLowerInvariant((char)key), key, false, false, false);

        private sealed class Stands : IPlayerStrategy
        {
            public decimal DecideBet(decimal chips, TableContext table) => table.Rules.MinimumBet;

            public bool DecideInsurance(Hand hand, TableContext table) => false;

            public Move DecideMove(Hand hand, Card dealerUpcard, MoveOptions options, TableContext table) => Move.Stand;
        }

        private sealed record Table(Game Game, TableScreen Screen, Player Human, ISideBet[] SideBets);

        // Six bots around you (fewer on request), with 21+3 and Bust It on offer. The shoe is
        // stacked so that with six bots you hold a pair of 8s against a 6 and nobody has a natural.
        private static Table NewTable(IAnsiConsole console, int bots = 6)
        {
            var human = new Player("Tester", 100m, new Stands());
            var seats = Enumerable.Range(1, bots).Select(i => new Player($"Bot {i}", 100m, new Stands())).ToList();
            seats.Insert(seats.Count / 2, human);
            var sideBets = new[] { SideBets.All[0], SideBets.All[1] };
            var shoe = Shoe.Stacked(Of("10h 10c 10d 8h 10s 9h 9c 6d 7h 7c 7d 8c 7s 8d 8s 10s 3h Kc 5c 9s"));
            var game = new Game(new Rules(), seats, shoe, sideBets);
            var screen = new TableScreen(console, KeyBindings.Defaults(), new FastForward());
            screen.Seat(game, human, new CardCounter(), new SessionStats(100m), 1);
            screen.Round = 1;
            return new Table(game, screen, human, sideBets);
        }

        // Everyone bets, you stake both side bets, and the bots before you stand: your move.
        private static void DealToYourMove(Table table)
        {
            foreach (var seat in table.Game.Players)
            {
                table.Game.PlaceBet(seat, 10m);
            }

            table.Game.PlaceSideBet(table.Human, table.SideBets[0], 10m);
            table.Game.PlaceSideBet(table.Human, table.SideBets[1], 10m);
            table.Game.Deal();
            while (table.Game.CurrentPlayer != table.Human)
            {
                table.Game.Stand();
            }
        }

        private static string Frame(TestConsole console, TableScreen screen)
        {
            var start = console.Output.Length;
            screen.Refresh();
            return console.Output[start..];
        }

        private static string[] Lines(string frame) => (frame.EndsWith('\n') ? frame[..^1] : frame).Split('\n');

        private static int RowOf(string frame, string pattern) =>
            Array.FindIndex(Lines(frame), line => Regex.IsMatch(line, pattern));

        private static void AssertFills(string frame, int width, int height)
        {
            var lines = Lines(frame);
            Assert.Equal(height, lines.Length);
            Assert.All(lines, line => Assert.True(line.Length <= width, $"{line.Length} wide: {line}"));
        }

        [Theory]
        [InlineData(60, 16)]
        [InlineData(80, 24)]
        [InlineData(100, 30)]
        [InlineData(120, 45)]
        [InlineData(200, 60)]
        public void Table_FillsTheConsole(int width, int height)
        {
            var console = NewConsole(width, height);
            var table = NewTable(console);
            DealToYourMove(table);

            var move = Frame(console, table.Screen);
            AssertFills(move, width, height);
            Assert.Contains("♠ BLACKJACK", move);
            Assert.Contains("DEALER", move);
            Assert.Contains("▼ YOU", move);
            Assert.Contains("Bot 6", move);
            Assert.Contains("YOUR MOVE", move);
            Assert.Contains("Bust It 10", move);
            Assert.Contains("┏━", move);

            // The dealer's hole card is a chip, a tile or a tall tile; your seat gets tiles when wide.
            Assert.Equal(height >= 36 ? 5 : height >= 23 ? 3 : 0, Lines(move).Count(line => line.Contains("▒▒▒▒▒")));
            if (height < 23)
            {
                Assert.Contains("6♦ ▒▒  showing 6", move);
            }

            if (width >= 120)
            {
                Assert.Contains(Lines(move), line => line.StartsWith('║') && Regex.IsMatch(line, "♥ {5}♣"));
            }

            table.Game.Split();
            var split = Frame(console, table.Screen);
            AssertFills(split, width, height);
            Assert.Contains("HAND 1 OF 2", split);
            Assert.Contains("21+3 no hand -10", split);
        }

        [Theory]
        [InlineData(80, 28, 6, true)]
        [InlineData(89, 30, 6, true)]
        [InlineData(90, 30, 6, false)]
        [InlineData(100, 24, 6, false)]
        [InlineData(80, 24, 3, false)]
        [InlineData(80, 24, 6, false)] // the tiles come first: no room beside them for a second seat row
        [InlineData(60, 22, 6, true)]
        [InlineData(60, 21, 6, false)] // no row to spare for a second seat row
        public void Seats_WrapOntoTwoRows_OnANarrowTerminal(int width, int height, int bots, bool twoRows)
        {
            var console = NewConsole(width, height);
            var table = NewTable(console, bots);
            DealToYourMove(table);

            var frame = Frame(console, table.Screen);
            AssertFills(frame, width, height);
            var first = RowOf(frame, "Bot 1");
            var last = RowOf(frame, $"Bot {bots}");
            Assert.NotEqual(-1, first);
            Assert.Equal(twoRows, last > first);
            Assert.True(twoRows || first == last);
        }

        [Fact]
        public void Count_CollapsesToOneLine_OnANarrowTerminal()
        {
            var console = NewConsole(69, 24);
            var table = NewTable(console);
            DealToYourMove(table);

            var shown = Frame(console, table.Screen);
            AssertFills(shown, 69, 24);
            Assert.Contains("Hi-Lo running 0 · true 0.0 · 0.1 decks", shown);
            Assert.DoesNotContain("─Hi-Lo", shown);
            Assert.Equal(RowOf(shown, "^╚═") + 1, RowOf(shown, "^Hi-Lo running"));

            table.Screen.HandleToggle(Press(ConsoleKey.C));
            var hidden = Frame(console, table.Screen);
            Assert.Contains("Hi-Lo hidden · C show count", hidden);
            Assert.DoesNotContain("running", hidden);

            // One column more and the panel is back beside your seat.
            var wider = NewConsole(70, 24);
            var beside = NewTable(wider);
            DealToYourMove(beside);
            var panel = Frame(wider, beside.Screen);
            Assert.Contains("─Hi-Lo", panel);
            Assert.DoesNotContain("Hi-Lo running", panel);
            Assert.Equal(RowOf(panel, "^╔═"), RowOf(panel, "─Hi-Lo"));

            // Hidden, the panel says what the key does, like the bar.
            beside.Screen.HandleToggle(Press(ConsoleKey.C));
            var hiddenPanel = Frame(wider, beside.Screen);
            Assert.Contains("C show count", hiddenPanel);
            Assert.DoesNotContain("running", hiddenPanel);
        }

        [Theory]
        [InlineData(59, 20)]
        [InlineData(80, 15)]
        public void TooSmall_ShowsOnlyTheMessage_AndStillReadsKeys(int width, int height)
        {
            var console = NewConsole(width, height);
            var table = NewTable(console);
            DealToYourMove(table);

            var frame = Frame(console, table.Screen);
            AssertFills(frame, width, height);
            Assert.Contains("Terminal too small", frame);
            Assert.Contains($"Blackjack needs 60×16, this is {width}×{height}", frame);
            Assert.Contains("keys still work · X twice between hands cashes out", frame);
            Assert.Equal(RowOf(frame, "Terminal too small") + 2, RowOf(frame, "keys still work"));
            Assert.DoesNotContain("BLACKJACK ", frame);
            Assert.DoesNotContain("DEALER", frame);
            Assert.DoesNotContain("┏", frame);
            Assert.InRange(RowOf(frame, "Terminal too small"), height / 2 - 2, height / 2 + 1);

            console.Input.PushKey(ConsoleKey.X);
            Assert.Equal(ConsoleKey.X, table.Screen.ReadKey().Key);
        }

        [Fact]
        public void Resize_RedrawsAtTheNewSize_FromAClearedScreen()
        {
            var test = NewConsole(80, 24);
            var console = new ClearCountingConsole(test);
            var table = NewTable(console);
            DealToYourMove(table);

            var before = Frame(test, table.Screen);
            AssertFills(before, 80, 24);
            Assert.Equal(0, console.Clears);

            // The terminal grows while a key is awaited: the frame is rebuilt for the new size.
            test.Profile.Width = 100;
            test.Profile.Height = 30;
            test.Input.PushKey(ConsoleKey.S);
            var start = test.Output.Length;
            var key = table.Screen.ReadKey();
            var after = test.Output[start..];

            Assert.Equal(ConsoleKey.S, key.Key);
            Assert.Equal(1, console.Clears);
            AssertFills(after, 100, 30);
            Assert.Equal(RowOf(after, "Bot 1"), RowOf(after, "Bot 6"));

            // The same size again is drawn over the last frame, with no clear.
            test.Input.PushKey(ConsoleKey.S);
            table.Screen.ReadKey();
            Assert.Equal(1, console.Clears);

            // A wait between moves notices a resize too.
            test.Profile.Width = 80;
            test.Profile.Height = 24;
            start = test.Output.Length;
            table.Screen.Wait(TimeSpan.FromMilliseconds(1));

            Assert.Equal(2, console.Clears);
            AssertFills(test.Output[start..], 80, 24);
        }

        [Theory]
        [InlineData(60, 16)]
        [InlineData(80, 24)]
        [InlineData(100, 30)]
        [InlineData(120, 45)]
        public void Regions_StayPut_WhileTheTerminalDoes(int width, int height)
        {
            var console = NewConsole(width, height);
            var table = NewTable(console);
            table.Screen.PendingBet = 10;
            table.Screen.PendingSideBets = new long[] { 10, 10 };
            table.Screen.Log("a line");
            var betting = Frame(console, table.Screen);

            table.Screen.PendingBet = null;
            table.Screen.PendingSideBets = null;
            DealToYourMove(table);
            for (var i = 1; i <= 3; i++)
            {
                table.Screen.Log($"Bot {i} stands on 17.");
            }

            var move = Frame(console, table.Screen);
            table.Game.Split();
            table.Game.Hit();
            var split = Frame(console, table.Screen);

            Assert.Contains("place your bet", betting);
            Assert.Contains("YOUR MOVE", move);
            Assert.Contains("HAND 1 OF 2", split);
            foreach (var frame in new[] { move, split })
            {
                AssertFills(frame, width, height);
                Assert.Equal(RowOf(betting, "DEALER"), RowOf(frame, "DEALER"));
                Assert.Equal(RowOf(betting, "▼ YOU"), RowOf(frame, "▼ YOU"));
                Assert.Equal(RowOf(betting, "Bot 6"), RowOf(frame, "Bot 6"));
                Assert.Equal(RowOf(betting, "^╭─YOU"), RowOf(frame, "^╔═YOUR MOVE"));
                Assert.Equal(RowOf(betting, "Hi-Lo"), RowOf(frame, "Hi-Lo"));
                Assert.Equal(RowOf(betting, "^┏"), RowOf(frame, "^┏"));
            }
        }

        [Fact]
        public void EventLog_ShowsTheLastLinesThatFit_OneRowEach()
        {
            var console = NewConsole(100, 30);
            var table = NewTable(console);
            for (var i = 1; i <= 12; i++)
            {
                table.Screen.Log($"event {i}");
            }

            table.Screen.Log(new string('x', 150));
            var frame = Frame(console, table.Screen);

            // Nine rows: the long last line is cut to one, and the three oldest lines are gone.
            AssertFills(frame, 100, 30);
            Assert.Contains("event 12", frame);
            Assert.Contains("event 5", frame);
            Assert.DoesNotContain("event 4", frame);
            Assert.Contains("› " + new string('x', 97) + "…", frame);
            Assert.Equal(RowOf(frame, "^┏") - 9, RowOf(frame, "event 5"));
        }

        [Fact]
        public void SeatLines_GiveUpTheTagRow_OnlyOnTheShortestTerminal()
        {
            Assert.Equal(3, new TableLayout(110, 16, 7, 4).SeatLines);
            Assert.Equal(4, new TableLayout(110, 17, 7, 4).SeatLines);
            Assert.Equal(3, new TableLayout(60, 17, 7, 4).SeatLines); // the count line takes a row too
            Assert.Equal(4, new TableLayout(60, 18, 7, 4).SeatLines);
        }

        [Theory]
        [InlineData(60, 16, 7, 0, 1, false, 5, false, 1)]
        [InlineData(60, 20, 7, 0, 2, true, 5, false, 1)]
        [InlineData(69, 36, 1, 5, 7, false, 9, false, 11)]
        [InlineData(80, 23, 7, 3, 5, false, 7, true, 3)] // the tiles come first, so the seats stay on one row
        [InlineData(80, 24, 7, 3, 5, true, 7, true, 1)]
        [InlineData(89, 36, 5, 5, 7, true, 9, true, 9)]
        [InlineData(100, 30, 3, 3, 5, false, 7, true, 10)]
        [InlineData(120, 45, 7, 5, 7, false, 9, true, 21)]
        public void TableLayout_SharesOutTheRows(int width, int height, int seats, int tile, int dealer, bool twoRows, int panel, bool countPanel, int events)
        {
            var layout = new TableLayout(width, height, seats, 3);

            Assert.False(layout.TooSmall);
            Assert.Equal(tile, layout.TileHeight);
            Assert.Equal(dealer, layout.DealerRows);
            Assert.Equal(twoRows, layout.TwoSeatRows);
            Assert.Equal(panel, layout.PanelRows);
            Assert.Equal(countPanel, layout.CountPanel);
            Assert.Equal(events, layout.EventRows);
            Assert.Equal(height, TableLayout.HeaderRows + layout.DealerRows + layout.SeatRows * 3 + layout.YouRows + layout.EventRows + TableLayout.BarRows);
            Assert.Equal(countPanel ? width - 26 : width, layout.PanelWidth);
        }

        [Fact]
        public void TableLayout_KnowsWhenTheTerminalIsTooSmall()
        {
            Assert.True(new TableLayout(59, 16, 1, 3).TooSmall);
            Assert.True(new TableLayout(60, 15, 1, 3).TooSmall);
            Assert.False(new TableLayout(60, 16, 1, 3).TooSmall);
        }

        [Fact]
        public void TallTile_PutsABlankLineEitherSideOfTheSuit()
        {
            var tile = CardArt.Tile(C("10h"), CardArt.TallTileHeight);

            Assert.Equal(new[] { "10   ", "     ", "  ♥  ", "     ", "   10" }, tile.Select(Markup.Remove));
            Assert.Equal(new[] { "A    ", "  ♠  ", "    A" }, CardArt.Tile(C("As")).Select(Markup.Remove));
            Assert.Equal(5, CardArt.Back(5).Length);
            Assert.Equal(5, CardArt.Row(new[] { tile, CardArt.Back(5) }).Split('\n').Length);
        }

        [Fact]
        public void LogChip_IsBoldTextInTheSuitColour_NotATile()
        {
            Assert.Equal("[bold red]10♥[/]", CardArt.LogChip(C("10h")));
            Assert.Equal("[bold white]A♠[/]", CardArt.LogChip(C("As")));
            Assert.Equal("[black on white]A♠[/]", CardArt.Chip(C("As")));
        }

        [Fact]
        public void Line_CutsALongLineShort_InsteadOfWrapping()
        {
            var console = NewConsole(10, 3);
            console.Write(new Rows(new Line("[red]abc[/] def ghi jkl"), new Line("short", Justify.Center)));

            Assert.Equal(new[] { "abc def g…", "  short" }, console.Lines.Select(line => line.TrimEnd()));
        }

        // Counts the screen clears instead of writing them to the output as escape codes.
        private sealed class ClearCountingConsole : IAnsiConsole
        {
            private readonly TestConsole inner;

            public ClearCountingConsole(TestConsole inner)
            {
                this.inner = inner;
            }

            public int Clears { get; private set; }
            public Profile Profile => inner.Profile;
            public IAnsiConsoleCursor Cursor => inner.Cursor;
            public IAnsiConsoleInput Input => inner.Input;
            public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;
            public RenderPipeline Pipeline => inner.Pipeline;

            public void Clear(bool home) => Clears++;

            public void Write(IRenderable renderable) => inner.Write(renderable);

            public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);
        }
    }
}
