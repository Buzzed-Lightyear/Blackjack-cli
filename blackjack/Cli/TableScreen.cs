using System.Diagnostics;
using blackjack.Domain;
using blackjack.Engine;
using blackjack.Engine.SideBets;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace blackjack.Cli
{
    internal enum BarTone
    {
        Waiting,
        Input,
        Alert
    }

    // The whole table on one screen, top to bottom: header, dealer, the other seats in seat order,
    // your seat beside the count, the latest events, and a bar that always says what the keys do
    // right now. Every region keeps its size, so nothing moves between frames, and inside a live
    // display each frame is drawn over the last instead of clearing the screen.
    internal sealed class TableScreen
    {
        private const int CountWidth = 26;
        private const int InfoWidth = 22;
        private const int MinHeight = 16;
        private const int MaxHeight = 34;
        private const int BigLayoutHeight = 23;
        private const int MaxEvents = 40;
        private static readonly TimeSpan WaitSlice = TimeSpan.FromMilliseconds(15);
        private static readonly KeyAction[] SideBetKeys = { KeyAction.SideBet1, KeyAction.SideBet2 };

        private readonly IAnsiConsole console;
        private readonly KeyBindings keys;
        private readonly FastForward fastForward;
        private readonly List<string> events = new();
        private LiveDisplayContext? live;
        private Game game = null!;
        private Player human = null!;
        private CardCounter counter = null!;
        private SessionStats stats = null!;
        private (string Line1, string Line2, BarTone Tone)? bar;

        public TableScreen(IAnsiConsole console, KeyBindings keys, FastForward fastForward)
        {
            this.console = console;
            this.keys = keys;
            this.fastForward = fastForward;
        }

        public int Round { get; set; }
        public bool ShowCount { get; private set; } = true;
        public bool ShowBook { get; private set; } = true;

        // The stake shown in your seat while you choose it.
        public long? PendingBet { get; set; }

        // The side-bet stakes being chosen, one per bet the table offers, shown with their keys.
        public IReadOnlyList<long>? PendingSideBets { get; set; }

        public static string Money(decimal amount) => amount.ToString("0.##");

        public static string SignedMoney(decimal amount) =>
            amount > 0m ? $"[green]+{Money(amount)}[/]" : amount < 0m ? $"[red]-{Money(-amount)}[/]" : "[grey]±0[/]";

        public static string Name(Player player) => Markup.Escape(player.Name);

        public static string DescribeRules(Rules rules)
        {
            var decks = rules.DeckCount == 1 ? "1 deck" : $"{rules.DeckCount} decks";
            var soft17 = rules.DealerHitsSoft17 ? "H17" : "S17";
            return $"{decks} · {soft17} · pays {Ratio(rules.BlackjackPayout)} · min {Money(rules.MinimumBet)}";
        }

        // "(hand 2)" when the player holds several hands, otherwise nothing.
        public static string HandTag(Player player, Hand hand) =>
            player.Hands.Count > 1 ? $" (hand {player.Hands.IndexOf(hand) + 1})" : "";

        // What a pair is called when it is split: "8s", "tens", "aces".
        public static string PairName(Hand hand)
        {
            var card = hand.Cards[0];
            return card.Rank == Rank.Ace ? "aces" : card.PointValue == 10 ? "tens" : $"{card.PointValue}s";
        }

        public void Seat(Game game, Player human, CardCounter counter, SessionStats stats)
        {
            this.game = game;
            this.human = human;
            this.counter = counter;
            this.stats = stats;
        }

        public void Attach(LiveDisplayContext context) => live = context;

        public void Detach() => live = null;

        // Lines are markup; escape anything a player typed.
        public void Log(string markup)
        {
            events.Add(markup);
            if (events.Count > MaxEvents)
            {
                events.RemoveAt(0);
            }
        }

        public void ClearLog() => events.Clear();

        public void SetBar(string line1, string line2, BarTone tone) => bar = (line1, line2, tone);

        public void ClearBar() => bar = null;

        public string Key(KeyAction action) => $"[bold]{Markup.Escape(keys.Label(action))}[/]";

        public string HandTag(Hand hand) => HandTag(human, hand);

        public string Toggles() =>
            $"[grey]{Key(KeyAction.Book)} book {OnOff(ShowBook)} · {Key(KeyAction.Count)} count {OnOff(ShowCount)}[/]";

        public void Refresh()
        {
            var frame = Build();
            if (live is null)
            {
                console.Write(frame);
                return;
            }

            live.UpdateTarget(frame);
            live.Refresh();
        }

        public ConsoleKeyInfo ReadKey()
        {
            Refresh();
            return console.Input.ReadKey(intercept: true)
                ?? throw new InvalidOperationException("No console input is available.");
        }

        public bool HandleToggle(ConsoleKeyInfo key)
        {
            if (keys.Is(KeyAction.Book, key))
            {
                ShowBook = !ShowBook;
                return true;
            }

            if (keys.Is(KeyAction.Count, key))
            {
                ShowCount = !ShowCount;
                return true;
            }

            return false;
        }

        // Leaves the table on screen for a while. Toggles still work, and on autopilot keys change its
        // speed or stop it. Anything else is dropped, so a key pressed while the table animates can't
        // answer the next question by accident. On autopilot this always checks the keys once, even
        // with no time to wait, so it can be stopped at full speed; keys after the one that stops it
        // are left for the next question.
        public void Wait(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero && !fastForward.IsActive)
            {
                return;
            }

            var clock = Stopwatch.StartNew();
            while (true)
            {
                var changed = false;
                while (!(fastForward.IsActive && fastForward.StopRequested)
                    && console.Input.IsKeyAvailable()
                    && console.Input.ReadKey(intercept: true) is { } key)
                {
                    if (HandleToggle(key))
                    {
                        changed = true;
                    }
                    else if (fastForward.IsActive)
                    {
                        fastForward.HandleKey(key, keys);
                        changed = true;
                    }
                }

                if (changed)
                {
                    Refresh();
                }

                var left = duration - clock.Elapsed;
                if (left <= TimeSpan.Zero)
                {
                    return;
                }

                Thread.Sleep(left < WaitSlice ? left : WaitSlice);
            }
        }

        public IRenderable Build()
        {
            var height = Math.Clamp(console.Profile.Height - 1, MinHeight, MaxHeight);
            var big = height >= BigLayoutHeight;
            var (dealerRows, seatRows, youRows) = big ? (5, 4, 7) : (2, 3, 5);
            var eventRows = height - 1 - dealerRows - seatRows - youRows - 4;

            var root = new Layout("table").SplitRows(
                new Layout("header", Header()).Size(1),
                new Layout("dealer", Dealer(big)).Size(dealerRows),
                new Layout("seats", Seats()).Size(seatRows),
                new Layout("you").Size(youRows),
                new Layout("events", Events(eventRows)).Size(eventRows),
                new Layout("bar", Bar()).Size(4));
            root["you"].SplitColumns(
                new Layout("seat", YourSeat(big, youRows - 2)),
                new Layout("count", Count()).Size(CountWidth));

            return new FixedHeight(root, height);
        }

        private IRenderable Header()
        {
            var round = Round > 0 ? $"round {Round} · " : "";
            var onTable = human.Hands.Where(h => h.Outcome is null).Sum(h => h.Bet)
                + human.SideBets.Where(s => !s.IsResolved).Sum(s => s.Amount)
                + (game.Phase == Phase.Insurance ? human.Insurance : 0m);
            var net = human.Chips + onTable - stats.StartingChips;

            return new Grid()
                .AddColumn(new GridColumn().NoWrap())
                .AddColumn(new GridColumn().NoWrap().RightAligned())
                .AddRow(
                    new Markup($"[bold green]♠ BLACKJACK[/]  [grey]{round}{Markup.Escape(DescribeRules(game.Rules))}[/]"),
                    new Markup($"chips [bold]{Money(human.Chips)}[/] {SignedMoney(net)}"));
        }

        private IRenderable Dealer(bool big)
        {
            var hand = game.DealerHand;
            var label = game.Phase == Phase.DealerTurn ? "[black on yellow] DEALER [/]" : "[bold]DEALER[/]";
            var total = DealerTotal();

            if (!big)
            {
                var chips = hand.Cards.Count == 0
                    ? DealerWaiting()
                    : string.Join(" ", hand.Cards.Select((c, i) => i == 1 && !game.HoleCardRevealed ? CardArt.HiddenChip : CardArt.Chip(c)));
                return new Markup($"{label}  {chips}  {total}").Centered();
            }

            IRenderable cards = hand.Cards.Count == 0
                ? new Markup($"\n{DealerWaiting()}\n")
                : new Markup(CardArt.Row(hand.Cards.Select((c, i) => i == 1 && !game.HoleCardRevealed ? CardArt.Back() : CardArt.Tile(c))));

            return new Rows(new Markup(label).Centered(), Align.Center(cards), new Markup(total).Centered());
        }

        private string DealerWaiting() =>
            game.Players.Any(p => p.Hands.Count > 0) ? "[grey]dealing…[/]" : "[grey]waiting for bets[/]";

        private string DealerTotal()
        {
            var hand = game.DealerHand;
            if (hand.Cards.Count == 0)
            {
                return "";
            }

            if (!game.HoleCardRevealed)
            {
                var up = hand.Cards[0];
                return $"[grey]showing {(up.Rank == Rank.Ace ? "an ace" : up.PointValue.ToString())}[/]";
            }

            if (hand.IsNatural)
            {
                return "[bold red]BLACKJACK[/]";
            }

            return hand.IsBust ? $"[bold green]BUST {hand.Value}[/]" : $"[bold]{Total(hand)}[/]";
        }

        // The other seats in the order they act, with a marker where you sit.
        private IRenderable Seats()
        {
            var seats = game.Players;
            var width = Math.Max(8, console.Profile.Width / seats.Count - 2);
            var grid = new Grid();
            foreach (var _ in seats)
            {
                grid.AddColumn(new GridColumn().Width(width).NoWrap().PadRight(2));
            }

            grid.AddRow(seats.Select(SeatName).ToArray());
            grid.AddRow(seats.Select(SeatCards).ToArray());
            grid.AddRow(seats.Select(SeatStatus).ToArray());
            return grid;
        }

        private IRenderable SeatName(Player seat)
        {
            var active = IsActing(seat);
            if (seat == human)
            {
                return new Markup(active ? "[black on yellow] ▼ YOU [/]" : "[bold aqua]▼ YOU[/]");
            }

            if (active)
            {
                return new Markup($"[black on yellow] ▶ {Name(seat)} [/]");
            }

            var done = seat.Hands.Count > 0 && seat.Hands.All(h => h.Outcome is not null || h.IsStood);
            return new Markup(done ? $"[grey]{Name(seat)}[/]" : $"[bold]{Name(seat)}[/]");
        }

        // Split hands sit side by side: 8♠ 3♦ | 8♥ 10♣.
        private IRenderable SeatCards(Player seat)
        {
            if (seat == human || seat.Hands.Count == 0)
            {
                return new Text("");
            }

            return new Markup(string.Join(" [grey]|[/] ", seat.Hands.Select(h => string.Join(" ", h.Cards.Select(CardArt.Chip)))));
        }

        private IRenderable SeatStatus(Player seat)
        {
            if (seat == human)
            {
                return new Text("");
            }

            if (seat.Hands.Count == 0)
            {
                return new Markup(game.Phase == Phase.Betting ? "" : "[grey]sitting out[/]");
            }

            return new Markup(string.Join(" [grey]|[/] ", seat.Hands.Select(HandStatus)));
        }

        private string HandStatus(Hand hand)
        {
            if (hand.Outcome is { } outcome)
            {
                return $"{Total(hand)}  {OutcomeShort(hand, outcome)}";
            }

            if (hand == game.CurrentHand)
            {
                return $"{Total(hand)}  [yellow]thinking…[/]";
            }

            return hand.IsStood ? $"{Total(hand)}  [grey]stood[/]" : Total(hand);
        }

        // One hand shows as tiles beside a column of figures. Several hands, or a hand too long
        // for tiles, go one per line with the figures underneath. rows is what the panel can show.
        private IRenderable YourSeat(bool big, int rows)
        {
            var hands = human.Hands;
            var (header, colour) = SeatHeader(hands);
            var room = (console.Profile.Width - CountWidth - InfoWidth - 6) / (CardArt.TileWidth + 1);
            var tiles = big && hands.Count == 1 && hands[0].Cards.Count <= room;

            IRenderable body;
            if (hands.Count == 0 || tiles || (hands.Count == 1 && hands[0].Cards.Count == 0))
            {
                var hand = hands.FirstOrDefault();
                IRenderable cards = hand is null || hand.Cards.Count == 0
                    ? new Markup(game.Phase == Phase.Betting && hand is null ? "[grey]place your bet[/]" : "")
                    : new Markup(CardArt.Row(hand.Cards.Select(CardArt.Tile)));

                var info = new List<string>();
                if (hand is not null && hand.Cards.Count > 0)
                {
                    info.Add(YourTotal(hand));
                }

                info.Add(PendingBet is { } pending
                    ? $"next bet [bold yellow]{pending}[/]"
                    : hand is not null ? $"bet [bold]{Money(hand.Bet)}[/]" : "");
                info.AddRange(SideBetLines(wide: false));
                info.Add($"[grey]chips {Money(human.Chips)}[/]");

                body = new Grid()
                    .AddColumn(new GridColumn())
                    .AddColumn(new GridColumn().Width(InfoWidth).NoWrap())
                    .AddRow(cards, new Markup(string.Join("\n", info.Take(rows))));
            }
            else
            {
                var lines = hands.Select(h => HandLine(h, hands.Count > 1)).ToList();
                if (PendingBet is { } pending)
                {
                    lines.Add($"next bet [bold yellow]{pending}[/]");
                }

                // The side bets share a line when it fits the panel.
                var sideBets = SideBetLines(wide: true).ToList();
                var together = string.Join(" · ", sideBets);
                if (sideBets.Count > 0)
                {
                    lines.AddRange(Markup.Remove(together).Length <= console.Profile.Width - CountWidth - 4 ? new[] { together } : sideBets);
                }

                lines.Add($"[grey]chips {Money(human.Chips)}[/]");
                body = new Markup(string.Join("\n", lines.Take(rows)));
            }

            return new Panel(body)
                .Header(header)
                .Border(colour == Color.Aqua ? BoxBorder.Rounded : BoxBorder.Double)
                .BorderColor(colour)
                .Expand();
        }

        private string HandLine(Hand hand, bool several)
        {
            var marker = !several ? "" : hand == game.CurrentHand ? "[bold yellow]▶[/] " : "  ";
            var cards = string.Join(" ", hand.Cards.Select(CardArt.Chip));
            var tail = hand.Outcome is { } outcome ? OutcomeLabel(hand, outcome).Text : $"bet [bold]{Money(hand.Bet)}[/]";
            return $"{marker}{cards}  {YourTotal(hand)}  {tail}";
        }

        // Your side bets: the stakes being chosen with their keys, or this round's stakes and how
        // they came out. The wide form has room for the winning hand's name.
        private IEnumerable<string> SideBetLines(bool wide)
        {
            if (PendingSideBets is { } pending)
            {
                for (var i = 0; i < Math.Min(pending.Count, game.SideBets.Count); i++)
                {
                    yield return $"{Key(SideBetKeys[i])} {Markup.Escape(game.SideBets[i].Name)} [bold yellow]{pending[i]}[/]";
                }

                yield break;
            }

            foreach (var stake in human.SideBets)
            {
                var name = Markup.Escape(stake.Bet.Name);
                if (stake.Result is not { } result)
                {
                    yield return $"{name} [bold]{Money(stake.Amount)}[/]";
                }
                else if (wide)
                {
                    yield return $"{name} [grey]{Markup.Escape(result.Label)}[/] {SignedMoney(stake.Net)}";
                }
                else
                {
                    yield return $"{name} {SignedMoney(stake.Net)}";
                }
            }
        }

        private (string Header, Color Colour) SeatHeader(List<Hand> hands)
        {
            if (hands.Count == 1 && hands[0].Outcome is { } outcome)
            {
                var (text, colour) = OutcomeLabel(hands[0], outcome);
                return ($" {text} ", colour);
            }

            if (hands.Count > 1 && hands.All(h => h.Outcome is not null))
            {
                var net = hands.Sum(h => h.Payout - h.Bet);
                return net switch
                {
                    > 0m => ($" [bold green]{hands.Count} HANDS +{Money(net)}[/] ", Color.Green),
                    < 0m => ($" [bold red]{hands.Count} HANDS -{Money(-net)}[/] ", Color.Red),
                    _ => ($" [bold yellow]{hands.Count} HANDS PUSH[/] ", Color.Yellow)
                };
            }

            if (game.Phase == Phase.Insurance && game.CurrentPlayer == human)
            {
                return (" [bold yellow]INSURANCE?[/] ", Color.Yellow);
            }

            if (game.CurrentHand is { } current && hands.Contains(current))
            {
                var which = hands.Count > 1 ? $" · HAND {hands.IndexOf(current) + 1} OF {hands.Count}" : "";
                return ($" [bold yellow]YOUR MOVE{which}[/] ", Color.Yellow);
            }

            return ($" [aqua]YOU · {Name(human)}[/] ", Color.Aqua);
        }

        private static (string Text, Color Colour) OutcomeLabel(Hand hand, Outcome outcome)
        {
            var net = Money(Math.Abs(hand.Payout - hand.Bet));
            return outcome switch
            {
                Outcome.Blackjack => ($"[bold green]BLACKJACK +{net}[/]", Color.Green),
                Outcome.Win => ($"[bold green]WIN +{net}[/]", Color.Green),
                Outcome.Push => ("[bold yellow]PUSH[/]", Color.Yellow),
                _ when hand.IsBust => ($"[bold red]BUST -{net}[/]", Color.Red),
                _ => ($"[bold red]LOSE -{net}[/]", Color.Red)
            };
        }

        private static string YourTotal(Hand hand)
        {
            if (hand.IsNatural)
            {
                return "[bold green]BLACKJACK[/]";
            }

            if (hand.IsBust)
            {
                return $"[bold red]BUST {hand.Value}[/]";
            }

            return $"[bold]{(hand.IsSoft && hand.Value < 21 ? $"soft {hand.Value}" : hand.Value.ToString())}[/]";
        }

        private IRenderable Count()
        {
            string body;
            if (!ShowCount)
            {
                body = $"[grey]hidden\n{Key(KeyAction.Count)} to show[/]";
            }
            else
            {
                var trueCount = counter.TrueCount(game.DecksRemaining);
                var colour = trueCount >= 2m ? "green" : trueCount <= -1m ? "red" : "white";
                body = $"running  [bold]{counter.RunningCount:+0;-0;0}[/]\n"
                    + $"true     [bold {colour}]{trueCount:+0.0;-0.0;0.0}[/]\n"
                    + $"decks    {game.DecksRemaining:0.0} left\n"
                    + ShoeBar();
            }

            return new Panel(new Markup(body)).Header(" Hi-Lo ").BorderColor(Color.Grey).Expand();
        }

        // How far through the shoe the deal is, with the cut card marked.
        private string ShoeBar()
        {
            const int width = 12;
            var total = game.Rules.DeckCount * Shoe.CardsPerDeck;
            var dealt = total - game.CardsRemaining;
            var filled = (int)Math.Round(width * (double)dealt / total);
            var cut = (int)Math.Round(width * (double)game.Rules.Penetration);
            var cells = Enumerable.Range(0, width).Select(i => i < filled ? '▓' : i == cut ? '│' : '░');
            return $"[grey]{string.Concat(cells)} {100 * dealt / total}%[/]";
        }

        private IRenderable Events(int rows)
        {
            var shown = events.TakeLast(Math.Max(rows, 0)).ToList();
            var lines = shown.Select((line, i) => i == shown.Count - 1 ? $"[bold]›[/] {line}" : $"  [grey]{line}[/]");
            return new Markup(string.Join("\n", lines));
        }

        private IRenderable Bar()
        {
            var (line1, line2, tone) = fastForward.IsActive ? AutopilotBar() : bar ?? WaitingBar();
            var colour = fastForward.IsActive ? Color.Aqua : tone switch
            {
                BarTone.Input => Color.Yellow,
                BarTone.Alert => Color.Red,
                _ => Color.Grey
            };

            return new Panel(new Markup($"{line1}\n{line2}")).Border(BoxBorder.Heavy).BorderColor(colour).Expand();
        }

        private (string, string, BarTone) WaitingBar()
        {
            var line = game.Phase switch
            {
                Phase.Insurance when game.CurrentPlayer is { } asked && asked != human => $"[grey]{Name(asked)} is offered insurance…[/]",
                Phase.PlayerTurn when game.CurrentPlayer is { } player && player != human => $"[grey]{Name(player)} is playing…[/]",
                Phase.DealerTurn => "[grey]Dealer's turn…[/]",
                Phase.Betting when game.Players.Any(p => p.Hands.Count > 0) => "[grey]Dealing…[/]",
                _ => ""
            };
            return (line, Toggles(), BarTone.Waiting);
        }

        private (string, string, BarTone) AutopilotBar()
        {
            var what = fastForward.SkippingShoes
                ? $"[bold aqua]▶▶ SKIPPING TO THE NEXT SHOE[/]{(fastForward.ShoesLeft > 1 ? $" [aqua]({fastForward.ShoesLeft} to go)[/]" : "")}"
                : "[bold aqua]▶▶ AUTOPILOT[/] [grey]the book plays your seat[/]";
            var line1 = $"{what}  ·  speed [bold]{fastForward.Speed.ToString().ToLowerInvariant()}[/]"
                + $"  ·  {fastForward.Hands} hands  ·  chips {SignedMoney(fastForward.Net)}";
            var line2 = $"[grey]{Key(KeyAction.Slower)} slower · {Key(KeyAction.Faster)} faster · "
                + $"{Key(KeyAction.NextShoe)} +1 shoe · any other key stops after this hand[/]";
            return (line1, line2, BarTone.Waiting);
        }

        private bool IsActing(Player seat) =>
            game.Phase is Phase.PlayerTurn or Phase.Insurance && game.CurrentPlayer == seat;

        private static string Total(Hand hand)
        {
            if (hand.Cards.Count == 0)
            {
                return "";
            }

            if (hand.IsNatural)
            {
                return "BJ";
            }

            if (hand.IsBust)
            {
                return $"[red]{hand.Value}[/]";
            }

            return hand.IsSoft && hand.Value < 21 ? $"soft {hand.Value}" : hand.Value.ToString();
        }

        private static string OutcomeShort(Hand hand, Outcome outcome)
        {
            var net = Money(Math.Abs(hand.Payout - hand.Bet));
            return outcome switch
            {
                Outcome.Blackjack => $"[green]+{net}[/]",
                Outcome.Win => $"[green]+{net}[/]",
                Outcome.Push => "[yellow]push[/]",
                _ when hand.IsBust => $"[red]bust -{net}[/]",
                _ => $"[red]-{net}[/]"
            };
        }

        private static string OnOff(bool on) => on ? "on" : "off";

        // 1.5 -> "3:2", 1.2 -> "6:5".
        private static string Ratio(decimal payout)
        {
            for (var d = 1; d <= 20; d++)
            {
                var n = payout * d;
                if (n == decimal.Truncate(n))
                {
                    return $"{n:0}:{d}";
                }
            }

            return $"{payout:0.##}:1";
        }

        // Renders its content at a set height, so a layout fills that many lines and no more.
        private sealed class FixedHeight : IRenderable
        {
            private readonly IRenderable inner;
            private readonly int height;

            public FixedHeight(IRenderable inner, int height)
            {
                this.inner = inner;
                this.height = height;
            }

            public Measurement Measure(RenderOptions options, int maxWidth) => inner.Measure(options, maxWidth);

            public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
                inner.Render(options with { Height = height }, maxWidth);
        }
    }
}
