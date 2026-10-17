using System.Diagnostics;
using blackjack.Domain;
using blackjack.Engine;
using blackjack.Engine.SideBets;
using blackjack.Strategies;
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
    // right now, whose key hints can be clicked as well as typed. The frame fills the terminal,
    // TableLayout decides how, and every region keeps its size while the terminal does, so nothing
    // moves between frames; inside a live display each frame is drawn over the last instead of
    // clearing the screen. A resize is noticed while the table waits, and the next frame starts
    // from a cleared screen.
    internal sealed class TableScreen
    {
        private const int InfoWidth = 22;
        private const int MaxEvents = 40;
        private static readonly TimeSpan WaitSlice = TimeSpan.FromMilliseconds(15);
        private static readonly TimeSpan SizePoll = TimeSpan.FromMilliseconds(50);
        private static readonly KeyAction[] SideBetKeys = { KeyAction.SideBet1, KeyAction.SideBet2 };

        // The keys kept for typing a bet, which the bet line names as hints too.
        private static readonly KeyChord[] TypingKeys = new[] { "Enter", "Backspace", "Esc" }
            .Select(name => KeyChord.TryParse(name, out var chord) ? chord : throw new ArgumentException(name))
            .ToArray();

        private readonly IAnsiConsole console;
        private readonly KeyBindings keys;
        private readonly FastForward fastForward;
        private readonly List<string> events = new();
        private readonly Func<Player, string>[] seatRows;
        private LiveDisplayContext? live;
        private Game game = null!;
        private Player human = null!;
        private CardCounter counter = null!;
        private SessionStats stats = null!;
        private int seed;
        private (string Line1, string Line2, BarTone Tone)? bar;

        // The console size the last frame was built for.
        private (int Width, int Height)? drawn;

        // Where the last frame drew each key hint of the bar, as click targets.
        private readonly List<HintSpan> hints = new();

        // Keys read while checking for a sequence that turned out not to be one, to be read next.
        private readonly Queue<ConsoleKeyInfo> unread = new();

        public TableScreen(IAnsiConsole console, KeyBindings keys, FastForward fastForward)
        {
            this.console = console;
            this.keys = keys;
            this.fastForward = fastForward;
            seatRows = new Func<Player, string>[] { SeatName, SeatTag, SeatCards, SeatStatus };
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

        // A side bet's pay table on one grey line: "21+3: suited trips 100, straight flush 40, ...".
        public static string SideBetDescription(ISideBet bet) =>
            $"[grey]{Markup.Escape(bet.Name)}: {Markup.Escape(bet.Description)}[/]";

        // The brief form leaves out the payout and the minimum, for a header with no room for them.
        public static string DescribeRules(Rules rules, bool brief = false)
        {
            var decks = rules.DeckCount == 1 ? "1 deck" : $"{rules.DeckCount} decks";
            var soft17 = rules.DealerHitsSoft17 ? "H17" : "S17";
            return brief
                ? $"{decks} · {soft17}"
                : $"{decks} · {soft17} · pays {Ratio(rules.BlackjackPayout)} · min {Money(rules.MinimumBet)}";
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

        public void Seat(Game game, Player human, CardCounter counter, SessionStats stats, int seed)
        {
            this.game = game;
            this.human = human;
            this.counter = counter;
            this.stats = stats;
            this.seed = seed;
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

        // Phrased as what the key does, like every other hint in the bar.
        public string Toggles() =>
            $"[grey]{Key(KeyAction.Book)} {HideOrShow(ShowBook)} book · {Key(KeyAction.Count)} {HideOrShow(ShowCount)} count[/]";

        public void Refresh()
        {
            // A live display only redraws the lines of its last frame, so after a resize the rest of
            // the screen would keep pieces of the old one: the next frame starts from a clear screen.
            var resized = Resized;
            var frame = Build();
            if (resized)
            {
                console.Clear(true);
            }

            if (live is null)
            {
                console.Write(frame);
            }
            else
            {
                live.UpdateTarget(frame);
            }
        }

        // Draws the table, then waits for a key: one typed, or the one a clicked hint in the bar
        // stands for. Mouse events elsewhere are ignored and the wait goes on.
        public ConsoleKeyInfo ReadKey()
        {
            Refresh();
            while (true)
            {
                if (NextKey(AwaitKey) is { } key)
                {
                    return key;
                }
            }
        }

        // The key hint drawn at a cell of the last frame, if any; column and row are 0-based.
        public KeyChord? HintAt(int column, int row)
        {
            foreach (var hint in hints)
            {
                if (hint.Row == row && column >= hint.Start && column < hint.End)
                {
                    return hint.Chord;
                }
            }

            return null;
        }

        // Watches the terminal size while the key is awaited; the read is a task so that the wait
        // can be interrupted for a redraw, and the test console's completes at once.
        private ConsoleKeyInfo? AwaitKey()
        {
            var pending = console.Input.ReadKeyAsync(intercept: true, CancellationToken.None);
            while (Task.WaitAny(new Task[] { pending }, SizePoll) < 0)
            {
                if (Resized)
                {
                    Refresh();
                }
            }

            return pending.GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("No console input is available.");
        }

        private ConsoleKeyInfo? ReadKeyNow() => console.Input.ReadKey(intercept: true);

        // A key is waiting: one put back after a mouse check, or one at the console.
        private bool KeyAvailable => unread.Count > 0 || console.Input.IsKeyAvailable();

        // The next key as the game should see it, read with readRaw. A sequence the console passed
        // through is read to its end: a named key comes out as that key, a press on a hint in the
        // bar as the key the hint names, and any other mouse report as nothing at all (null). Keys
        // taken while checking for a sequence that wasn't one are kept, and come next.
        private ConsoleKeyInfo? NextKey(Func<ConsoleKeyInfo?> readRaw)
        {
            if (unread.Count > 0)
            {
                return unread.Dequeue();
            }

            if (readRaw() is not { } raw)
            {
                return null;
            }

            var taken = new List<ConsoleKeyInfo>();
            var decoded = TerminalInput.TryDecode(raw, console.Input, taken);
            foreach (var key in taken)
            {
                unread.Enqueue(key);
            }

            if (decoded is not { } sequence)
            {
                return raw;
            }

            if (sequence.Key is { } named)
            {
                return named;
            }

            return sequence.Click is { Pressed: true } click && HintAt(click.Column, click.Row) is { } chord ? chord.ToKeyInfo() : null;
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
                while (!(fastForward.IsActive && fastForward.StopRequested) && KeyAvailable)
                {
                    if (NextKey(ReadKeyNow) is not { } key)
                    {
                        continue;
                    }

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

                if (changed || Resized)
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

        // The size the last frame was built for is checked against the terminal's size now.
        private bool Resized =>
            drawn is { } last && (last.Width != console.Profile.Width || last.Height != console.Profile.Height);

        public IRenderable Build()
        {
            var width = console.Profile.Width;
            var height = console.Profile.Height;
            drawn = (width, height);
            hints.Clear();

            var layout = new TableLayout(width, height, game.Players.Count, seatRows.Length);
            if (layout.TooSmall)
            {
                return new FixedHeight(TooSmall(width, height), height);
            }

            var root = new Layout("table").SplitRows(
                new Layout("header", Header(width)).Size(TableLayout.HeaderRows),
                new Layout("dealer", Dealer(layout.TileHeight)).Size(layout.DealerRows),
                new Layout("seats", Seats(layout)).Size(layout.SeatRows * layout.SeatLines),
                new Layout("you").Size(layout.YouRows),
                new Layout("events", Events(layout.EventRows)).Size(layout.EventRows),
                new Layout("bar", Bar(width, height)).Size(TableLayout.BarRows));
            if (layout.CountPanel)
            {
                root["you"].SplitColumns(
                    new Layout("seat", YourSeat(layout)),
                    new Layout("count", Count(layout.PanelRows - 2)).Size(TableLayout.CountWidth));
            }
            else
            {
                root["you"].SplitRows(
                    new Layout("seat", YourSeat(layout)).Size(layout.PanelRows),
                    new Layout("count", CountLine()).Size(1));
            }

            return new FixedHeight(root, height);
        }

        // Shown in place of the table while the terminal can't hold one; keys are still read, so
        // the player can make it bigger or cash out, and the message says so.
        private IRenderable TooSmall(int width, int height) =>
            Align.Center(
                new Rows(
                    new Markup("[bold red]Terminal too small[/]"),
                    new Markup($"[grey]Blackjack needs {TableLayout.MinWidth}×{TableLayout.MinHeight}, this is {width}×{height}[/]"),
                    new Markup($"[grey]keys still work · {Key(KeyAction.CashOut)} twice between hands cashes out[/]")),
                VerticalAlignment.Middle);

        // The round and the rules on the left; the shoe, its seed and your chips on the right. The
        // rules make way for the rest as the terminal narrows: first the payout and the minimum
        // (which setup showed, and which never change), then the rules altogether.
        private IRenderable Header(int width)
        {
            var onTable = human.Hands.Where(h => h.Outcome is null).Sum(h => h.Bet)
                + human.SideBets.Where(s => !s.IsResolved).Sum(s => s.Amount)
                + (game.Phase == Phase.Insurance ? human.Insurance : 0m);
            var net = human.Chips + onTable - stats.StartingChips;
            var right = $"[grey]shoe {game.ShoeNumber} · seed {seed} ·[/] chips [bold]{Money(human.Chips)}[/] {SignedMoney(net)}";

            var room = width - Markup.Remove(right).Length - 2;
            var titles = new[] { Title(DescribeRules(game.Rules)), Title(DescribeRules(game.Rules, brief: true)), Title(null) };
            var left = titles.FirstOrDefault(title => Markup.Remove(title).Length <= room) ?? "[bold green]♠ BLACKJACK[/]";
            var gap = Math.Max(2, width - Markup.Remove(left).Length - Markup.Remove(right).Length);
            return new Line($"{left}{new string(' ', gap)}{right}");
        }

        private string Title(string? rules)
        {
            var parts = new[] { Round > 0 ? $"round {Round}" : null, rules is null ? null : Markup.Escape(rules) };
            return $"[bold green]♠ BLACKJACK[/]  [grey]{string.Join(" · ", parts.Where(p => p is not null))}[/]";
        }

        // tileHeight is 0 for one-line chips.
        private IRenderable Dealer(int tileHeight)
        {
            var hand = game.DealerHand;
            var label = game.Phase == Phase.DealerTurn ? "[black on yellow] DEALER [/]" : "[bold]DEALER[/]";
            var total = DealerTotal();

            if (tileHeight == 0)
            {
                var chips = hand.Cards.Count == 0
                    ? DealerWaiting()
                    : string.Join(" ", hand.Cards.Select((c, i) => i == 1 && !game.HoleCardRevealed ? CardArt.HiddenChip : CardArt.Chip(c)));
                return new Line($"{label}  {chips}  {total}", Justify.Center);
            }

            IRenderable cards = hand.Cards.Count == 0
                ? new Markup($"{new string('\n', (tileHeight - 1) / 2)}{DealerWaiting()}")
                : new Markup(CardArt.Row(hand.Cards.Select((c, i) => i == 1 && !game.HoleCardRevealed ? CardArt.Back(tileHeight) : CardArt.Tile(c, tileHeight))));

            return new Rows(new Line(label, Justify.Center), Align.Center(cards), new Line(total, Justify.Center));
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

        // The seats in the order they act, with a marker where you sit: one grid, or two when the
        // terminal is narrow, with the first half of the seats above the rest. Each grid row comes
        // from one of seatRows, so a row can be added under the names without touching the rest.
        private IRenderable Seats(TableLayout layout)
        {
            var seats = game.Players;
            if (!layout.TwoSeatRows)
            {
                return SeatGrid(seats, layout);
            }

            var half = (seats.Count + 1) / 2;
            return new Rows(SeatGrid(seats.Take(half).ToList(), layout), SeatGrid(seats.Skip(half).ToList(), layout));
        }

        private IRenderable SeatGrid(IReadOnlyList<Player> seats, TableLayout layout)
        {
            var width = Math.Max(8, console.Profile.Width / seats.Count - 2);
            var grid = new Grid();
            foreach (var _ in seats)
            {
                grid.AddColumn(new GridColumn().Width(width).NoWrap().PadRight(2));
            }

            foreach (var row in SeatRowsFor(layout))
            {
                grid.AddRow(seats.Select(seat => new Line(row(seat))).ToArray());
            }

            return grid;
        }

        private string SeatName(Player seat)
        {
            var active = IsActing(seat);
            if (seat == human)
            {
                return active ? "[black on yellow] ▼ YOU [/]" : "[bold aqua]▼ YOU[/]";
            }

            if (active)
            {
                return $"[black on yellow] ▶ {Name(seat)} [/]";
            }

            var done = seat.Hands.Count > 0 && seat.Hands.All(h => h.Outcome is not null || h.IsStood);
            return done ? $"[grey]{Name(seat)}[/]" : $"[bold]{Name(seat)}[/]";
        }

        // A bot's type under its name, with its stake while it has one; nothing under yours.
        private string SeatTag(Player seat)
        {
            if (seat == human || Bots.TypeOf(seat.Strategy) is not { } type)
            {
                return "";
            }

            var stake = seat.Hands.Sum(h => h.Bet);
            return stake > 0m ? $"[grey]{Bots.Tag(type)} · {Money(stake)}[/]" : $"[grey]{Bots.Tag(type)}[/]";
        }

        // The tag row is the one the layout drops when the terminal has no row for it.
        private IEnumerable<Func<Player, string>> SeatRowsFor(TableLayout layout) =>
            layout.SeatLines == seatRows.Length ? seatRows : seatRows.Where(row => row != SeatTag);

        // Split hands sit side by side: 8♠ 3♦ | 8♥ 10♣.
        private string SeatCards(Player seat)
        {
            if (seat == human || seat.Hands.Count == 0)
            {
                return "";
            }

            return string.Join(" [grey]|[/] ", seat.Hands.Select(h => string.Join(" ", h.Cards.Select(CardArt.Chip))));
        }

        private string SeatStatus(Player seat)
        {
            if (seat == human)
            {
                return "";
            }

            if (seat.Hands.Count == 0)
            {
                return game.Phase == Phase.Betting ? "" : "[grey]sitting out[/]";
            }

            return string.Join(" [grey]|[/] ", seat.Hands.Select(HandStatus));
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
        // for tiles, go one per line with the figures underneath; the panel shows what fits its rows.
        private IRenderable YourSeat(TableLayout layout)
        {
            var hands = human.Hands;
            var rows = layout.PanelRows - 2;
            var (header, colour) = SeatHeader(hands);
            var room = (layout.PanelWidth - InfoWidth - 6) / (CardArt.TileWidth + 1);
            var tiles = layout.TileHeight > 0 && hands.Count == 1 && hands[0].Cards.Count <= room;

            IRenderable body;
            if (hands.Count == 0 || tiles || (hands.Count == 1 && hands[0].Cards.Count == 0))
            {
                var hand = hands.FirstOrDefault();
                IRenderable cards = hand is null || hand.Cards.Count == 0
                    ? new Markup(game.Phase == Phase.Betting && hand is null ? "[grey]place your bet[/]" : "")
                    : new Markup(CardArt.Row(hand.Cards.Select(c => CardArt.Tile(c, layout.TileHeight))));

                var info = new List<string>();
                if (hand is not null && hand.Cards.Count > 0)
                {
                    info.Add(YourTotal(hand));
                }

                var bet = PendingBet is { } pending
                    ? $"next bet [bold yellow]{pending}[/]"
                    : hand is not null ? BetLabel(hand) : "";
                info.Add($"{bet}{InsuranceTag()}");
                info.AddRange(SideBetLines(wide: false));
                info.Add($"[grey]chips {Money(human.Chips)}[/]");

                body = new Grid()
                    .AddColumn(new GridColumn())
                    .AddColumn(new GridColumn().Width(InfoWidth).NoWrap())
                    .AddRow(cards, Lines(info.Take(rows)));
            }
            else
            {
                // Insurance was taken on the first hand, before any split.
                var lines = hands.Select((h, i) => $"{HandLine(h, hands.Count > 1)}{(i == 0 ? InsuranceTag() : "")}").ToList();
                if (PendingBet is { } pending)
                {
                    lines.Add($"next bet [bold yellow]{pending}[/]");
                }

                // The side bets share a line when it fits the panel.
                var sideBets = SideBetLines(wide: true).ToList();
                var together = string.Join(" · ", sideBets);
                if (sideBets.Count > 0)
                {
                    lines.AddRange(Markup.Remove(together).Length <= layout.PanelWidth - 4 ? new[] { together } : sideBets);
                }

                lines.Add($"[grey]chips {Money(human.Chips)}[/]");
                body = Lines(lines.Take(rows));
            }

            return new Panel(body)
                .Header(header)
                .Border(colour == Color.Aqua ? BoxBorder.Rounded : BoxBorder.Double)
                .BorderColor(colour)
                .Expand();
        }

        // A finished split hand says so while the round is still on, as a bot's would.
        private string HandLine(Hand hand, bool several)
        {
            var marker = !several ? "" : hand == game.CurrentHand ? "[bold yellow]▶[/] " : "  ";
            var cards = string.Join(" ", hand.Cards.Select(CardArt.Chip));
            var stood = several && hand.Outcome is null && hand.IsStood ? "[grey]stood[/]  " : "";
            var tail = hand.Outcome is { } outcome ? OutcomeLabel(hand, outcome).Text : BetLabel(hand);
            return $"{marker}{cards}  {YourTotal(hand)}  {stood}{tail}";
        }

        private static string BetLabel(Hand hand) => $"bet [bold]{Money(hand.Bet)}[/]{(hand.IsDoubled ? " (doubled)" : "")}";

        // " · ins 5" while the dealer has yet to peek, then " · ins +10" or " · ins -5" until the
        // next round.
        private string InsuranceTag()
        {
            if (human.Insurance == 0m)
            {
                return "";
            }

            return game.Phase == Phase.Insurance
                ? $" · ins [bold]{Money(human.Insurance)}[/]"
                : $" · ins {SignedMoney(human.InsurancePayout - human.Insurance)}";
        }

        // Your side bets: the stakes being chosen with their keys (each beside how the last round's
        // stake on that bet came out), or this round's stakes and how they came out. The wide form
        // has room for the winning hand's name.
        private IEnumerable<string> SideBetLines(bool wide)
        {
            if (PendingSideBets is { } pending)
            {
                for (var i = 0; i < Math.Min(pending.Count, game.SideBets.Count); i++)
                {
                    var bet = game.SideBets[i];
                    var last = human.SideBets.FirstOrDefault(s => s.Bet == bet && s.IsResolved) is { } settled
                        ? $" ({SignedMoney(settled.Net)})"
                        : "";
                    yield return $"{Key(SideBetKeys[i])} {Markup.Escape(bet.Name)} [bold yellow]{pending[i]}[/]{last}";
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
                return ($" {text}{RoundNet(hands)} ", colour);
            }

            if (hands.Count > 1 && hands.All(h => h.Outcome is not null))
            {
                var net = hands.Sum(h => h.Payout - h.Bet);
                var (text, colour) = net switch
                {
                    > 0m => ($"[bold green]{hands.Count} HANDS +{Money(net)}[/]", Color.Green),
                    < 0m => ($"[bold red]{hands.Count} HANDS -{Money(-net)}[/]", Color.Red),
                    _ => ($"[bold grey]{hands.Count} HANDS ±0[/]", Color.Grey)
                };
                return ($" {text}{RoundNet(hands)} ", colour);
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

        // " · ROUND +250" when insurance or a side bet made the round's net differ from the hands'.
        private string RoundNet(List<Hand> hands)
        {
            var net = human.RoundNet;
            return net == hands.Sum(h => h.Payout - h.Bet) ? "" : $" [bold]· ROUND {SignedMoney(net)}[/]";
        }

        // A push is grey, so that yellow means your turn and nothing else.
        private static (string Text, Color Colour) OutcomeLabel(Hand hand, Outcome outcome)
        {
            var net = Money(Math.Abs(hand.Payout - hand.Bet));
            return outcome switch
            {
                Outcome.Blackjack => ($"[bold green]BLACKJACK +{net}[/]", Color.Green),
                Outcome.Win => ($"[bold green]WIN +{net}[/]", Color.Green),
                Outcome.Push => ("[bold grey]PUSH ±0[/]", Color.Grey),
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

        // The count beside your seat; the shoe bar goes when the panel has no row for it.
        private IRenderable Count(int rows)
        {
            var lines = new List<string>();
            if (!ShowCount)
            {
                lines.Add("[grey]hidden[/]");
                lines.Add($"[grey]{Key(KeyAction.Count)} show count[/]");
            }
            else
            {
                lines.Add($"running  [bold]{counter.RunningCount:+0;-0;0}[/]");
                lines.Add($"true     [bold {TrueCountColour()}]{counter.TrueCount(game.DecksRemaining):+0.0;-0.0;0.0}[/]");
                lines.Add($"decks    {game.DecksRemaining:0.0} left");
                if (rows >= 4)
                {
                    lines.Add(ShoeBar());
                }
            }

            return new Panel(Lines(lines)).Header(" Hi-Lo ").BorderColor(Color.Grey).Expand();
        }

        // The count on one line under your seat, for a terminal too narrow for the panel.
        private IRenderable CountLine()
        {
            if (!ShowCount)
            {
                return new Line($"[grey]Hi-Lo hidden · {Key(KeyAction.Count)} show count[/]");
            }

            return new Line(
                $"[grey]Hi-Lo[/] running [bold]{counter.RunningCount:+0;-0;0}[/] · "
                + $"true [bold {TrueCountColour()}]{counter.TrueCount(game.DecksRemaining):+0.0;-0.0;0.0}[/] · "
                + $"{game.DecksRemaining:0.0} decks");
        }

        private string TrueCountColour()
        {
            var trueCount = counter.TrueCount(game.DecksRemaining);
            return trueCount >= 2m ? "green" : trueCount <= -1m ? "red" : "white";
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

        // The last events that fit, newest at the bottom.
        private IRenderable Events(int rows)
        {
            var shown = events.TakeLast(rows).ToList();
            return Lines(shown.Select((line, i) => i == shown.Count - 1 ? $"[bold]›[/] {line}" : $"  [grey]{line}[/]"));
        }

        // The bar's two lines sit on the third and second rows from the bottom, inside a border and
        // a column of padding.
        private IRenderable Bar(int width, int height)
        {
            var (line1, line2, tone) = fastForward.IsActive ? AutopilotBar() : bar ?? WaitingBar();
            var colour = fastForward.IsActive ? Color.Aqua : tone switch
            {
                BarTone.Input => Color.Yellow,
                BarTone.Alert => Color.Red,
                _ => Color.Grey
            };

            RegisterHints(line1, height - 3, width);
            RegisterHints(line2, height - 2, width);

            // An empty first line still takes its row, so the second stays where its hints are.
            var lines = Lines(new[] { line1.Length == 0 ? " " : line1, line2 });
            return new Panel(lines).Border(BoxBorder.Heavy).BorderColor(colour).Expand();
        }

        // Records the key hints of one bar line as click targets. A hint is a key label standing as
        // a word of its own, together with the words after it up to the next · separator, so "S
        // stand" and "X again to confirm" are each one target; the labels either side of a slash,
        // as in Q/E, are a target each of just the label. The line starts two columns in and is cut
        // with an ellipsis where it overflows the panel: nothing past the cut can be clicked, and a
        // label the cut runs through is left out.
        private void RegisterHints(string markup, int row, int width)
        {
            var text = Markup.Remove(markup);
            var room = width - 4;
            var visible = text.Length <= room ? text.Length : room - 1;
            var labels = HintLabels();
            var start = 0;
            while (start < text.Length)
            {
                var separator = text.IndexOf(" · ", start, StringComparison.Ordinal);
                var end = separator < 0 ? text.Length : separator;
                while (end > start && text[end - 1] == ' ')
                {
                    end--;
                }

                RegisterSegment(text, start, end, row, visible, labels);
                start = separator < 0 ? text.Length : separator + 3;
            }
        }

        private void RegisterSegment(string text, int start, int end, int row, int visible, Dictionary<string, KeyChord> labels)
        {
            var at = start;
            while (at < end)
            {
                if (text[at] == ' ')
                {
                    at++;
                    continue;
                }

                var wordEnd = text.IndexOf(' ', at, end - at);
                if (wordEnd < 0)
                {
                    wordEnd = end;
                }

                var word = text[at..wordEnd];
                if (labels.TryGetValue(word, out var chord))
                {
                    AddHint(row, at, end, chord, visible);
                    return;
                }

                var parts = word.Split('/');
                if (parts.Length > 1 && parts.All(labels.ContainsKey))
                {
                    var partStart = at;
                    foreach (var part in parts)
                    {
                        AddHint(row, partStart, partStart + part.Length, labels[part], visible);
                        partStart += part.Length + 1;
                    }
                }

                at = wordEnd;
            }
        }

        private void AddHint(int row, int start, int end, KeyChord chord, int visible)
        {
            if (start + chord.Label.Length <= visible)
            {
                hints.Add(new HintSpan(row, start + 2, Math.Min(end, visible) + 2, chord));
            }
        }

        // Every key a hint can stand for, by the label the bar shows it under.
        private Dictionary<string, KeyChord> HintLabels()
        {
            var labels = new Dictionary<string, KeyChord>(StringComparer.Ordinal);
            foreach (var chord in Enum.GetValues<KeyAction>().SelectMany(keys.Get).Concat(TypingKeys))
            {
                labels.TryAdd(chord.Label, chord);
            }

            return labels;
        }

        private static IRenderable Lines(IEnumerable<string> lines) => new Rows(lines.Select(line => new Line(line)));

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
            var line1 = $"{what} · speed [bold]{fastForward.Speed.ToString().ToLowerInvariant()}[/]"
                + $" · {fastForward.Hands} hands · chips {SignedMoney(fastForward.Net)}";
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
                Outcome.Push => "[grey]push[/]",
                _ when hand.IsBust => $"[red]bust -{net}[/]",
                _ => $"[red]-{net}[/]"
            };
        }

        private static string HideOrShow(bool shown) => shown ? "hide" : "show";

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

        // The cells of one bar row, Start up to but not including End, that stand for a key.
        private readonly record struct HintSpan(int Row, int Start, int End, KeyChord Chord);

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
