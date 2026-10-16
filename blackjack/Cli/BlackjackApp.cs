using blackjack.Domain;
using blackjack.Engine;
using blackjack.Strategies;
using Spectre.Console;

namespace blackjack.Cli
{
    // Runs a session at the console: setup, then rounds on a live table until the player cashes out
    // or goes broke.
    internal sealed class BlackjackApp
    {
        private readonly IAnsiConsole console;
        private readonly TimeSpan pause;
        private readonly int seed;
        private readonly int firstShoe;
        private readonly KeyBindings keys;
        private readonly string keysPath;
        private readonly IReadOnlyList<string> keyWarnings;
        private readonly FastForward fastForward = new();
        private readonly HashSet<Hand> calledNaturals = new();
        private bool shuffledAfterRound;

        // pause is how long a bot's move or a dealer's draw stays on screen; zero for tests. seed is
        // offered in setup, and the shoes come out from firstShoe.
        public BlackjackApp(
            IAnsiConsole console,
            TimeSpan pause,
            int seed,
            int firstShoe = 1,
            KeyBindings? keys = null,
            string? keysPath = null,
            IReadOnlyList<string>? keyWarnings = null)
        {
            this.console = console;
            this.pause = pause;
            this.seed = seed;
            this.firstShoe = firstShoe;
            this.keys = keys ?? KeyBindings.Defaults();
            this.keysPath = keysPath ?? KeyBindings.ResolvePath(null);
            this.keyWarnings = keyWarnings ?? Array.Empty<string>();
        }

        // Card, turn and end-of-hand pauses, scaled for normal play or the autopilot's speed.
        private bool ShowEachCard => !fastForward.IsActive || fastForward.Speed <= FastForwardSpeed.Normal;
        private TimeSpan CardPause => Paced(play: 0.25, slow: 0.25, normal: 0.1, fast: 0, max: 0);
        private TimeSpan TurnPause => Paced(play: 1, slow: 1, normal: 0.33, fast: 0, max: 0);
        private TimeSpan HandEndPause => Paced(play: 0, slow: 1.5, normal: 1, fast: 0.5, max: 0);

        public void Run()
        {
            console.Write(new FigletText("Blackjack").Color(Color.Green));
            foreach (var warning in keyWarnings)
            {
                console.MarkupLine($"[yellow]{Markup.Escape(warning)}[/]");
            }

            var settings = TableSetup.Ask(console, seed, keys, keysPath);
            var rules = settings.Rules;

            var stats = new SessionStats(settings.Chips);
            var screen = new TableScreen(console, keys, fastForward);
            var human = new Player(settings.Name, settings.Chips, new ConsoleStrategy(screen, keys, fastForward, stats));
            var game = new Game(rules, SeatPlayers(human, settings), new Shoe(rules.DeckCount, settings.Seed, rules.Penetration, firstShoe));

            // The counter subscribes first so the table is drawn with the count already updated.
            var counter = new CardCounter();
            game.CardRevealed += counter.OnCardRevealed;
            game.Shuffled += counter.Reset;
            screen.Seat(game, human, counter, stats, settings.Seed);

            game.CardRevealed += card => OnCardRevealed(game, screen, human, card);
            game.Shuffled += () =>
            {
                fastForward.OnShuffled();

                // A cut-card shuffle happens as the round settles; it is announced after the results.
                if (game.Phase == Phase.Settled)
                {
                    shuffledAfterRound = true;
                }
                else
                {
                    screen.Log($"[aqua]The shoe ran out: shuffling shoe {game.ShoeNumber}.[/]");
                }
            };

            console.Clear();
            console.Live(screen.Build())
                .AutoClear(false)
                .Overflow(VerticalOverflow.Crop)
                .Cropping(VerticalOverflowCropping.Top)
                .Start(context =>
                {
                    screen.Attach(context);
                    PlayRounds(game, screen, human, stats);
                    screen.Detach();
                });

            ShowSummary(human, stats, settings, game);
        }

        // Bots fill the seats either side of the player.
        private static List<Player> SeatPlayers(Player human, TableSettings settings)
        {
            var seats = Enumerable.Range(1, settings.Bots)
                .Select(i => new Player($"Bot {i}", settings.Chips, new BasicStrategyBot()))
                .ToList();
            seats.Insert(seats.Count / 2, human);
            return seats;
        }

