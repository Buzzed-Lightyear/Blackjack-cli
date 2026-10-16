using blackjack.Domain;
using blackjack.Engine;
using blackjack.Engine.SideBets;
using blackjack.Strategies;

namespace blackjack.Cli
{
    // The human seat: reads bets and moves from single keys in the table's action bar, keeping
    // Console out of the engine. On autopilot the book plays instead, at the table minimum and
    // with no side bets or insurance.
    internal sealed class ConsoleStrategy : IPlayerStrategy
    {
        private static readonly KeyAction[] SideBetKeys = { KeyAction.SideBet1, KeyAction.SideBet2 };

        private readonly TableScreen screen;
        private readonly KeyBindings keys;
        private readonly FastForward fastForward;
        private readonly SessionStats stats;
        private readonly IReadOnlyList<ISideBet> sideBets;
        private readonly BasicStrategyBot book;
        private readonly long[] sideStakes;
        private long bet;

        public ConsoleStrategy(
            TableScreen screen,
            KeyBindings keys,
            FastForward fastForward,
            SessionStats stats,
            Rules? rules = null,
            IReadOnlyList<ISideBet>? sideBets = null)
        {
            this.screen = screen;
            this.keys = keys;
            this.fastForward = fastForward;
            this.stats = stats;
            this.sideBets = sideBets ?? Array.Empty<ISideBet>();
            book = new BasicStrategyBot(rules ?? new Rules());
            sideStakes = new long[Math.Min(this.sideBets.Count, SideBetKeys.Length)];
        }

        // The side bets chosen along with the last bet, to place once the main bet is down.
        public IReadOnlyList<(ISideBet Bet, decimal Amount)> SideBetStakes =>
            fastForward.IsActive
                ? Array.Empty<(ISideBet, decimal)>()
                : sideStakes.Select((stake, i) => (sideBets[i], (decimal)stake)).Where(s => s.Item2 > 0m).ToList();

        // Bets are whole chips, so a balance never has more decimals than the table shows
        // (3:2 and 6:5 on whole bets leave at most one). Returns 0 to cash out.
        public decimal DecideBet(decimal chips, TableContext table)
        {
            var min = (long)decimal.Ceiling(table.Rules.MinimumBet);
            var max = (long)decimal.Floor(Math.Min(chips, long.MaxValue));
            if (max < min)
            {
                return 0m;
            }

            if (fastForward.IsActive)
            {
                return book.DecideBet(chips, table);
            }

            bet = Math.Clamp(bet == 0 ? min : bet, min, max);
            var typed = "";
            var cashingOut = false;
            try
            {
                while (true)
                {
                    var shown = typed.Length > 0 && long.TryParse(typed, out var value) ? value : bet;
                    ClampSideStakes(shown >= min && shown <= max ? shown : bet, max);
                    screen.PendingBet = shown;
                    screen.PendingSideBets = sideStakes;
                    if (cashingOut)
                    {
                        screen.SetBar(
                            $"[bold red]Cash out with {TableScreen.Money(chips)} chips?[/]  {screen.Key(KeyAction.CashOut)} again to confirm",
                            "[grey]any other key keeps you at the table[/]",
                            BarTone.Alert);
                    }
                    else
                    {
                        screen.SetBar(BetLine(typed, min, max), BetCommands(), BarTone.Input);
                    }

                    var key = screen.ReadKey();
                    if (cashingOut)
                    {
                        if (keys.Is(KeyAction.CashOut, key))
                        {
                            return 0m;
                        }

                        cashingOut = false;
                        continue;
                    }

                    if (screen.HandleToggle(key))
                    {
                        continue;
                    }

                    if (key.KeyChar is >= '0' and <= '9')
                    {
                        if (typed.Length < 9 && !(typed.Length == 0 && key.KeyChar == '0'))
                        {
                            typed += key.KeyChar;
                        }
                    }
                    else if (key.Key == ConsoleKey.Backspace)
                    {
                        typed = typed.Length > 0 ? typed[..^1] : typed;
                    }
                    else if (key.Key == ConsoleKey.Escape)
                    {
                        typed = "";
                    }
                    else if (keys.Is(KeyAction.BetDown, key))
                    {
                        typed = "";
                        bet = Math.Max(min, bet - min);
                    }
                    else if (keys.Is(KeyAction.BetUp, key))
                    {
                        typed = "";
                        bet = Math.Min(max, bet + min);
                    }
                    else if (SideBetPressed(key) is { } side)
                    {
                        AddSideStake(side, min, shown >= min && shown <= max ? shown : bet, max);
                    }
                    else if (key.Key == ConsoleKey.Enter || keys.Is(KeyAction.Deal, key))
                    {
                        if (typed.Length == 0)
                        {
                            ClampSideStakes(bet, max);
                            return bet;
                        }

                        var amount = long.Parse(typed);
                        if (amount >= min && amount <= max)
                        {
                            bet = amount;
                            ClampSideStakes(bet, max);
                            return bet;
                        }
                    }
                    else if (keys.Is(KeyAction.FastForward, key))
                    {
                        fastForward.Watch();
                        return book.DecideBet(chips, table);
                    }
                    else if (keys.Is(KeyAction.NextShoe, key))
                    {
                        fastForward.SkipShoe();
                        return book.DecideBet(chips, table);
                    }
                    else if (keys.Is(KeyAction.CashOut, key))
                    {
                        cashingOut = true;
                    }
                }
            }
            finally
            {
                screen.PendingBet = null;
                screen.PendingSideBets = null;
                screen.ClearBar();
            }
        }

