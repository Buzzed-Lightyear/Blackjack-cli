using System.Text.RegularExpressions;
using blackjack.Cli;
using blackjack.Domain;
using blackjack.Engine;
using blackjack.Strategies;
using Spectre.Console;
using Spectre.Console.Testing;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    // Drives the console layer through Spectre's TestConsole: markup mistakes only show up at runtime.
    // Inside the live table every frame is appended to the output, so assertions look at the last one.
    public class CliTests
    {
        private const string FrameStart = "♠ BLACKJACK";

        private static TestConsole NewConsole(int width = 110, int height = 30) =>
            new TestConsole().Width(width).Height(height).Interactive();

        private sealed class AlwaysStand : IPlayerStrategy
        {
            public decimal DecideBet(decimal chips, decimal minBet) => minBet;

            public Move DecideMove(Hand hand, Card dealerUpcard) => Move.Stand;
        }

        private sealed record FirstRound(Game Game, Hand Human, Move FirstAdvice, bool ShuffledAtTheEnd);

        // Plays the first round from this seed the way BlackjackApp seats and deals it, with the
        // human always standing at the minimum and the bots on basic strategy.
        private static FirstRound PlayFirstRound(Rules rules, int seed, int bots)
        {
            var human = new Player("P", 1000m, new AlwaysStand());
            var seats = Enumerable.Range(1, bots)
                .Select(i => new Player($"Bot {i}", 1000m, new BasicStrategyBot()))
                .ToList();
            seats.Insert(seats.Count / 2, human);

            var game = new Game(rules, seats, new Shoe(rules.DeckCount, seed, rules.Penetration));
            var shuffledAtTheEnd = false;
            game.Shuffled += () => shuffledAtTheEnd = game.Phase == Phase.Settled;

            foreach (var player in seats)
            {
                game.PlaceBet(player, rules.MinimumBet);
            }

            game.Deal();
            var advice = new BasicStrategyBot().DecideMove(human.Hands[0], game.DealerUpcard!);
            while (game.Phase == Phase.PlayerTurn)
            {
                var move = game.CurrentPlayer!.Strategy.DecideMove(game.CurrentHand!, game.DealerUpcard!);
                if (move == Move.Hit)
                {
                    game.Hit();
                }
                else
                {
                    game.Stand();
                }
            }

            return new FirstRound(game, human.Hands[0], advice, shuffledAtTheEnd);
        }

        private static int FirstSeedWhere(int bots, Func<FirstRound, bool> wanted) =>
            FirstSeedWhere(new Rules(), bots, wanted);

        private static int FirstSeedWhere(Rules rules, int bots, Func<FirstRound, bool> wanted)
        {
            for (var seed = 1; seed < 10_000; seed++)
            {
                if (wanted(PlayFirstRound(rules, seed, bots)))
                {
                    return seed;
                }
            }

            throw new InvalidOperationException("No seed deals that round.");
        }

        // The player gets a decision: no natural on either side.
        private static bool NeedsAMove(FirstRound round) => !round.Human.IsNatural && !round.Game.DealerHand.IsNatural;

        private static void PushSetup(TestConsole console, string bots = "0", string? chips = null)
        {
            console.Input.PushTextWithEnter("Tester");
            console.Input.PushKey(ConsoleKey.Enter);      // keep the default rules
            if (chips is null)
            {
                console.Input.PushKey(ConsoleKey.Enter);  // 1000 chips
            }
            else
            {
                console.Input.PushTextWithEnter(chips);
            }

            console.Input.PushTextWithEnter(bots);
            console.Input.PushKey(ConsoleKey.Enter);      // keep the keys
        }

        private static void PushCashOut(TestConsole console)
        {
            console.Input.PushKey(ConsoleKey.X);
            console.Input.PushKey(ConsoleKey.X);
        }

        private static string LastFrame(string output) => output[output.LastIndexOf(FrameStart, StringComparison.Ordinal)..];

        private static void AssertInOrder(string text, params string[] patterns)
        {
            var at = 0;
            foreach (var pattern in patterns)
            {
                var match = new Regex(pattern).Match(text, at);
                Assert.True(match.Success, $"Expected /{pattern}/ after position {at} in:\n{text}");
                at = match.Index + match.Length;
            }
        }

        [Fact]
        public void App_PlaysARound_ThenCashesOut()
        {
            var seed = FirstSeedWhere(bots: 0, NeedsAMove);
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.Spacebar);  // deal at the minimum
            console.Input.PushKey(ConsoleKey.S);         // stand
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, seed).Run();

            var output = console.Output;
            Assert.Contains("PLACE YOUR BET", output);
            Assert.Contains("YOUR MOVE", output);
            Assert.Contains("book: ", output);
            Assert.Contains("You stand on", output);
            Assert.Contains("Cash out with", output);
            Assert.Contains("Hands you played", output);
            Assert.Contains("Thanks for playing, Tester!", output);
        }

        [Fact]
        public void App_EndsWhenThePlayerIsBroke()
        {
            // Losing the first round with only the minimum bet in chips ends the session.
            var seed = FirstSeedWhere(bots: 2, round => round.Human.Outcome == Outcome.Lose);
            var console = NewConsole();
            PushSetup(console, bots: "2", chips: "10");
            console.Input.PushKey(ConsoleKey.Spacebar);  // bet the 10
            console.Input.PushKey(ConsoleKey.S);         // stand (unread if the dealer has blackjack)
            console.Input.PushKey(ConsoleKey.Z);         // on to the summary

            new BlackjackApp(console, TimeSpan.Zero, seed).Run();

            Assert.Contains("You don't have enough chips", console.Output);
            Assert.Contains("Thanks for playing, Tester!", console.Output);
        }

        [Fact]
        public void App_DropsKeysPressedWhileTheTableAnimates()
        {
            var seed = FirstSeedWhere(bots: 0, NeedsAMove);
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.Spacebar);  // deal
            console.Input.PushKey(ConsoleKey.S);         // typed ahead, before the cards come out
            var app = new BlackjackApp(console, TimeSpan.FromMilliseconds(4), seed);

            // The early S is thrown away during the deal animation, so the move is left waiting for a key.
            var error = Assert.Throws<InvalidOperationException>(app.Run);
            Assert.Contains("No input available", error.Message);
            Assert.DoesNotContain("You stand on", console.Output);
        }

        [Fact]
        public void App_CallsYourBlackjackBeforeTheDealerTurnsOver()
        {
            var seed = FirstSeedWhere(bots: 0, round => round.Human.IsNatural && !round.Game.DealerHand.IsNatural);
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.Spacebar);
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, seed).Run();

            AssertInOrder(LastFrame(console.Output), "You have blackjack!", "Dealer turns over", @"Blackjack pays! You win 15\.");
            Assert.DoesNotContain("Tester has blackjack", console.Output);
        }

        [Fact]
        public void App_AnnouncesTheCutCardShuffleAfterTheResults()
        {
            // One deck cut half way: a full table can reach the cut card in the first round.
            var rules = new Rules { DeckCount = 1, Penetration = 0.5m };
            var seed = FirstSeedWhere(rules, bots: 6, round => round.ShuffledAtTheEnd && NeedsAMove(round));
            var console = NewConsole();
            console.Input.PushTextWithEnter("Tester");
            console.Input.PushTextWithEnter("y");     // change the rules
            console.Input.PushTextWithEnter("1");     // one deck
            console.Input.PushKey(ConsoleKey.Enter);  // minimum bet 10
            console.Input.PushKey(ConsoleKey.Enter);  // dealer stands on soft 17
            console.Input.PushKey(ConsoleKey.Enter);  // 3:2
            console.Input.PushTextWithEnter("50");    // penetration
            console.Input.PushKey(ConsoleKey.Enter);  // keep the seed
            console.Input.PushKey(ConsoleKey.Enter);  // 1000 chips
            console.Input.PushTextWithEnter("6");     // bots
            console.Input.PushKey(ConsoleKey.Enter);  // keep the keys
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.S);
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, seed).Run();

            // The count already shows the fresh shoe, and the shuffle note follows the results.
            var frame = LastFrame(console.Output);
            Assert.Matches(@"running\s+0\b", frame);
            AssertInOrder(frame, "Dealer (has|busts)", "You (win|lose)|Push", @"Cut card reached: shuffling shoe 2\.");
        }

        [Fact]
        public void App_PointsOutAMoveTheBookDisagreesWith()
        {
            var seed = FirstSeedWhere(bots: 0, round => NeedsAMove(round) && round.FirstAdvice == Move.Hit);
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.S);         // stand where the book hits
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, seed).Run();

            Assert.Contains("The book says hit on", console.Output);
            Assert.Matches(@"Followed the book\s+│\s+0 of 1 moves", console.Output);
        }

        [Fact]
        public void App_Autopilot_PlaysYourSeatUntilAKeyStopsIt()
        {
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.F);         // autopilot
            console.Input.PushKey(ConsoleKey.Z);         // any key: stop after this hand
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, 1).Run();

            var output = console.Output;
            Assert.Contains("AUTOPILOT", output);
            Assert.Contains("Autopilot off after 1 hand", output);
            Assert.Matches(@"Hands on autopilot\s+│\s+1\b", output);
            Assert.Matches(@"Hands you played\s+│\s+0\b", output);
        }

        [Fact]
        public void App_SkipsToTheNextShoe()
        {
            // Keys only reach questions here, so nothing stops the skip early.
            var test = NewConsole();
            var console = new ScriptedConsole(test);
            PushSetup(test, bots: "2");
            test.Input.PushKey(ConsoleKey.R);            // skip to the next shoe
            PushCashOut(test);

            new BlackjackApp(console, TimeSpan.Zero, 3).Run();

            var frame = LastFrame(console.Output);
            Assert.Contains("Fresh shoe. The autopilot played", frame);
            Assert.Matches(@"running\s+0\b", frame);
            Assert.Matches(@"decks\s+6\.0 left", frame);
            Assert.Contains("SKIPPING TO THE NEXT SHOE", console.Output);
        }

        [Fact]
        public void App_ShowsTheSeedAndShoe_AndTheCommandThatReplaysThem()
        {
            var console = NewConsole();
            PushSetup(console);
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, 12345).Run();

            var output = console.Output;
            Assert.Contains("shoe 1 · seed 12345", output);
            Assert.Matches(@"Seed\s+│\s+12345\b", output);
            Assert.Matches(@"Shoes\s+│\s+1\b", output);
            Assert.Contains("Replay this session: dotnet run --project blackjack -- --seed 12345", output);
            Assert.DoesNotContain("--shoe", output);
        }

        [Fact]
        public void App_UsesTheSeedChosenInSetup()
        {
            var console = NewConsole();
            console.Input.PushTextWithEnter("Tester");
            console.Input.PushTextWithEnter("y");         // change the rules
            for (var i = 0; i < 5; i++)
            {
                console.Input.PushKey(ConsoleKey.Enter);  // decks, minimum, soft 17, payout, penetration
            }

            console.Input.PushTextWithEnter("424242");    // the seed
            console.Input.PushKey(ConsoleKey.Enter);      // 1000 chips
            console.Input.PushTextWithEnter("0");         // bots
            console.Input.PushKey(ConsoleKey.Enter);      // keep the keys
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, 1).Run();

            Assert.Contains("shoe 1 · seed 424242", console.Output);
            Assert.Contains("-- --seed 424242", console.Output);
        }

        [Fact]
        public void App_CanStartAtALaterShoe()
        {
            var console = NewConsole();
            PushSetup(console);
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, 12345, firstShoe: 2).Run();

            var output = console.Output;
            Assert.Contains("shoe 2 · seed 12345", output);
            Assert.Matches(@"Shoes\s+│\s+2\b", output);
            Assert.Contains("-- --seed 12345 --shoe 2", output);
        }

        [Fact]
        public void TableScreen_ShowsEveryStageOfARound()
        {
            var console = NewConsole();
            var bot = new Player("Bot [1]", 100m, new BasicStrategyBot());
            var human = new Player("[you]", 100m, new BasicStrategyBot());
            // Bot: A, K (natural). Human: 10, 6, then hits 9 and busts. Dealer: 5 up, 4 in the hole.
            var game = new Game(new Rules(), new[] { bot, human }, Shoe.Stacked(Of("As 10h 5s Kd 6c 4d 9s")));
            var counter = new CardCounter();
            game.CardRevealed += counter.OnCardRevealed;
            var screen = new TableScreen(console, KeyBindings.Defaults(), new FastForward());
            screen.Seat(game, human, counter, new SessionStats(100m), 1);
            screen.Log("[green]a log line[/]");

            string Frame()
            {
                var start = console.Output.Length;
                screen.Refresh();
                return console.Output[start..];
            }

            screen.PendingBet = 25;
            var betting = Frame();
            Assert.Contains("waiting for bets", betting);
            Assert.Contains("place your bet", betting);
            Assert.Contains("next bet 25", betting);
            Assert.Contains("▼ YOU", betting);
            Assert.Contains("Bot [1]", betting);
            Assert.Contains("YOU · [you]", betting);
            Assert.Contains("a log line", betting);

            screen.PendingBet = null;
            game.PlaceBet(bot, 10m);
            game.PlaceBet(human, 10m);
            game.Deal();
            screen.Round = 1;
            var playing = Frame();
            Assert.Contains("round 1", playing);
            Assert.Contains("showing 5", playing);
            Assert.Contains("▒▒▒▒▒", playing);
            Assert.Contains("YOUR MOVE", playing);
            Assert.Contains("BJ  +15", playing);
            Assert.Matches(@"running\s+-1\b", playing); // A -1, 10 -1, 5 +1, K -1, 6 +1; the hole card is hidden

            game.Hit();
            var settled = Frame();
            Assert.Equal(Phase.Settled, game.Phase);
            Assert.Contains("BUST -10", settled);
            Assert.DoesNotContain("▒▒▒▒▒", settled);
        }

        [Fact]
        public void TableScreen_FallsBackToChipsOnAShortTerminal()
        {
            var console = NewConsole(height: 18);
            var human = new Player("Tester", 100m, new BasicStrategyBot());
            var game = new Game(new Rules(), new[] { human }, Shoe.Stacked(Of("10h 5s 6c 4d")));
            var screen = new TableScreen(console, KeyBindings.Defaults(), new FastForward());
            screen.Seat(game, human, new CardCounter(), new SessionStats(100m), 1);
            game.PlaceBet(human, 10m);
            game.Deal();

            screen.Refresh();

            Assert.Contains("10♥ 6♣", console.Output);
            Assert.Contains("DEALER  5♠ ▒▒  showing 5", console.Output);
        }

        [Fact]
        public void TableScreen_FitsAFullTableIntoEightyColumns()
        {
            var console = NewConsole(width: 80, height: 24);
            var human = new Player("Tester", 100m, new BasicStrategyBot());
            var seats = Enumerable.Range(1, 6).Select(i => new Player($"Bot {i}", 100m, new BasicStrategyBot())).ToList();
            seats.Insert(3, human);
            var game = new Game(new Rules(), seats, new Shoe(6, 2147483647));
            var screen = new TableScreen(console, KeyBindings.Defaults(), new FastForward());
            screen.Seat(game, human, new CardCounter(), new SessionStats(100m), 2147483647);
            foreach (var seat in seats)
            {
                game.PlaceBet(seat, 10m);
            }

            game.Deal();
            screen.Round = 1;
            screen.Refresh();

            // The round, the shoe, a ten-digit seed and the chips all stay readable.
            Assert.Contains("Bot 6", console.Output);
            Assert.Contains("round 1 · 6 decks · S17", console.Output);
            Assert.Contains("shoe 1 · seed 2147483647 · chips 90 ±0", console.Output);
            Assert.All(console.Lines, line => Assert.True(line.Length <= 80, line));
        }

        [Fact]
        public void TableScreen_DescribesTheRules()
        {
            Assert.Equal("6 decks · S17 · pays 3:2 · min 10", TableScreen.DescribeRules(new Rules()));
            Assert.Equal("6 decks · S17", TableScreen.DescribeRules(new Rules(), brief: true));
            Assert.Equal(
                "1 deck · H17 · pays 6:5 · min 25",
                TableScreen.DescribeRules(new Rules { DeckCount = 1, DealerHitsSoft17 = true, BlackjackPayout = 1.2m, MinimumBet = 25m }));
        }

        // A seat for the human at a one-player table, with the screen it reads keys through.
        private static (ConsoleStrategy Strategy, TableScreen Screen, FastForward Autopilot, SessionStats Stats) NewSeat(TestConsole console)
        {
            var keys = KeyBindings.Defaults();
            var autopilot = new FastForward();
            var stats = new SessionStats(100m);
            var screen = new TableScreen(console, keys, autopilot);
            var strategy = new ConsoleStrategy(screen, keys, autopilot, stats);
            var human = new Player("Tester", 100m, strategy);
            var game = new Game(new Rules(), new[] { human }, Shoe.Stacked(Of("10h 9c 6d 7s")));
            screen.Seat(game, human, new CardCounter(), stats, 1);
            return (strategy, screen, autopilot, stats);
        }

        [Fact]
        public void Betting_SpaceDealsTheOfferedBet_AndQEChangeIt()
        {
            var console = NewConsole();
            var (strategy, _, _, _) = NewSeat(console);
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.E);
            console.Input.PushKey(ConsoleKey.E);
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.Q);
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.Spacebar);

            Assert.Equal(10m, strategy.DecideBet(100m, 10m));  // starts at the minimum
            Assert.Equal(30m, strategy.DecideBet(100m, 10m));
            Assert.Equal(20m, strategy.DecideBet(100m, 10m));  // starts from the last bet
            Assert.Equal(15m, strategy.DecideBet(15.5m, 10m)); // capped at whole chips in hand
        }

        [Fact]
        public void Betting_TypedAmounts_MustFitTheTable()
        {
            var console = NewConsole();
            var (strategy, _, _, _) = NewSeat(console);
            console.Input.PushKey(ConsoleKey.D5);
            console.Input.PushKey(ConsoleKey.Spacebar);        // under the minimum: not dealt
            console.Input.PushKey(ConsoleKey.D0);
            console.Input.PushKey(ConsoleKey.D0);
            console.Input.PushKey(ConsoleKey.Enter);           // 500, more than the chips: not dealt
            console.Input.PushKey(ConsoleKey.Backspace);
            console.Input.PushKey(ConsoleKey.Backspace);       // back to 5
            console.Input.PushKey(ConsoleKey.Escape);          // cleared
            console.Input.PushKey(ConsoleKey.D4);
            console.Input.PushKey(ConsoleKey.D5);
            console.Input.PushKey(ConsoleKey.Spacebar);

            Assert.Equal(45m, strategy.DecideBet(100m, 10m));
            Assert.Contains("the minimum is 10", console.Output);
            Assert.Contains("you have 100", console.Output);
        }

        [Fact]
        public void Betting_CashingOutTakesTheKeyTwice()
        {
            var console = NewConsole();
            var (strategy, _, _, _) = NewSeat(console);
            console.Input.PushKey(ConsoleKey.X);
            console.Input.PushKey(ConsoleKey.Z);               // changed my mind
            console.Input.PushKey(ConsoleKey.X);
            console.Input.PushKey(ConsoleKey.X);

            Assert.Equal(0m, strategy.DecideBet(100m, 10m));
            Assert.Contains("Cash out with 100 chips?", console.Output);
        }

        [Fact]
        public void Betting_AutopilotKeys_HandTheSeatToTheBook()
        {
            var console = NewConsole();
            var (strategy, _, autopilot, _) = NewSeat(console);
            console.Input.PushKey(ConsoleKey.E);
            console.Input.PushKey(ConsoleKey.F);

            Assert.Equal(10m, strategy.DecideBet(100m, 10m));  // the book bets the minimum
            Assert.True(autopilot.IsActive);
            Assert.False(autopilot.SkippingShoes);
            Assert.Equal(Move.Stand, strategy.DecideMove(HandOf("10h 7c"), C("10s")));

            autopilot.Stop();
            console.Input.PushKey(ConsoleKey.R);
            strategy.DecideBet(100m, 10m);
            Assert.True(autopilot.SkippingShoes);
        }

        [Fact]
        public void Moves_AreSingleKeys_WithTheBookAlongside()
        {
            var console = NewConsole();
            var (strategy, screen, _, stats) = NewSeat(console);
            console.Input.PushKey(ConsoleKey.Z);               // ignored
            console.Input.PushKey(ConsoleKey.D);
            console.Input.PushKey(ConsoleKey.B);               // hide the book
            console.Input.PushKey(ConsoleKey.S);

            Assert.Equal(Move.Hit, strategy.DecideMove(HandOf("10h 6c"), C("10s")));
            Assert.Contains("book: hit", console.Output);
            Assert.Equal(Move.Stand, strategy.DecideMove(HandOf("10h 6c"), C("10s")));
            Assert.False(screen.ShowBook);

            screen.Refresh();
            Assert.Contains("The book says hit on hard 16 against a 10.", console.Output);
            Assert.Equal(2, stats.Decisions);
            Assert.Equal(1, stats.BookDecisions);
        }

        [Fact]
        public void TableSetup_CanChangeTheRules()
        {
            var console = NewConsole();
            console.Input.PushTextWithEnter("  Ada  ");
            console.Input.PushTextWithEnter("y");     // change the rules
            console.Input.PushTextWithEnter("1");     // decks
            console.Input.PushTextWithEnter("25");    // minimum bet
            console.Input.PushTextWithEnter("y");     // dealer hits soft 17
            console.Input.PushTextWithEnter("6:5");
            console.Input.PushTextWithEnter("60");    // penetration
            console.Input.PushTextWithEnter("777");   // seed
            console.Input.PushKey(ConsoleKey.Enter);  // default chips
            console.Input.PushTextWithEnter("6");     // bots
            console.Input.PushKey(ConsoleKey.Enter);  // keep the keys

            var settings = TableSetup.Ask(console, 1, KeyBindings.Defaults(), Path.Combine(Path.GetTempPath(), "unused.ini"));

            Assert.Equal("Ada", settings.Name);
            Assert.Equal(1, settings.Rules.DeckCount);
            Assert.Equal(25m, settings.Rules.MinimumBet);
            Assert.True(settings.Rules.DealerHitsSoft17);
            Assert.Equal(1.2m, settings.Rules.BlackjackPayout);
            Assert.Equal(0.6m, settings.Rules.Penetration);
            Assert.Equal(777, settings.Seed);
            Assert.Equal(1000m, settings.Chips);
            Assert.Equal(6, settings.Bots);
        }

        [Fact]
        public void TableSetup_RejectsStacksThatAreNotSensibleWholeNumbers()
        {
            var console = NewConsole();
            console.Input.PushTextWithEnter("Ada");
            console.Input.PushKey(ConsoleKey.Enter);  // keep the default rules
            console.Input.PushTextWithEnter("5e28");
            console.Input.PushTextWithEnter("2000000000");
            console.Input.PushTextWithEnter("1000.5");
            console.Input.PushTextWithEnter("1000000000");
            console.Input.PushKey(ConsoleKey.Enter);  // bots
            console.Input.PushKey(ConsoleKey.Enter);  // keep the keys

            var settings = TableSetup.Ask(console, 1, KeyBindings.Defaults(), Path.Combine(Path.GetTempPath(), "unused.ini"));

            Assert.Equal(1_000_000_000m, settings.Chips);
            Assert.Contains("Invalid input", console.Output);
            Assert.Contains("Pick a whole number from 10 to 1000000000.", console.Output);
        }

        [Fact]
        public void TableSetup_RebindsKeys_AndSavesThem()
        {
            var console = NewConsole();
            var keys = KeyBindings.Defaults();
            var path = Path.Combine(Path.GetTempPath(), $"blackjack-keys-{Guid.NewGuid():N}.ini");
            console.Input.PushTextWithEnter("Ada");
            console.Input.PushKey(ConsoleKey.Enter);  // keep the default rules
            console.Input.PushKey(ConsoleKey.Enter);  // chips
            console.Input.PushKey(ConsoleKey.Enter);  // bots
            console.Input.PushTextWithEnter("y");     // change the keys
            console.Input.PushKey(ConsoleKey.A);      // hit
            console.Input.PushKey(ConsoleKey.Enter);  // stand stays S
            console.Input.PushKey(ConsoleKey.B);      // deal: B is already the book's key
            console.Input.PushKey(ConsoleKey.G);      // deal
            console.Input.PushKey(ConsoleKey.Escape); // keep the rest

            try
            {
                TableSetup.Ask(console, 1, keys, path);

                Assert.Equal("A", keys.Label(KeyAction.Hit));
                Assert.Equal("S", keys.Label(KeyAction.Stand));
                Assert.Equal("G", keys.Label(KeyAction.Deal));
                Assert.Contains("already does", console.Output);
                var saved = File.ReadAllText(path);
                Assert.Contains("hit = A", saved);
                Assert.Contains("deal = G", saved);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