        private void PlayRounds(Game game, TableScreen screen, Player human, SessionStats stats)
        {
            while (true)
            {
                foreach (var bot in game.Players.Where(p => p != human && p.Chips < game.Rules.MinimumBet).ToList())
                {
                    game.RemovePlayer(bot);
                    screen.Log($"[grey]{TableScreen.Name(bot)} is out of chips and leaves the table.[/]");
                }

                if (human.Chips < game.Rules.MinimumBet)
                {
                    fastForward.Stop();
                    screen.SetBar(
                        "[bold red]You don't have enough chips for the minimum bet.[/]",
                        "[grey]press any key for your summary[/]",
                        BarTone.Alert);
                    screen.ReadKey();
                    return;
                }

                // Bets are decided while last round's results are still on screen.
                var bets = new List<(Player Player, decimal Amount)>();
                foreach (var player in game.Players)
                {
                    var amount = player.Strategy.DecideBet(player.Chips, game.Rules.MinimumBet);
                    if (amount > 0m)
                    {
                        bets.Add((player, amount));
                    }
                    else if (player == human)
                    {
                        return;
                    }
                }

                if (game.Phase == Phase.Settled)
                {
                    game.NewRound();
                }

                screen.Round++;
                screen.ClearLog();
                calledNaturals.Clear();
                foreach (var (player, amount) in bets)
                {
                    game.PlaceBet(player, amount);
                }

                game.Deal();
                PlayTurns(game, screen, human);
                AnnounceResults(game, screen, human, stats);

                if (shuffledAfterRound)
                {
                    screen.Log($"[aqua]Cut card reached: shuffling shoe {game.ShoeNumber}.[/]");
                    shuffledAfterRound = false;
                }

                if (fastForward.IsActive)
                {
                    FinishAutopilotHand(game, screen, human);
                }
            }
        }

        private void PlayTurns(Game game, TableScreen screen, Player human)
        {
            while (game.Phase == Phase.PlayerTurn)
            {
                var player = game.CurrentPlayer!;
                var hand = game.CurrentHand!;

                // Your own move redraws the table when it asks for a key.
                if (player != human || fastForward.IsActive)
                {
                    if (ShowEachCard)
                    {
                        screen.Refresh();
                    }

                    screen.Wait(TurnPause);
                }

                var move = player.Strategy.DecideMove(hand, game.DealerUpcard!);
                if (move == Move.Stand)
                {
                    screen.Log($"{Who(player, human)} {(player == human ? "stand" : "stands")} on {hand.Value}.");
                    game.Stand();
                }
                else
                {
                    game.Hit();
                }
            }
        }

        // Logs each card as it lands and animates the parts the player only watches.
        private void OnCardRevealed(Game game, TableScreen screen, Player human, Card card)
        {
            switch (game.Phase)
            {
                case Phase.PlayerTurn:
                    var player = game.CurrentPlayer!;
                    var hand = game.CurrentHand!;
                    var draws = player == human ? "draw" : "draws";
                    var busts = player == human ? "bust" : "busts";
                    screen.Log(hand.IsBust
                        ? $"{Who(player, human)} {draws} {CardArt.LogChip(card)} and [red]{busts} with {hand.Value}[/]."
                        : $"{Who(player, human)} {draws} {CardArt.LogChip(card)} for {hand.Value}.");
                    break;

                case Phase.DealerTurn:
                    screen.Log(game.DealerHand.Cards.Count == 2
                        ? $"Dealer turns over {CardArt.LogChip(card)}."
                        : $"Dealer draws {CardArt.LogChip(card)}.");
                    if (ShowEachCard)
                    {
                        screen.Refresh();
                        screen.Wait(TurnPause);
                    }

                    break;

                default:
                    // Dealing. Blackjacks are called as soon as they land; the hole card only shows
                    // here when the dealer peeks and has one.
                    if (game.HoleCardRevealed)
                    {
                        screen.Log($"Dealer turns over {CardArt.LogChip(card)}: [bold red]blackjack[/].");
                    }

                    foreach (var seat in game.Players)
                    {
                        var dealt = seat.Hands.FirstOrDefault();
                        if (dealt is { IsNatural: true } && calledNaturals.Add(dealt))
                        {
                            screen.Log(seat == human ? "[green]You have blackjack![/]" : $"[green]{TableScreen.Name(seat)} has blackjack![/]");
                        }
                    }

                    if (ShowEachCard)
                    {
                        screen.Refresh();
                        screen.Wait(CardPause);
                    }

                    break;
            }
        }