        public bool DecideInsurance(Hand hand, TableContext table)
        {
            if (fastForward.IsActive)
            {
                return book.DecideInsurance(hand, table);
            }

            try
            {
                while (true)
                {
                    screen.SetBar(InsuranceLine(hand), InsuranceCommands(), BarTone.Input);
                    var key = screen.ReadKey();
                    if (screen.HandleToggle(key))
                    {
                        continue;
                    }

                    if (keys.Is(KeyAction.Insurance, key))
                    {
                        screen.Log("[yellow]The book never takes insurance.[/]");
                        return true;
                    }

                    if (keys.Is(KeyAction.Deal, key))
                    {
                        return false;
                    }
                }
            }
            finally
            {
                screen.ClearBar();
            }
        }

        public Move DecideMove(Hand hand, Card dealerUpcard, MoveOptions options, TableContext table)
        {
            var advice = book.DecideMove(hand, dealerUpcard, options, table);
            if (fastForward.IsActive)
            {
                return advice;
            }

            try
            {
                while (true)
                {
                    screen.SetBar(MoveLine(hand, dealerUpcard, advice), MoveCommands(options, advice), BarTone.Input);
                    var key = screen.ReadKey();
                    if (screen.HandleToggle(key))
                    {
                        continue;
                    }

                    Move? move = options.CanHit && keys.Is(KeyAction.Hit, key) ? Move.Hit
                        : keys.Is(KeyAction.Stand, key) ? Move.Stand
                        : options.CanDouble && keys.Is(KeyAction.Double, key) ? Move.Double
                        : options.CanSplit && keys.Is(KeyAction.Split, key) ? Move.Split
                        : null;
                    if (move is null)
                    {
                        continue;
                    }

                    stats.RecordDecision(move == advice);
                    if (move != advice)
                    {
                        screen.Log($"[yellow]The book says {Verb(advice)} on {Describe(hand)} against {Describe(dealerUpcard)}.[/]");
                    }

                    return move.Value;
                }
            }
            finally
            {
                screen.ClearBar();
            }
        }

        private int? SideBetPressed(ConsoleKeyInfo key)
        {
            for (var i = 0; i < sideStakes.Length; i++)
            {
                if (keys.Is(SideBetKeys[i], key))
                {
                    return i;
                }
            }

            return null;
        }

        // A side bet can't be more than the main bet, nor more than the chips left once the main
        // bet and the other side bet are down. Each press adds the minimum; past the cap the stake
        // wraps round to nothing.
        private void AddSideStake(int index, long min, long mainBet, long max)
        {
            var others = sideStakes.Where((_, i) => i != index).Sum();
            var cap = Math.Max(0, Math.Min(mainBet, max - mainBet - others));
            var next = sideStakes[index] + min;
            sideStakes[index] = next > cap ? 0 : next;
        }

