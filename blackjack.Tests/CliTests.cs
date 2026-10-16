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
    // Drives the console layer through Spectre's TestConsole: markup mistakes only show up at runtime.
    // Inside the live table every frame is appended to the output, so assertions look at the last one.
    public class CliTests
    {
        private const string FrameStart = "♠ BLACKJACK";

        private static TestConsole NewConsole(int width = 110, int height = 30) =>
            new TestConsole().Width(width).Height(height).Interactive();

        // Plays a list of moves in order and stands once they run out, or when the table won't
        // allow the next one. Never takes insurance.
        private sealed class Scripted : IPlayerStrategy
        {
            private readonly Queue<Move> moves;

            public Scripted(params Move[] moves)
            {
                this.moves = new Queue<Move>(moves);
            }

            public int Decisions { get; private set; }

            public decimal DecideBet(decimal chips, decimal minBet) => minBet;

            public bool DecideInsurance(Hand hand) => false;

            public Move DecideMove(Hand hand, Card dealerUpcard, MoveOptions options)
            {
                Decisions++;
                var move = moves.Count > 0 ? moves.Dequeue() : Move.Stand;
                return move switch
                {
                    Move.Double when !options.CanDouble => Move.Stand,
                    Move.Split when !options.CanSplit => Move.Stand,
                    Move.Hit when !options.CanHit => Move.Stand,
                    _ => move
                };
            }
        }

        private sealed record FirstRound(
            Game Game,
            Player Player,
            Hand Hand,
            Move FirstAdvice,
            MoveOptions FirstOptions,
            bool InsuranceOffered,
            int Decisions,
            bool ShuffledAtTheEnd);

        // Plays the first round from this seed the way BlackjackApp seats and deals it, with the
        // human on the scripted moves at the minimum and the bots on basic strategy. FirstAdvice
        // and FirstOptions describe the human's first hand when their turn comes.
        private static FirstRound PlayFirstRound(Rules rules, int seed, int bots, params Move[] moves)
        {
            var script = new Scripted(moves);
            var human = new Player("P", 1000m, script);
            var seats = Enumerable.Range(1, bots)
                .Select(i => new Player($"Bot {i}", 1000m, new BasicStrategyBot(rules)))
                .ToList();
            seats.Insert(seats.Count / 2, human);

            var game = new Game(rules, seats, new Shoe(rules.DeckCount, new Random(seed), rules.Penetration));
            var shuffledAtTheEnd = false;
            game.Shuffled += () => shuffledAtTheEnd = game.Phase == Phase.Settled;

            foreach (var player in seats)
            {
                game.PlaceBet(player, rules.MinimumBet);
            }

            game.Deal();
            var insuranceOffered = game.DealerUpcard!.Rank == Rank.Ace;
            while (game.Phase == Phase.Insurance)
            {
                if (game.CurrentPlayer!.Strategy.DecideInsurance(game.CurrentPlayer.Hands[0]))
                {
                    game.TakeInsurance();
                }
                else
                {
                    game.DeclineInsurance();
                }
            }

            var book = new BasicStrategyBot(rules);
            var firstAdvice = Move.Stand;
            var firstOptions = new MoveOptions(false, false);
            var firstSeen = false;
            while (game.Phase == Phase.PlayerTurn)
            {
                var player = game.CurrentPlayer!;
                var options = new MoveOptions(game.CanDouble, game.CanSplit, game.CanHit);
                if (player == human && !firstSeen)
                {
                    firstSeen = true;
                    firstOptions = options;
                    firstAdvice = book.DecideMove(game.CurrentHand!, game.DealerUpcard!, options);
                }

                switch (player.Strategy.DecideMove(game.CurrentHand!, game.DealerUpcard!, options))
                {
                    case Move.Hit:
                        game.Hit();
                        break;
                    case Move.Double:
                        game.Double();
                        break;
                    case Move.Split:
                        game.Split();
                        break;
                    default:
                        game.Stand();
                        break;
                }
            }

            return new FirstRound(game, human, human.Hands[0], firstAdvice, firstOptions, insuranceOffered, script.Decisions, shuffledAtTheEnd);
        }

        private static int FirstSeedWhere(int bots, Func<FirstRound, bool> wanted, params Move[] moves) =>
            FirstSeedWhere(new Rules(), bots, wanted, moves);

        private static int FirstSeedWhere(Rules rules, int bots, Func<FirstRound, bool> wanted, params Move[] moves)
        {
            for (var seed = 1; seed < 10_000; seed++)
            {
                if (wanted(PlayFirstRound(rules, seed, bots, moves)))
                {
                    return seed;
                }
            }

            throw new InvalidOperationException("No seed deals that round.");
        }

        // The player gets a decision and no insurance question: no natural on either side, no ace up.
        private static bool NeedsAMove(FirstRound round) =>
            !round.Hand.IsNatural && !round.Game.DealerHand.IsNatural && !round.InsuranceOffered;

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
            console.Input.PushKey(ConsoleKey.Enter);      // keep 21+3 and Bust It
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

        private static void AssertFits(string frame, int width)
        {
            var lines = frame.Split('\n').Select(l => l.TrimEnd('\r'));
            Assert.All(lines, line => Assert.True(line.Length <= width, $"{line.Length} wide: {line}"));
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

            new BlackjackApp(console, TimeSpan.Zero, new Random(seed)).Run();

            var output = console.Output;
            Assert.Contains("PLACE YOUR BET", output);
            Assert.Contains("T/G side bets", output);
            Assert.Contains("YOUR MOVE", output);
            Assert.Contains("book: ", output);
            Assert.Contains("You stand on", output);
            Assert.Contains("Cash out with", output);
            Assert.Contains("Hands you played", output);
            Assert.Matches(@"Doubled / split\s+│\s+0 / 0", output);
            Assert.DoesNotContain("Side bets", output);
            Assert.Contains("Thanks for playing, Tester!", output);
        }

        [Fact]
        public void App_EndsWhenThePlayerIsBroke()
        {
            // Losing the first round with only the minimum bet in chips ends the session.
            var seed = FirstSeedWhere(bots: 2, round => round.Hand.Outcome == Outcome.Lose && !round.InsuranceOffered);
            var console = NewConsole();
            PushSetup(console, bots: "2", chips: "10");
            console.Input.PushKey(ConsoleKey.Spacebar);  // bet the 10
            console.Input.PushKey(ConsoleKey.S);         // stand (unread if the dealer has blackjack)
            console.Input.PushKey(ConsoleKey.Z);         // on to the summary

            new BlackjackApp(console, TimeSpan.Zero, new Random(seed)).Run();

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
            var app = new BlackjackApp(console, TimeSpan.FromMilliseconds(4), new Random(seed));

            // The early S is thrown away during the deal animation, so the move is left waiting for a key.
            var error = Assert.Throws<InvalidOperationException>(app.Run);
            Assert.Contains("No input available", error.Message);
            Assert.DoesNotContain("You stand on", console.Output);
        }

        [Fact]
        public void App_CallsYourBlackjackBeforeTheDealerTurnsOver()
        {
            var seed = FirstSeedWhere(bots: 0, round => round.Hand.IsNatural && !round.Game.DealerHand.IsNatural && !round.InsuranceOffered);
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.Spacebar);
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, new Random(seed)).Run();

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
            console.Input.PushKey(ConsoleKey.Enter);  // double on any two
            console.Input.PushKey(ConsoleKey.Enter);  // double after split
            console.Input.PushKey(ConsoleKey.Enter);  // three hands
            console.Input.PushKey(ConsoleKey.Enter);  // no resplit aces
            console.Input.PushKey(ConsoleKey.Enter);  // no hitting split aces
            console.Input.PushKey(ConsoleKey.Enter);  // split 21 pays even money
            console.Input.PushKey(ConsoleKey.Enter);  // 1000 chips
            console.Input.PushTextWithEnter("6");     // bots
            console.Input.PushKey(ConsoleKey.Enter);  // keep the side bets
            console.Input.PushKey(ConsoleKey.Enter);  // keep the keys
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.S);
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, new Random(seed)).Run();

            // The count already shows the fresh shoe, and the shuffle note follows the results.
            var frame = LastFrame(console.Output);
            Assert.Matches(@"running\s+0\b", frame);
            AssertInOrder(frame, "Dealer (has|busts)", "You (win|lose)|Push", @"Cut card reached: shuffling a fresh shoe\.");
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

            new BlackjackApp(console, TimeSpan.Zero, new Random(seed)).Run();

            Assert.Contains("The book says hit on", console.Output);
            Assert.Matches(@"Followed the book\s+│\s+0 of 1 moves", console.Output);
        }

        [Fact]
        public void App_DoublesWithTheDoubleKey()
        {
            var seed = FirstSeedWhere(bots: 0, round => NeedsAMove(round) && round.FirstOptions.CanDouble && round.Hand.IsDoubled, Move.Double);
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.A);         // double: one card, then the hand is done
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, new Random(seed)).Run();

            var output = console.Output;
            Assert.Contains("A double", output);
            Assert.Matches(@"You double to 20(,| and) draw", output);
            Assert.Matches(@"Doubled / split\s+│\s+1 / 0", output);
            Assert.Matches(@"Hands you played\s+│\s+1\b", output);
        }

        [Fact]
        public void App_SplitsWithTheSplitKey_AndPlaysBothHands()
        {
            // Both split hands need a decision, so neither drew to 21.
            var seed = FirstSeedWhere(
                bots: 0,
                round => NeedsAMove(round) && round.Player.Hands.Count == 2 && round.Decisions == 3,
                Move.Split, Move.Stand, Move.Stand);
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.W);         // split
            console.Input.PushKey(ConsoleKey.S);         // stand on the first hand
            console.Input.PushKey(ConsoleKey.S);         // and the second
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, new Random(seed)).Run();

            var output = console.Output;
            Assert.Contains("W split", output);
            Assert.Matches(@"You split (\d+s|tens)\.", output);
            AssertInOrder(output, @"Hand 1 draws", @"Hand 2 draws", @"You stand on \d+ \(hand 1\)\.", @"You stand on \d+ \(hand 2\)\.");
            Assert.Contains("HAND 1 OF 2", output);
            Assert.Matches(@"Hand 1 (wins|loses|pushes)", output);
            Assert.Matches(@"You (win|lose|break even)[^.]* over 2 hands\.", output);
            Assert.Matches(@"Hands you played\s+│\s+2\b", output);
            Assert.Matches(@"Doubled / split\s+│\s+0 / 1", output);
        }

        [Fact]
        public void App_OffersInsuranceOnAnAce_AndPaysItOnADealerBlackjack()
        {
            var seed = FirstSeedWhere(bots: 0, round => round.InsuranceOffered && round.Game.DealerHand.IsNatural);
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.Z);         // take insurance
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, new Random(seed)).Run();

            var output = console.Output;
            Assert.Contains("Dealer shows an ace. Insurance for 5 chips?", output);
            Assert.Contains("Z yes", output);
            Assert.Contains("INSURANCE?", output);
            AssertInOrder(output, @"You take insurance for 5\.", "The book never takes insurance", "blackjack", @"Insurance pays 10\.");
            Assert.Matches(@"Insurance taken\s+│\s+1\b", output);
        }

        [Fact]
        public void App_InsuranceIsLost_WhenTheDealerHasNoBlackjack()
        {
            var seed = FirstSeedWhere(bots: 0, round => round.InsuranceOffered && !round.Game.DealerHand.IsNatural && !round.Hand.IsNatural);
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.Z);         // take insurance
            console.Input.PushKey(ConsoleKey.S);         // then stand
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, new Random(seed)).Run();

            AssertInOrder(console.Output, @"You take insurance for 5\.", @"Insurance lost\.", "You stand on");
        }

        [Fact]
        public void App_DeclinesInsurance_WithTheDealKey()
        {
            var seed = FirstSeedWhere(bots: 0, round => round.InsuranceOffered && !round.Game.DealerHand.IsNatural && !round.Hand.IsNatural);
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.D);         // not an answer: ignored
            console.Input.PushKey(ConsoleKey.Spacebar);  // no insurance
            console.Input.PushKey(ConsoleKey.S);
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, new Random(seed)).Run();

            var output = console.Output;
            Assert.Contains("Space NO", output);
            AssertInOrder(output, @"You decline insurance\.", "You stand on");
            Assert.DoesNotContain("Insurance lost", output);
            Assert.DoesNotContain("Insurance taken", output);
        }

        [Fact]
        public void App_StakesSideBets_WithTheirKeys()
        {
            var seed = FirstSeedWhere(bots: 0, NeedsAMove);
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.T);         // 21+3 at the minimum
            console.Input.PushKey(ConsoleKey.G);         // Bust It at the minimum
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.S);
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, new Random(seed)).Run();

            var output = console.Output;
            Assert.Contains("T 21+3 10", output);
            Assert.Contains("G Bust It 10", output);
            Assert.Matches(@"21\+3( loses \(no hand\)|: [a-z ]+, \+\d+)\.", output);
            Assert.Matches(@"Bust It( loses \((no bust|dealer blackjack)\)|: \d-card bust, \+\d+)\.", output);
            Assert.Matches(@"Side bets\s+│\s+2 placed, [012] won", output);
            Assert.Matches(@"Side-bet net\s+│\s+[+\-±]\d+", output);
        }

        [Fact]
        public void App_Autopilot_PlaysYourSeatUntilAKeyStopsIt()
        {
            var console = NewConsole();
            PushSetup(console);
            console.Input.PushKey(ConsoleKey.F);         // autopilot
            console.Input.PushKey(ConsoleKey.Z);         // any key: stop after this hand
            PushCashOut(console);

            new BlackjackApp(console, TimeSpan.Zero, new Random(1)).Run();

            var output = console.Output;
            Assert.Contains("AUTOPILOT", output);
            Assert.Matches(@"Autopilot off after \d hands?", output);
            Assert.Matches(@"Hands on autopilot\s+│\s+\d\b", output);
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

            new BlackjackApp(console, TimeSpan.Zero, new Random(3)).Run();

            var frame = LastFrame(console.Output);
            Assert.Contains("Fresh shoe. The autopilot played", frame);
            Assert.Matches(@"running\s+0\b", frame);
            Assert.Matches(@"decks\s+6\.0 left", frame);
            Assert.Contains("SKIPPING TO THE NEXT SHOE", console.Output);
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
            screen.Seat(game, human, counter, new SessionStats(100m));
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
            screen.Seat(game, human, new CardCounter(), new SessionStats(100m));
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
            var game = new Game(new Rules(), seats, new Shoe(6, new Random(5)));
            var screen = new TableScreen(console, KeyBindings.Defaults(), new FastForward());
            screen.Seat(game, human, new CardCounter(), new SessionStats(100m));
            foreach (var seat in seats)
            {
                game.PlaceBet(seat, 10m);
            }

            game.Deal();
            screen.Refresh();

            Assert.Contains("Bot 6", console.Output);
            Assert.All(console.Lines, line => Assert.True(line.Length <= 80, line));
        }

        // A full table where you split 8s against a 6 with both side bets down, through to the
        // dealer's three-card bust: every frame must fit the terminal with its key parts visible.
        [Theory]
        [InlineData(80, 24)]
        [InlineData(110, 30)]
        public void TableScreen_FitsTwoHandsAndTwoSideBets(int width, int height)
        {
            var console = NewConsole(width, height);
            var human = new Player("Tester", 100m, new Scripted());
            var seats = Enumerable.Range(1, 6).Select(i => new Player($"Bot {i}", 100m, new Scripted())).ToList();
            seats.Insert(3, human);
            var sideBets = new[] { SideBets.All[0], SideBets.All[1] };
            var shoe = Shoe.Stacked(Of("10h 10c 10d 8h 10s 9h 9c 6d 7h 7c 7d 8c 7s 8d 8s 10s 3h Kc 5c 9s"));
            var game = new Game(new Rules(), seats, shoe, sideBets);
            var screen = new TableScreen(console, KeyBindings.Defaults(), new FastForward());
            screen.Seat(game, human, new CardCounter(), new SessionStats(100m));
            screen.Round = 1;

            string Frame()
            {
                var start = console.Output.Length;
                screen.Refresh();
                var frame = console.Output[start..];
                AssertFits(frame, width);
                return frame;
            }

            foreach (var seat in seats)
            {
                game.PlaceBet(seat, 10m);
            }

            game.PlaceSideBet(human, sideBets[0], 10m);
            game.PlaceSideBet(human, sideBets[1], 10m);
            screen.PendingBet = 10;
            screen.PendingSideBets = new long[] { 10, 10 };
            var betting = Frame();
            Assert.Contains("T 21+3 10", betting);
            Assert.Contains("G Bust It 10", betting);
            screen.PendingBet = null;
            screen.PendingSideBets = null;

            game.Deal();
            game.Stand();
            game.Stand();
            game.Stand();
            game.Split();
            var split = Frame();
            Assert.Contains("HAND 1 OF 2", split);
            Assert.Contains("▶ 8♥ 3♥", split);
            Assert.Contains("8♣ K♣", split);
            Assert.Contains("21+3 no hand -10", split); // 8-8-6 is nothing, settled at the deal
            Assert.Contains("Bust It 10", split);       // still waiting on the dealer

            game.Hit();
            game.Stand();
            var second = Frame();
            Assert.Contains("HAND 2 OF 2", second);
            Assert.Contains("▶ 8♣ K♣", second);

            while (game.Phase == Phase.PlayerTurn)
            {
                game.Stand();
            }

            var settled = Frame();
            Assert.Equal(Phase.Settled, game.Phase);
            Assert.Contains("2 HANDS +20", settled);
            Assert.Contains("WIN +10", settled);
            Assert.Contains("21+3 no hand -10 · Bust It 3-card bust +20", settled);
            Assert.Contains("BUST 25", settled);
        }

        [Fact]
        public void TableScreen_ShowsTheInsuranceQuestion()
        {
            var console = NewConsole();
            var bot = new Player("Bot 1", 100m, new BasicStrategyBot());
            var human = new Player("Tester", 100m, new BasicStrategyBot());
            var game = new Game(new Rules(), new[] { bot, human }, Shoe.Stacked(Of("10h 9c Ad 7s 8d Ks")));
            var screen = new TableScreen(console, KeyBindings.Defaults(), new FastForward());
            screen.Seat(game, human, new CardCounter(), new SessionStats(100m));
            game.PlaceBet(bot, 10m);
            game.PlaceBet(human, 10m);
            game.Deal();

            screen.Refresh();
            Assert.Contains("Bot 1 is offered insurance…", console.Output);
            Assert.Contains("showing an ace", console.Output);

            game.DeclineInsurance();
            var start = console.Output.Length;
            screen.Refresh();
            Assert.Contains("INSURANCE?", console.Output[start..]);
        }

        [Fact]
        public void TableScreen_DescribesTheRules()
        {
            Assert.Equal("6 decks · S17 · pays 3:2 · min 10", TableScreen.DescribeRules(new Rules()));
            Assert.Equal(
                "1 deck · H17 · pays 6:5 · min 25",
                TableScreen.DescribeRules(new Rules { DeckCount = 1, DealerHitsSoft17 = true, BlackjackPayout = 1.2m, MinimumBet = 25m }));
        }

        // A seat for the human at a one-player table, with the screen it reads keys through.
        private static (ConsoleStrategy Strategy, TableScreen Screen, FastForward Autopilot, SessionStats Stats) NewSeat(
            TestConsole console,
            IReadOnlyList<ISideBet>? sideBets = null)
        {
            var keys = KeyBindings.Defaults();
            var autopilot = new FastForward();
            var stats = new SessionStats(100m);
            var screen = new TableScreen(console, keys, autopilot);
            var strategy = new ConsoleStrategy(screen, keys, autopilot, stats, new Rules(), sideBets);
            var human = new Player("Tester", 100m, strategy);
            var game = new Game(new Rules(), new[] { human }, Shoe.Stacked(Of("10h 9c 6d 7s")), sideBets);
            screen.Seat(game, human, new CardCounter(), stats);
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
            Assert.Empty(strategy.SideBetStakes);
            Assert.DoesNotContain("side bet", console.Output);
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
        public void Betting_SideBetKeys_AddTheMinimum_AndWrapPastTheCap()
        {
            var console = NewConsole();
            var sideBets = new[] { SideBets.All[0], SideBets.All[1] };
            var (strategy, _, _, _) = NewSeat(console, sideBets);
            console.Input.PushKey(ConsoleKey.T);               // 21+3: 10
            console.Input.PushKey(ConsoleKey.T);               // 20 would top the main bet: back to 0
            console.Input.PushKey(ConsoleKey.T);               // 10 again
            console.Input.PushKey(ConsoleKey.G);               // Bust It: 10
            console.Input.PushKey(ConsoleKey.Spacebar);

            Assert.Equal(10m, strategy.DecideBet(100m, 10m));
            Assert.Equal(new[] { (sideBets[0], 10m), (sideBets[1], 10m) }, strategy.SideBetStakes);
            Assert.Contains("T/G side bets", console.Output);
            Assert.Contains("T 21+3 10", console.Output);
            Assert.Contains("G Bust It 0", console.Output);
            Assert.Contains("G Bust It 10", console.Output);

            // The stakes are remembered, and a bigger main bet makes room for more.
            console.Input.PushKey(ConsoleKey.E);               // bet 20
            console.Input.PushKey(ConsoleKey.T);               // 21+3: 20
            console.Input.PushKey(ConsoleKey.Spacebar);

            Assert.Equal(20m, strategy.DecideBet(100m, 10m));
            Assert.Equal(new[] { (sideBets[0], 20m), (sideBets[1], 10m) }, strategy.SideBetStakes);

            // With 35 chips the main bet of 20 leaves 15: the first stake keeps what fits, the
            // second is squeezed out.
            console.Input.PushKey(ConsoleKey.Spacebar);

            Assert.Equal(20m, strategy.DecideBet(35m, 10m));
            Assert.Equal(new[] { (sideBets[0], 15m) }, strategy.SideBetStakes);
        }

        [Fact]
        public void Betting_SideBetsAreOffOnAutopilot()
        {
            var console = NewConsole();
            var sideBets = new[] { SideBets.All[0], SideBets.All[1] };
            var (strategy, _, autopilot, _) = NewSeat(console, sideBets);
            console.Input.PushKey(ConsoleKey.T);
            console.Input.PushKey(ConsoleKey.F);

            Assert.Equal(10m, strategy.DecideBet(100m, 10m));
            Assert.True(autopilot.IsActive);
            Assert.Empty(strategy.SideBetStakes);
            Assert.False(strategy.DecideInsurance(HandOf("10h 7c")));
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
            Assert.Equal(Move.Stand, strategy.DecideMove(HandOf("10h 7c"), C("10s"), new MoveOptions(true, false)));
            Assert.Equal(Move.Double, strategy.DecideMove(HandOf("6h 5c"), C("10s"), new MoveOptions(true, false)));

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

            Assert.Equal(Move.Hit, strategy.DecideMove(HandOf("10h 6c"), C("10s"), new MoveOptions(true, false)));
            Assert.Contains("book: hit", console.Output);
            Assert.Equal(Move.Stand, strategy.DecideMove(HandOf("10h 6c"), C("10s"), new MoveOptions(true, false)));
            Assert.False(screen.ShowBook);

            screen.Refresh();
            Assert.Contains("The book says hit on hard 16 against a 10.", console.Output);
            Assert.Equal(2, stats.Decisions);
            Assert.Equal(1, stats.BookDecisions);
        }

        [Fact]
        public void Moves_OfferDoubleAndSplit_OnlyWhenTheTableAllowsThem()
        {
            var console = NewConsole();
            var (strategy, _, _, _) = NewSeat(console);
            console.Input.PushKey(ConsoleKey.W);
            var start = console.Output.Length;

            Assert.Equal(Move.Split, strategy.DecideMove(HandOf("8h 8c"), C("6s"), new MoveOptions(true, true)));
            var offered = console.Output[start..];
            Assert.Contains("A double", offered);
            Assert.Contains("W SPLIT", offered);
            Assert.Contains("book: split", offered);
            Assert.Contains("a pair of 8s against a 6", offered);

            console.Input.PushKey(ConsoleKey.W);               // no longer on offer: ignored
            console.Input.PushKey(ConsoleKey.A);               // nor is this
            console.Input.PushKey(ConsoleKey.D);
            start = console.Output.Length;

            Assert.Equal(Move.Hit, strategy.DecideMove(HandOf("8h 8c"), C("6s"), new MoveOptions(false, false)));
            var limited = console.Output[start..];
            Assert.DoesNotContain("A double", limited);
            Assert.DoesNotContain("W split", limited);
            Assert.Contains("book: stand", limited); // 16 against a 6 when it can't be split

            console.Input.PushKey(ConsoleKey.D);               // split aces can't be hit: ignored
            console.Input.PushKey(ConsoleKey.S);
            start = console.Output.Length;

            Assert.Equal(Move.Stand, strategy.DecideMove(HandOf("Ah Ac"), C("6s"), new MoveOptions(false, true, CanHit: false)));
            var acesOnly = console.Output[start..];
            Assert.DoesNotContain("D hit", acesOnly);
            Assert.Contains("The book says stand on a pair of 8s against a 6.", acesOnly);
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
            console.Input.PushTextWithEnter("n");     // double on hard 9 to 11 only
            console.Input.PushKey(ConsoleKey.Enter);  // double after split stays on
            console.Input.PushTextWithEnter("4");     // hands after splitting
            console.Input.PushTextWithEnter("y");     // resplit aces
            console.Input.PushKey(ConsoleKey.Enter);  // no hitting split aces
            console.Input.PushTextWithEnter("n");     // a split 21 pays like a blackjack
            console.Input.PushKey(ConsoleKey.Enter);  // default chips
            console.Input.PushTextWithEnter("6");     // bots
            console.Input.PushKey(ConsoleKey.Enter);  // keep the side bets
            console.Input.PushKey(ConsoleKey.Enter);  // keep the keys

            var settings = TableSetup.Ask(console, KeyBindings.Defaults(), Path.Combine(Path.GetTempPath(), "unused.ini"));

            Assert.Equal("Ada", settings.Name);
            Assert.Equal(1, settings.Rules.DeckCount);
            Assert.Equal(25m, settings.Rules.MinimumBet);
            Assert.True(settings.Rules.DealerHitsSoft17);
            Assert.Equal(1.2m, settings.Rules.BlackjackPayout);
            Assert.Equal(0.6m, settings.Rules.Penetration);
            Assert.False(settings.Rules.DoubleOnAnyTwo);
            Assert.True(settings.Rules.DoubleAfterSplit);
            Assert.Equal(4, settings.Rules.MaxSplitHands);
            Assert.True(settings.Rules.ResplitAces);
            Assert.False(settings.Rules.HitSplitAces);
            Assert.False(settings.Rules.SplitBlackjackPaysEven);
            Assert.Equal(1000m, settings.Chips);
            Assert.Equal(6, settings.Bots);
            Assert.Equal(new[] { "21+3", "Bust It" }, settings.SideBets.Select(b => b.Name));
        }

        [Fact]
        public void TableSetup_CanChangeTheSideBets_ToTwoDifferentOnes()
        {
            var console = NewConsole();
            console.Input.PushTextWithEnter("Ada");
            console.Input.PushKey(ConsoleKey.Enter);          // keep the default rules
            console.Input.PushKey(ConsoleKey.Enter);          // chips
            console.Input.PushKey(ConsoleKey.Enter);          // bots
            console.Input.PushTextWithEnter("y");             // change the side bets
            console.Input.PushTextWithEnter("Lucky Ladies");
            console.Input.PushTextWithEnter("Lucky Ladies");  // the same twice: refused
            console.Input.PushTextWithEnter("Royal Match");
            console.Input.PushKey(ConsoleKey.Enter);          // keep the keys

            var settings = TableSetup.Ask(console, KeyBindings.Defaults(), Path.Combine(Path.GetTempPath(), "unused.ini"));

            Assert.Contains("Change the side bets? (21+3 and Bust It)", console.Output);
            Assert.Contains("Pick a bet that differs from the first.", console.Output);
            Assert.Equal(new[] { "Lucky Ladies", "Royal Match" }, settings.SideBets.Select(b => b.Name));
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
            console.Input.PushKey(ConsoleKey.Enter);  // keep the side bets
            console.Input.PushKey(ConsoleKey.Enter);  // keep the keys

            var settings = TableSetup.Ask(console, KeyBindings.Defaults(), Path.Combine(Path.GetTempPath(), "unused.ini"));

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
            console.Input.PushKey(ConsoleKey.Enter);  // keep the side bets
            console.Input.PushTextWithEnter("y");     // change the keys
            console.Input.PushKey(ConsoleKey.H);      // hit
            console.Input.PushKey(ConsoleKey.Enter);  // stand stays S
            console.Input.PushKey(ConsoleKey.Enter);  // double stays A
            console.Input.PushKey(ConsoleKey.Enter);  // split stays W
            console.Input.PushKey(ConsoleKey.B);      // deal: B is already the book's key
            console.Input.PushKey(ConsoleKey.G);      // deal: G is already the second side bet's key
            console.Input.PushKey(ConsoleKey.Y);      // deal
            console.Input.PushKey(ConsoleKey.Escape); // keep the rest

            try
            {
                TableSetup.Ask(console, keys, path);

                Assert.Equal("H", keys.Label(KeyAction.Hit));
                Assert.Equal("S", keys.Label(KeyAction.Stand));
                Assert.Equal("Y", keys.Label(KeyAction.Deal));
                Assert.Contains("already does", console.Output);
                var saved = File.ReadAllText(path);
                Assert.Contains("hit = H", saved);
                Assert.Contains("deal = Y", saved);
                Assert.Contains("side_bet_2 = G", saved);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