        private void AnnounceResults(Game game, TableScreen screen, Player human, SessionStats stats)
        {
            var dealer = game.DealerHand;
            if (!dealer.IsNatural)
            {
                screen.Log(dealer.IsBust ? $"[green]Dealer busts with {dealer.Value}.[/]" : $"Dealer has {dealer.Value}.");
            }

            var hand = human.Hands.FirstOrDefault();
            if (hand?.Outcome is not { } outcome)
            {
                return;
            }

            if (fastForward.IsActive)
            {
                stats.RecordAutopilot();
            }
            else
            {
                stats.Record(hand);
            }

            var net = TableScreen.Money(hand.Payout - hand.Bet);
            screen.Log(outcome switch
            {
                Outcome.Blackjack => $"[bold green]Blackjack pays! You win {net}.[/]",
                Outcome.Win => $"[bold green]You win {net}.[/]",
                Outcome.Push => "[bold yellow]Push. Your bet comes back.[/]",
                _ => $"[bold red]You lose {TableScreen.Money(hand.Bet)}.[/]"
            });
        }

        // Shows the finished hand for a moment, then stops if a key asked to or the shoes are done.
        private void FinishAutopilotHand(Game game, TableScreen screen, Player human)
        {
            if (human.Hands.FirstOrDefault() is { } hand)
            {
                fastForward.RecordHand(hand);
            }

            screen.Refresh();
            screen.Wait(HandEndPause);

            if (!fastForward.ShouldStop && human.Chips >= game.Rules.MinimumBet)
            {
                return;
            }

            var summary = $"{fastForward.Hands} {(fastForward.Hands == 1 ? "hand" : "hands")}, chips {TableScreen.SignedMoney(fastForward.Net)}";
            screen.Log(fastForward.SkippingShoes && fastForward.ShoesLeft == 0
                ? $"[aqua]Fresh shoe. The autopilot played {summary}.[/]"
                : $"[aqua]Autopilot off after {summary}.[/]");
            fastForward.Stop();
        }

        private void ShowSummary(Player human, SessionStats stats, TableSettings settings, Game game)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .HideHeaders()
                .AddColumn("")
                .AddColumn(new TableColumn("").RightAligned())
                .AddRow("Hands you played", stats.Rounds.ToString())
                .AddRow("Won / lost / pushed", $"{stats.Wins} / {stats.Losses} / {stats.Pushes}")
                .AddRow("Blackjacks", stats.Blackjacks.ToString());

            if (stats.Decisions > 0)
            {
                table.AddRow("Followed the book", $"{stats.BookDecisions} of {stats.Decisions} moves");
            }

            if (stats.AutopilotHands > 0)
            {
                table.AddRow("Hands on autopilot", stats.AutopilotHands.ToString());
            }

            table.AddRow("Started with", TableScreen.Money(stats.StartingChips))
                .AddRow("Leaving with", TableScreen.Money(human.Chips))
                .AddRow("Net", TableScreen.SignedMoney(human.Chips - stats.StartingChips))
                .AddRow("Seed", settings.Seed.ToString())
                .AddRow("Shoes", game.ShoeNumber == firstShoe ? firstShoe.ToString() : $"{firstShoe} to {game.ShoeNumber}");

            // The same seed, shoe and setup answers deal the same cards again.
            var replay = $"dotnet run --project blackjack -- --seed {settings.Seed}" + (firstShoe > 1 ? $" --shoe {firstShoe}" : "");

            console.WriteLine();
            console.Write(new Rule("[bold]Cashing out[/]") { Justification = Justify.Left, Style = new Style(Color.Green) });
            console.Write(table);
            console.MarkupLine($"Replay this session: [bold]{Markup.Escape(replay)}[/]");
            console.MarkupLine($"Thanks for playing, {TableScreen.Name(human)}!");
        }

        private static string Who(Player player, Player human) => player == human ? "You" : TableScreen.Name(player);

        private TimeSpan Paced(double play, double slow, double normal, double fast, double max)
        {
            var factor = !fastForward.IsActive
                ? play
                : fastForward.Speed switch
                {
                    FastForwardSpeed.Slow => slow,
                    FastForwardSpeed.Normal => normal,
                    FastForwardSpeed.Fast => fast,
                    _ => max
                };
            return pause * factor;
        }
    }
}
