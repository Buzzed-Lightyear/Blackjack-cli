using System.Text.RegularExpressions;
using blackjack.Cli;
using blackjack.Domain;
using blackjack.Engine;
using blackjack.Engine.SideBets;
using blackjack.Strategies;
using Spectre.Console;
using Spectre.Console.Testing;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    // Clicking the key hints in the bar: the terminal's mouse reports are decoded, matched against
    // where the last frame drew the hints, and turned into the keys the hints name.
    public class MouseTests
    {
        private const string FrameStart = "♠ BLACKJACK";

        private static TestConsole NewConsole(int width = 110, int height = 30) =>
            new TestConsole().Width(width).Height(height).Interactive();

        private static KeyChord Chord(string name) =>
            KeyChord.TryParse(name, out var chord) ? chord : throw new ArgumentException(name);

        // What a terminal sends for the left button going down (or up) on a 0-based cell.
        private static string Report(int column, int row, bool release = false) =>
            $"\u001b[<0;{column + 1};{row + 1}{(release ? 'm' : 'M')}";

        // A click as a terminal reports it: the press, then the release.
        private static void PushClick(TestConsole console, int column, int row)
        {
            console.Input.PushText(Report(column, row));
            console.Input.PushText(Report(column, row, release: true));
        }

        private static ConsoleKeyInfo First(TestConsole console) =>
            console.Input.ReadKey(intercept: true) ?? throw new InvalidOperationException();

        // The column where a word of its own starts in a rendered line: "S" in "D hit · S stand".
        private static int ColumnOf(string line, string word)
        {
            var match = Regex.Match(line, $"(?<=^|[ /]){Regex.Escape(word)}(?=$|[ /])");
            Assert.True(match.Success, $"\"{word}\" is not in: {line}");
            return match.Index;
        }

        private static string[] LastFrame(TestConsole console, int height) =>
            console.Output.TrimEnd('\n').Split('\n')[^height..];

        // The frame a line matching the pattern was last drawn in.
        private static string[] FrameWith(string output, string pattern, int height)
        {
            var lines = output.Split('\n');
            var at = Array.FindLastIndex(lines, line => Regex.IsMatch(line, pattern));
            var start = Array.FindLastIndex(lines, at, line => line.Contains(FrameStart));
            Assert.True(start >= 0, $"No frame holds /{pattern}/");
            return lines[start..(start + height)];
        }

        // A seat for the human at a one-player table, with the screen it reads keys through.
        private static (ConsoleStrategy Strategy, TableScreen Screen, FastForward Autopilot) NewSeat(TestConsole console)
        {
            var keys = KeyBindings.Defaults();
            var autopilot = new FastForward();
            var stats = new SessionStats(100m);
            var screen = new TableScreen(console, keys, autopilot);
            var sideBets = new[] { SideBets.All[0], SideBets.All[1] };
            var strategy = new ConsoleStrategy(screen, keys, autopilot, stats, new Rules(), sideBets);
            var human = new Player("Tester", 100m, strategy);
            var game = new Game(new Rules(), new[] { human }, Shoe.Stacked(Of("10h 9c 6d 7s")), sideBets);
            screen.Seat(game, human, new CardCounter(), stats, 1);
            return (strategy, screen, autopilot);
        }

        private static void PushSetup(TestConsole console)
        {
            console.Input.PushTextWithEnter("Tester");
            console.Input.PushKey(ConsoleKey.Enter);  // keep the default rules
            console.Input.PushKey(ConsoleKey.Enter);  // 1000 chips
            console.Input.PushTextWithEnter("0");     // no bots
            console.Input.PushKey(ConsoleKey.Enter);  // keep 21+3 and Bust It
            console.Input.PushKey(ConsoleKey.Enter);  // keep the keys
        }

        // The first seed whose first deal, alone against the dealer, leaves you a move to make (no
        // natural on either side and no ace up) and loses if you stand.
        private static int SeedThatLosesOnAStand()
        {
            for (var seed = 1; seed < 10_000; seed++)
            {
                var rules = new Rules();
                var human = new Player("P", 1000m, new BasicStrategyBot(rules));
                var game = new Game(rules, new[] { human }, new Shoe(rules.DeckCount, seed, rules.Penetration));
                game.PlaceBet(human, rules.MinimumBet);
                game.Deal();
                if (game.Phase != Phase.PlayerTurn)
                {
                    continue;
                }

                game.Stand();
                if (human.Hands[0].Outcome == Outcome.Lose)
                {
                    return seed;
                }
            }

            throw new InvalidOperationException("No seed deals that round.");
        }

        [Theory]
        [InlineData("\u001b[<0;11;29M", 10, 28, true)]
        [InlineData("\u001b[<0;11;29m", 10, 28, false)]  // released
        [InlineData("\u001b[<4;1;1M", 0, 0, true)]        // with Shift held
        [InlineData("\u001b[<32;11;29M", 10, 28, false)] // dragged
        [InlineData("\u001b[<64;11;29M", 10, 28, false)] // wheel up
        [InlineData("\u001b[<65;11;29M", 10, 28, false)] // wheel down
        [InlineData("\u001b[<2;200;60M", 199, 59, false)] // right button
        public void Decoder_ReadsAReport_ToItsEnd(string report, int column, int row, bool pressed)
        {
            var console = NewConsole();
            console.Input.PushText(report);
            console.Input.PushKey(ConsoleKey.S);
            var taken = new List<ConsoleKeyInfo>();

            var click = MouseInput.TryDecode(First(console), console.Input, taken);

            Assert.Equal(new MouseClick(column, row, pressed), click);
            Assert.Equal(report.Length - 1, taken.Count);
            Assert.Equal(ConsoleKey.S, First(console).Key); // the key after the report is untouched
        }

        [Fact]
        public void Decoder_LeavesALoneEscapeAlone()
        {
            var console = NewConsole();
            console.Input.PushKey(ConsoleKey.Escape);
            var first = First(console);
            var taken = new List<ConsoleKeyInfo>();

            Assert.Null(MouseInput.TryDecode(first, console.Input, taken));
            Assert.Empty(taken);
            Assert.Equal(ConsoleKey.Escape, first.Key);
            Assert.Null(MouseInput.TryDecode(new ConsoleKeyInfo('D', ConsoleKey.D, false, false, false), console.Input, taken));
        }

        [Fact]
        public void Decoder_HandsBackTheKeysOfWhatIsNotAReport()
        {
            // Escape then a digit, as when a typed bet is cleared and typed again.
            var console = NewConsole();
            console.Input.PushKey(ConsoleKey.Escape);
            console.Input.PushKey(ConsoleKey.D4);
            var taken = new List<ConsoleKeyInfo>();

            Assert.Null(MouseInput.TryDecode(First(console), console.Input, taken));
            Assert.Equal(new[] { ConsoleKey.D4 }, taken.Select(k => k.Key));

            // A sequence that starts like a report and isn't one.
            console.Input.PushText("\u001b[<0;3x");
            taken.Clear();

            Assert.Null(MouseInput.TryDecode(First(console), console.Input, taken));
            Assert.Equal("[<0;3x", string.Concat(taken.Select(k => k.KeyChar)));

            // A report cut short.
            console.Input.PushText("\u001b[<0;3;");
            taken.Clear();

            Assert.Null(MouseInput.TryDecode(First(console), console.Input, taken));
            Assert.Equal("[<0;3;", string.Concat(taken.Select(k => k.KeyChar)));
        }

        [Fact]
        public void Hints_CoverTheKeyAndItsWords_OnTheBettingBar()
        {
            var console = NewConsole(100, 30);
            var (strategy, screen, _) = NewSeat(console);
            console.Input.PushKey(ConsoleKey.Spacebar);
            strategy.DecideBet(100m, Tables.Fresh());

            var frame = LastFrame(console, 30);
            var line1 = frame[27];
            var line2 = frame[28];
            Assert.StartsWith("┏", frame[26]);
            Assert.Contains("PLACE YOUR BET", line1);
            Assert.Equal(2, ColumnOf(line2, "F"));

            // The key, and the words that go with it.
            Assert.Equal(Chord("Space"), screen.HintAt(ColumnOf(line1, "Space"), 27));
            Assert.Equal(Chord("Space"), screen.HintAt(ColumnOf(line1, "deal"), 27));
            Assert.Equal(Chord("Space"), screen.HintAt(ColumnOf(line1, "deal") + 3, 27));
            Assert.Equal(Chord("X"), screen.HintAt(ColumnOf(line2, "X"), 28));
            Assert.Equal(Chord("X"), screen.HintAt(ColumnOf(line2, "cash"), 28));
            Assert.Equal(Chord("F"), screen.HintAt(ColumnOf(line2, "F"), 28));
            Assert.Equal(Chord("R"), screen.HintAt(ColumnOf(line2, "play"), 28));
            Assert.Equal(Chord("R"), screen.HintAt(ColumnOf(line2, "shoe"), 28));
            Assert.Equal(Chord("B"), screen.HintAt(ColumnOf(line2, "book"), 28));
            Assert.Equal(Chord("C"), screen.HintAt(ColumnOf(line2, "C"), 28));

            // Either side of a slash the labels are a target each, and the slash and the words
            // after them belong to neither.
            Assert.Equal(Chord("Q"), screen.HintAt(ColumnOf(line1, "Q"), 27));
            Assert.Equal(Chord("E"), screen.HintAt(ColumnOf(line1, "E"), 27));
            Assert.Null(screen.HintAt(ColumnOf(line1, "Q") + 1, 27));
            Assert.Null(screen.HintAt(ColumnOf(line1, "type"), 27));
            Assert.Equal(Chord("T"), screen.HintAt(ColumnOf(line1, "T"), 27));
            Assert.Equal(Chord("G"), screen.HintAt(ColumnOf(line1, "G"), 27));
            Assert.Null(screen.HintAt(ColumnOf(line1, "side"), 27));

            // Nothing in the gaps, the words that aren't hints, the border and the padding, or
            // on the other rows.
            Assert.Null(screen.HintAt(line2.IndexOf(" · ", StringComparison.Ordinal) + 1, 28));
            Assert.Null(screen.HintAt(ColumnOf(line1, "PLACE"), 27));
            Assert.Null(screen.HintAt(ColumnOf(line1, "10"), 27));
            Assert.Null(screen.HintAt(0, 28));
            Assert.Null(screen.HintAt(1, 28));
            Assert.Null(screen.HintAt(99, 28));
            Assert.Null(screen.HintAt(ColumnOf(line2, "X"), 26));
            Assert.Null(screen.HintAt(ColumnOf(line2, "X"), 29));
            Assert.Null(screen.HintAt(ColumnOf(line2, "X"), 10));
            Assert.Null(screen.HintAt(ColumnOf(line2, "F"), 27)); // the P of PLACE
        }

        [Fact]
        public void Hints_FollowTheBar_FromMoveToMove()
        {
            var console = NewConsole(100, 30);
            var (strategy, screen, _) = NewSeat(console);
            console.Input.PushKey(ConsoleKey.S);
            strategy.DecideMove(HandOf("10h 6c"), C("10s"), new MoveOptions(true, false), Tables.Fresh());

            var frame = LastFrame(console, 30);
            var line1 = frame[27];
            var line2 = frame[28];
            Assert.Contains("YOUR MOVE", line1);
            Assert.Equal(2, ColumnOf(line2, "D"));

            // The book's advice is set in capitals: "D HIT".
            Assert.Equal(Chord("D"), screen.HintAt(ColumnOf(line2, "D"), 28));
            Assert.Equal(Chord("D"), screen.HintAt(ColumnOf(line2, "HIT"), 28));
            Assert.Equal(Chord("S"), screen.HintAt(ColumnOf(line2, "S"), 28));
            Assert.Equal(Chord("S"), screen.HintAt(ColumnOf(line2, "stand"), 28));
            Assert.Equal(Chord("A"), screen.HintAt(ColumnOf(line2, "double"), 28));
            Assert.Equal(Chord("B"), screen.HintAt(ColumnOf(line2, "B"), 28));

            // The first line talks about the hand; "a" and the book's "hit" are words, not keys.
            Assert.Contains("book: hit", line1);
            Assert.Null(screen.HintAt(ColumnOf(line1, "a"), 27));
            Assert.Null(screen.HintAt(ColumnOf(line1, "hit"), 27));
            Assert.Null(screen.HintAt(ColumnOf(line1, "MOVE"), 27));

            // Once the move is made and the bar cleared, only the toggles are left to click.
            screen.Refresh();
            var after = LastFrame(console, 30)[28];
            Assert.DoesNotContain("stand", after);
            Assert.Equal(Chord("B"), screen.HintAt(ColumnOf(after, "book"), 28));
            Assert.Equal(Chord("C"), screen.HintAt(ColumnOf(after, "count"), 28));
            Assert.Null(screen.HintAt(after.IndexOf(" · ", StringComparison.Ordinal) + 1, 28));
            Assert.DoesNotContain(Chord("S"), Enumerable.Range(0, 100).Select(column => screen.HintAt(column, 28)));
            Assert.All(Enumerable.Range(0, 100), column => Assert.Null(screen.HintAt(column, 27)));
        }

        [Fact]
        public void Hints_StopAtTheEllipsis_OnANarrowTerminal()
        {
            var console = NewConsole(70, 30);
            var (_, screen, _) = NewSeat(console);
            var filler = $"[grey]{new string('x', 56)}[/]";
            screen.SetBar($"{filler} · [bold]Space[/] deal", "[bold]X[/] cash out", BarTone.Input);
            screen.Refresh();

            // 66 cells fit the panel: the line is cut to 65 and an ellipsis, right after "Space".
            var frame = LastFrame(console, 30);
            var line1 = frame[27];
            Assert.Equal(67, line1.IndexOf('…'));
            Assert.Equal(Chord("Space"), screen.HintAt(ColumnOf(line1, "Space"), 27));
            Assert.Equal(Chord("Space"), screen.HintAt(66, 27));
            Assert.Null(screen.HintAt(67, 27));
            Assert.Null(screen.HintAt(68, 27));
            Assert.Equal(Chord("X"), screen.HintAt(ColumnOf(frame[28], "cash"), 28));

            // Two columns further on the label itself is cut, so it is not a target at all.
            screen.SetBar($"{filler}xx · [bold]Space[/] deal", "", BarTone.Input);
            screen.Refresh();

            line1 = LastFrame(console, 30)[27];
            Assert.Contains("Spac…", line1);
            Assert.Null(screen.HintAt(line1.IndexOf("Spac", StringComparison.Ordinal), 27));
            Assert.Null(screen.HintAt(66, 27));
        }

        [Fact]
        public void Hints_AreGoneWhenTheTerminalIsTooSmall()
        {
            var console = NewConsole(100, 30);
            var (_, screen, _) = NewSeat(console);
            screen.SetBar("[bold]S[/] stand", "[bold]B[/] book", BarTone.Input);
            screen.Refresh();
            Assert.Equal(Chord("S"), screen.HintAt(2, 27));

            console.Profile.Height = 15;
            screen.Refresh();

            Assert.Null(screen.HintAt(2, 12));
            Assert.Null(screen.HintAt(2, 27));
        }

        [Fact]
        public void Screen_TurnsAClickOnAHintIntoItsKey_AndSkipsOtherMouseEvents()
        {
            var console = NewConsole(100, 30);
            var (_, screen, _) = NewSeat(console);
            screen.SetBar("[bold]D[/] hit · [bold]S[/] stand", "[bold]B[/] book on", BarTone.Input);
            PushClick(console, 50, 10);          // the table: nothing
            PushClick(console, 2, 26);           // the bar's border: nothing
            console.Input.PushText(Report(10, 27, release: true)); // a release on "S stand": nothing
            PushClick(console, 12, 27);          // "stand"
            PushClick(console, 2, 28);           // "B"
            console.Input.PushKey(ConsoleKey.Escape);
            console.Input.PushKey(ConsoleKey.D4);

            Assert.Equal(Chord("S").ToKeyInfo(), screen.ReadKey());
            Assert.Equal(Chord("B").ToKeyInfo(), screen.ReadKey());
            Assert.Equal(ConsoleKey.Escape, screen.ReadKey().Key); // a lone Escape stays one
            Assert.Equal(ConsoleKey.D4, screen.ReadKey().Key);     // and the key after it is kept
        }

        [Fact]
        public void Wait_TakesClicksOnTheToggles_AndDropsTheRest()
        {
            var console = NewConsole(100, 30);
            var (_, screen, autopilot) = NewSeat(console);
            screen.Refresh();
            var line2 = LastFrame(console, 30)[28];
            PushClick(console, ColumnOf(line2, "count"), 28);
            PushClick(console, 50, 10);

            screen.Wait(TimeSpan.FromMilliseconds(1));

            Assert.False(screen.ShowCount);
            Assert.False(console.Input.IsKeyAvailable());

            // On autopilot a click on the table doesn't count as the key that stops it.
            autopilot.Watch();
            screen.Refresh();
            PushClick(console, 50, 10);
            screen.Wait(TimeSpan.FromMilliseconds(1));
            Assert.False(autopilot.StopRequested);

            line2 = LastFrame(console, 30)[28];
            PushClick(console, ColumnOf(line2, "faster"), 28);
            screen.Wait(TimeSpan.FromMilliseconds(1));
            Assert.Equal(FastForwardSpeed.Max, autopilot.Speed);
            Assert.False(autopilot.StopRequested);
        }

        [Fact]
        public void App_PlaysARoundByClickingTheHints()
        {
            // At 110 columns the betting bar reads "PLACE YOUR BET  ◀ 10 ▶  Space deal · Q/E -/+10
            // or type it · T/G side bets" over "F autopilot · R play out shoe · X cash out · …", the
            // move bar "D hit · S stand · …", and cashing out after the lost 20 "Cash out with 980
            // chips?  X again to confirm", each from column 2 of rows 27 and 28.
            var seed = SeedThatLosesOnAStand();
            var console = NewConsole();
            PushSetup(console);
            PushClick(console, 2 + 39, 27);  // E: bet 20
            PushClick(console, 2 + 30, 27);  // the "deal" of Space deal
            PushClick(console, 2 + 8, 28);   // S stand
            PushClick(console, 2 + 32, 28);  // X cash out
            PushClick(console, 2 + 26, 27);  // X again to confirm

            new BlackjackApp(console, TimeSpan.Zero, seed).Run();

            var output = console.Output;
            var betting = FrameWith(output, "PLACE YOUR BET", 30);
            var move = FrameWith(output, "YOUR MOVE", 30);
            var cashOut = FrameWith(output, "Cash out with", 30);
            Assert.Equal("E", betting[27].Substring(41, 1));
            Assert.Equal("Space deal", betting[27].Substring(26, 10));
            Assert.Equal("S stand", move[28].Substring(10, 7));
            Assert.Equal("X cash out", betting[28].Substring(34, 10));
            Assert.Equal("X again", cashOut[27].Substring(28, 7));
            Assert.Contains("next bet 20", output);
            Assert.Contains(move, line => Regex.IsMatch(line, @"(?<!next )bet 20\b"));
            Assert.Matches(@"You stand on \d+\.", output);
            Assert.Contains("You lose 20.", output);
            Assert.Contains("Cash out with 980 chips?", output);
            Assert.Contains("Thanks for playing, Tester!", output);
            Assert.DoesNotContain("\u001b[?1000h", output); // no ANSI: the terminal was never asked to report
        }

        [Fact]
        public void App_IgnoresClicksOffTheHints_AndStillTakesTheKeys()
        {
            var seed = SeedThatLosesOnAStand();
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.Spacebar);
            PushClick(console, 40, 5);        // the dealer's cards
            PushClick(console, 2, 26);        // the bar's top border
            PushClick(console, 2 + 6, 28);    // the gap between "D hit" and "S stand"
            console.Input.PushKey(ConsoleKey.S);
            PushClick(console, 50, 15);
            console.Input.PushKey(ConsoleKey.X);
            console.Input.PushKey(ConsoleKey.X);

            new BlackjackApp(console, TimeSpan.Zero, seed).Run();

            Assert.Matches(@"You stand on \d+\.", console.Output);
            Assert.Contains("Thanks for playing, Tester!", console.Output);
        }

        [Fact]
        public void App_CashesOutOnTwoClicksAtX()
        {
            var console = NewConsole();
            PushSetup(console);
            PushClick(console, 2 + 32, 28);  // X cash out
            PushClick(console, 2 + 27, 27);  // X again to confirm

            new BlackjackApp(console, TimeSpan.Zero, 1).Run();

            var cashOut = FrameWith(console.Output, "Cash out with", 30);
            Assert.Equal("X again", cashOut[27].Substring(29, 7));
            Assert.Contains("Cash out with 1000 chips?", console.Output);
            Assert.Contains("Thanks for playing, Tester!", console.Output);
        }

        [Fact]
        public void App_AsksTheTerminalToReportTheMouse_ForTheTableOnly()
        {
            var console = new TestConsole().Width(110).Height(30).Interactive().EmitAnsiSequences();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.X);
            console.Input.PushKey(ConsoleKey.X);

            new BlackjackApp(console, TimeSpan.Zero, 1).Run();

            var output = console.Output;
            var on = output.IndexOf("\u001b[?1000h\u001b[?1006h", StringComparison.Ordinal);
            var off = output.IndexOf("\u001b[?1006l\u001b[?1000l", StringComparison.Ordinal);
            Assert.True(on >= 0 && off > on);
            Assert.Equal(on, output.LastIndexOf("\u001b[?1000h", StringComparison.Ordinal));
            Assert.Equal(off, output.LastIndexOf("\u001b[?1006l", StringComparison.Ordinal));
            Assert.InRange(output.IndexOf(FrameStart, StringComparison.Ordinal), on, off);
            Assert.InRange(output.LastIndexOf(FrameStart, StringComparison.Ordinal), on, off);
            Assert.True(output.IndexOf("Cashing out", StringComparison.Ordinal) > off);
            Assert.True(output.IndexOf("Your name?", StringComparison.Ordinal) < on);
        }

        [Fact]
        public void App_LeavesTheMouseAlone_WithTheFlagOff()
        {
            var console = new TestConsole().Width(110).Height(30).Interactive().EmitAnsiSequences();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.X);
            console.Input.PushKey(ConsoleKey.X);

            new BlackjackApp(console, TimeSpan.Zero, 1, mouse: false).Run();

            Assert.DoesNotContain("\u001b[?1000", console.Output);
            Assert.DoesNotContain("\u001b[?1006", console.Output);
            Assert.Contains("Thanks for playing, Tester!", console.Output);
        }

        [Fact]
        public void App_StopsTheReportsEvenWhenTheGameThrows()
        {
            var console = new TestConsole().Width(110).Height(30).Interactive().EmitAnsiSequences();
            PushSetup(console);  // then no keys at all: the first bet throws

            Assert.Throws<InvalidOperationException>(() => new BlackjackApp(console, TimeSpan.Zero, 1).Run());

            Assert.Contains("\u001b[?1000h\u001b[?1006h", console.Output);
            Assert.EndsWith("\u001b[?1006l\u001b[?1000l", console.Output);
        }
    }
}