        // When the main bet or the chips change, the first side bet keeps what still fits and the
        // second gets what is left.
        private void ClampSideStakes(long mainBet, long max)
        {
            var left = max - mainBet;
            for (var i = 0; i < sideStakes.Length; i++)
            {
                sideStakes[i] = Math.Max(0, Math.Min(sideStakes[i], Math.Min(mainBet, left)));
                left -= sideStakes[i];
            }
        }

        private string BetLine(string typed, long min, long max)
        {
            var title = "[bold yellow]PLACE YOUR BET[/]";
            if (typed.Length == 0)
            {
                return $"{title}  [bold]◀ {bet} ▶[/]  {screen.Key(KeyAction.BetDown)}/{screen.Key(KeyAction.BetUp)} -/+{min} or type it"
                    + $"{SideBetKeyHint()} · {screen.Key(KeyAction.Deal)} deal";
            }

            var value = long.Parse(typed);
            var problem = value < min ? $"  [red]the minimum is {min}[/]" : value > max ? $"  [red]you have {max}[/]" : "";
            return $"{title}  [bold]{typed}_[/]{problem}  [grey]Backspace · Esc[/] · {screen.Key(KeyAction.Deal)} deal";
        }

        private string SideBetKeyHint() => sideStakes.Length switch
        {
            0 => "",
            1 => $" · {screen.Key(KeyAction.SideBet1)} side bet",
            _ => $" · {screen.Key(KeyAction.SideBet1)}/{screen.Key(KeyAction.SideBet2)} side bets"
        };

        private string BetCommands() =>
            $"[grey]{screen.Key(KeyAction.FastForward)} autopilot · {screen.Key(KeyAction.NextShoe)} next shoe · "
            + $"{screen.Key(KeyAction.CashOut)} cash out ·[/] {screen.Toggles()}";

        private string InsuranceLine(Hand hand)
        {
            var hint = screen.ShowBook ? "   [green]book: no[/]" : "";
            return $"[bold yellow]INSURANCE[/]  Dealer shows an ace. Insurance for {TableScreen.Money(hand.Bet / 2)} chips?{hint}";
        }

        private string InsuranceCommands()
        {
            var no = screen.ShowBook ? $"[bold green]{screen.Key(KeyAction.Deal)} NO[/]" : $"{screen.Key(KeyAction.Deal)} no";
            return $"{screen.Key(KeyAction.Insurance)} yes · {no} · {screen.Toggles()}";
        }

        private string MoveLine(Hand hand, Card dealerUpcard, Move advice)
        {
            var hint = screen.ShowBook ? $"   [green]book: {Verb(advice)}[/]" : "";
            return $"[bold yellow]YOUR MOVE[/]  {Describe(hand)} against {Describe(dealerUpcard)}{screen.HandTag(hand)}{hint}";
        }

        private string MoveCommands(MoveOptions options, Move advice)
        {
            string Option(KeyAction action, Move move) =>
                screen.ShowBook && move == advice
                    ? $"[bold green]{screen.Key(action)} {Verb(move).ToUpperInvariant()}[/]"
                    : $"{screen.Key(action)} {Verb(move)}";

            var choices = new List<string>();
            if (options.CanHit)
            {
                choices.Add(Option(KeyAction.Hit, Move.Hit));
            }

            choices.Add(Option(KeyAction.Stand, Move.Stand));
            if (options.CanDouble)
            {
                choices.Add(Option(KeyAction.Double, Move.Double));
            }

            if (options.CanSplit)
            {
                choices.Add(Option(KeyAction.Split, Move.Split));
            }

            return $"{string.Join(" · ", choices)} · {screen.Toggles()}";
        }

        private static string Verb(Move move) => move switch
        {
            Move.Hit => "hit",
            Move.Stand => "stand",
            Move.Double => "double",
            _ => "split"
        };

        private static string Describe(Hand hand)
        {
            if (hand.Cards.Count == 2 && hand.Cards[0].PointValue == hand.Cards[1].PointValue)
            {
                return $"a pair of {TableScreen.PairName(hand)}";
            }

            return hand.IsSoft ? $"soft {hand.Value}" : $"hard {hand.Value}";
        }

        private static string Describe(Card upcard) =>
            upcard.Rank == Rank.Ace ? "an ace" : upcard.PointValue == 8 ? "an 8" : $"a {upcard.PointValue}";
    }
}
