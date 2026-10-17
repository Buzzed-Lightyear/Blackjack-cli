using blackjack.Domain;
using blackjack.Engine;
using blackjack.Engine.SideBets;
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
        private readonly HashSet<SideBetStake> announcedSideBets = new();
        private readonly Dictionary<Player, decimal> lastBets = new();
        private bool shuffledAfterRound;

        // While a split is dealt: the index of the hand being split, and how many of the two
        // cards have landed, so each can be logged against its hand.
        private int splitIndex = -1;
        private int splitDealt;

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
            var seat = new ConsoleStrategy(screen, keys, fastForward, stats, rules, settings.SideBets);
            var human = new Player(settings.Name, settings.Chips, seat);
            var seats = SeatPlayers(human, settings);
            var bots = seats.Where(p => p != human).ToList();
            var game = new Game(rules, seats, new Shoe(rules.DeckCount, settings.Seed, rules.Penetration, firstShoe), settings.SideBets);

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
                    PlayRounds(game, screen, human, seat, stats, counter);
                    screen.Detach();
                });

            ShowSummary(human, bots, stats, settings, game);
        }

        // Bots fill the seats either side of the player, each playing with its own Random from the
        // session seed, so the same seed seats the same table.
        private static List<Player> SeatPlayers(Player human, TableSettings settings)
        {
            var seats = settings.BotTypes
                .Select((type, i) => new Player($"Bot {i + 1}", settings.Chips, Bots.Create(type, settings.Rules, Bots.RandomFor(settings.Seed, i + 1))))
                .ToList();
            seats.Insert(seats.Count / 2, human);
            return seats;
        }

        // What a strategy is told about the table when it is asked for a decision.
        private static TableContext Context(Game game, CardCounter counter) =>
            new(game.Rules, counter.RunningCount, counter.TrueCount(game.DecksRemaining), game.DecksRemaining);

        private void PlayRounds(Game game, TableScreen screen, Player human, ConsoleStrategy seat, SessionStats stats, CardCounter counter)
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
                    var amount = player.Strategy.DecideBet(player.Chips, Context(game, counter));
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
                announcedSideBets.Clear();
                foreach (var (player, amount) in bets)
                {
                    game.PlaceBet(player, amount);
                    if (player != human && (!lastBets.TryGetValue(player, out var last) || last != amount))
                    {
                        screen.Log(BotBet(player, amount, counter.TrueCount(game.DecksRemaining)));
                    }

                    lastBets[player] = amount;
                }

                foreach (var (bet, amount) in seat.SideBetStakes)
                {
                    game.PlaceSideBet(human, bet, amount);
                }

                game.Deal();
                OfferInsurance(game, screen, human, counter);
                AnnounceSideBets(screen, human);
                PlayTurns(game, screen, human, counter);
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

        // With an ace up, each seat answers in turn before the dealer peeks.
        private void OfferInsurance(Game game, TableScreen screen, Player human, CardCounter counter)
        {
            while (game.Phase == Phase.Insurance)
            {
                var player = game.CurrentPlayer!;
                var hand = player.Hands[0];
                if (player.Strategy.DecideInsurance(hand, Context(game, counter)))
                {
                    screen.Log(player == human
                        ? $"You take insurance for {TableScreen.Money(hand.Bet / 2)}."
                        : $"{TableScreen.Name(player)} takes insurance.");
                    game.TakeInsurance();
                }
                else
                {
                    if (player == human)
                    {
                        screen.Log("You decline insurance.");
                    }

                    game.DeclineInsurance();
                }
            }

            if (human.Insurance > 0m)
            {
                screen.Log(human.InsurancePayout > 0m
                    ? $"[bold green]Insurance pays {TableScreen.Money(human.InsurancePayout - human.Insurance)}.[/]"
                    : "[red]Insurance lost.[/]");
            }
        }

        private void PlayTurns(Game game, TableScreen screen, Player human, CardCounter counter)
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

                var options = new MoveOptions(game.CanDouble, game.CanSplit, game.CanHit);
                var move = player.Strategy.DecideMove(hand, game.DealerUpcard!, options, Context(game, counter));
                switch (move)
                {
                    case Move.Stand:
                        screen.Log($"{Who(player, human)} {(player == human ? "stand" : "stands")} on {hand.Value}{TableScreen.HandTag(player, hand)}.");
                        game.Stand();
                        break;

                    case Move.Double:
                        game.Double();
                        break;

                    case Move.Split:
                        Split(game, screen, human, player, hand);
                        break;

                    default:
                        game.Hit();
                        break;
                }
            }
        }

        private void Split(Game game, TableScreen screen, Player human, Player player, Hand hand)
        {
            var aces = hand.Cards[0].Rank == Rank.Ace;
            screen.Log($"{Who(player, human)} {(player == human ? "split" : "splits")} {TableScreen.PairName(hand)}.");
            splitIndex = player.Hands.IndexOf(hand);
            splitDealt = 0;
            try
            {
                game.Split();
            }
            finally
            {
                splitIndex = -1;
            }

            if (aces && !game.Rules.HitSplitAces)
            {
                screen.Log("[grey]Split aces stand with one card each.[/]");
            }
        }

        // Logs each card as it lands and animates the parts the player only watches.
        private void OnCardRevealed(Game game, TableScreen screen, Player human, Card card)
        {
            switch (game.Phase)
            {
                case Phase.PlayerTurn:
                    var player = game.CurrentPlayer!;
                    if (splitIndex >= 0)
                    {
                        var dealt = player.Hands[splitIndex + splitDealt++];
                        screen.Log($"Hand {splitIndex + splitDealt} draws {CardArt.LogChip(card)} for {dealt.Value}.");
                        break;
                    }

                    var hand = game.CurrentHand!;
                    var draws = player == human ? "draw" : "draws";
                    var busts = player == human ? "bust" : "busts";
                    var tag = TableScreen.HandTag(player, hand);
                    if (hand.IsDoubled)
                    {
                        var doubles = $"{Who(player, human)} {(player == human ? "double" : "doubles")} to {TableScreen.Money(hand.Bet)}";
                        screen.Log(hand.IsBust
                            ? $"{doubles}, {draws} {CardArt.LogChip(card)} and [red]{busts} with {hand.Value}[/]{tag}."
                            : $"{doubles} and {draws} {CardArt.LogChip(card)} for {hand.Value}{tag}.");
                    }
                    else
                    {
                        screen.Log(hand.IsBust
                            ? $"{Who(player, human)} {draws} {CardArt.LogChip(card)} and [red]{busts} with {hand.Value}[/]{tag}."
                            : $"{Who(player, human)} {draws} {CardArt.LogChip(card)} for {hand.Value}{tag}.");
                    }

                    break;

                case Phase.DealerTurn:
                    if (game.DealerHand.Cards.Count == 2)
                    {
                        screen.Log($"Dealer turns over {CardArt.LogChip(card)}.");
                    }
                    else
                    {
                        if (game.DealerHand.Cards.Count == 3 && !game.Players.Any(p => p.Hands.Any(h => h.Outcome is null)))
                        {
                            screen.Log($"[grey]Dealer plays on for {PendingDealerBets(game)}.[/]");
                        }

                        screen.Log($"Dealer draws {CardArt.LogChip(card)}.");
                    }

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

        // The side bets still waiting on the dealer's hand, by name: "Bust It".
        private static string PendingDealerBets(Game game)
        {
            var names = game.Players
                .SelectMany(p => p.SideBets)
                .Where(s => !s.IsResolved && s.Bet.Timing == SideBetTiming.AfterDealer)
                .Select(s => Markup.Escape(s.Bet.Name))
                .Distinct()
                .ToList();
            return string.Join(" and ", names);
        }

        private void AnnounceResults(Game game, TableScreen screen, Player human, SessionStats stats)
        {
            var dealer = game.DealerHand;
            if (!dealer.IsNatural)
            {
                screen.Log(dealer.IsBust ? $"[green]Dealer busts with {dealer.Value}.[/]" : $"Dealer has {dealer.Value}.");
            }

            var hands = human.Hands;
            if (hands.Count == 0 || hands.Any(h => h.Outcome is null))
            {
                return;
            }

            if (fastForward.IsActive)
            {
                stats.RecordAutopilot(human);
            }
            else
            {
                stats.Record(human);
            }

            if (hands.Count == 1)
            {
                var hand = hands[0];
                var net = TableScreen.Money(hand.Payout - hand.Bet);
                screen.Log(hand.Outcome switch
                {
                    Outcome.Blackjack => $"[bold green]Blackjack pays! You win {net}.[/]",
                    Outcome.Win => $"[bold green]You win {net}.[/]",
                    Outcome.Push => "[bold yellow]Push. Your bet comes back.[/]",
                    _ => $"[bold red]You lose {TableScreen.Money(hand.Bet)}.[/]"
                });
            }
            else
            {
                for (var i = 0; i < hands.Count; i++)
                {
                    var hand = hands[i];
                    var net = TableScreen.Money(Math.Abs(hand.Payout - hand.Bet));
                    screen.Log(hand.Outcome switch
                    {
                        Outcome.Blackjack => $"[bold green]Hand {i + 1} wins {net} at {TableScreen.Money(game.Rules.BlackjackPayout)} to 1.[/]",
                        Outcome.Win => $"[bold green]Hand {i + 1} wins {net}.[/]",
                        Outcome.Push => $"[bold yellow]Hand {i + 1} pushes.[/]",
                        _ => $"[bold red]Hand {i + 1} loses {net}.[/]"
                    });
                }

                var total = hands.Sum(h => h.Payout - h.Bet);
                screen.Log(total switch
                {
                    > 0m => $"[bold green]You win {TableScreen.Money(total)} over {hands.Count} hands.[/]",
                    < 0m => $"[bold red]You lose {TableScreen.Money(-total)} over {hands.Count} hands.[/]",
                    _ => $"[bold yellow]You break even over {hands.Count} hands.[/]"
                });
            }

            AnnounceSideBets(screen, human);
        }

        // Side bets settle at the deal or after the dealer plays; each is announced once, when it has.
        private void AnnounceSideBets(TableScreen screen, Player human)
        {
            foreach (var stake in human.SideBets)
            {
                if (stake.Result is not { } result || !announcedSideBets.Add(stake))
                {
                    continue;
                }

                var name = Markup.Escape(stake.Bet.Name);
                screen.Log(result.Wins
                    ? $"[bold green]{name}: {Markup.Escape(result.Label)}, +{TableScreen.Money(stake.Net)}.[/]"
                    : $"[red]{name} loses ({Markup.Escape(result.Label)}).[/]");
            }
        }

        // Shows the finished hand for a moment, then stops if a key asked to or the shoes are done.
        private void FinishAutopilotHand(Game game, TableScreen screen, Player human)
        {
            fastForward.RecordRound(human);
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

        private void ShowSummary(Player human, IReadOnlyList<Player> bots, SessionStats stats, TableSettings settings, Game game)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .HideHeaders()
                .AddColumn("")
                .AddColumn(new TableColumn("").RightAligned())
                .AddRow("Hands you played", stats.Hands.ToString())
                .AddRow("Won / lost / pushed", $"{stats.Wins} / {stats.Losses} / {stats.Pushes}")
                .AddRow("Blackjacks", stats.Blackjacks.ToString())
                .AddRow("Doubled / split", $"{stats.Doubles} / {stats.Splits}");

            if (stats.InsuranceTaken > 0)
            {
                table.AddRow("Insurance taken", stats.InsuranceTaken.ToString());
            }

            if (stats.Decisions > 0)
            {
                table.AddRow("Followed the book", $"{stats.BookDecisions} of {stats.Decisions} moves");
            }

            if (stats.AutopilotHands > 0)
            {
                table.AddRow("Hands on autopilot", stats.AutopilotHands.ToString())
                    .AddRow("Autopilot net", TableScreen.SignedMoney(stats.AutopilotNet));
            }

            if (stats.SideBetsPlaced > 0)
            {
                table.AddRow("Side bets", $"{stats.SideBetsPlaced} placed, {stats.SideBetsWon} won")
                    .AddRow("Side-bet net", TableScreen.SignedMoney(stats.SideBetNet));
            }

            table.AddRow("Started with", TableScreen.Money(stats.StartingChips))
                .AddRow("Leaving with", TableScreen.Money(human.Chips))
                .AddRow("Net", TableScreen.SignedMoney(human.Chips - stats.StartingChips));

            // Every bot that sat down, against the stack it started with; a bot that went broke and
            // left the table is still listed.
            foreach (var bot in bots)
            {
                var kind = Bots.TypeOf(bot.Strategy) is { } type ? $" ({Bots.Name(type)})" : "";
                table.AddRow($"{TableScreen.Name(bot)}{kind}", TableScreen.SignedMoney(bot.Chips - settings.Chips));
            }

            table.AddRow("Seed", settings.Seed.ToString())
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

        // "Bot 1 bets 40." or, for a counter, "Bot 1 bets 40 at true +3."
        private static string BotBet(Player bot, decimal amount, decimal trueCount)
        {
            var count = Bots.TypeOf(bot.Strategy) == BotType.Counter ? $" at true {trueCount:+0.#;-0.#;0}" : "";
            return $"{TableScreen.Name(bot)} bets {TableScreen.Money(amount)}{count}.";
        }

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
