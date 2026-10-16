using blackjack.Domain;
using blackjack.Engine.SideBets;

namespace blackjack.Engine
{
    // One table, one round at a time. Game is passive: the caller asks the current player's
    // strategy for a move and then calls Hit(), Stand(), Double() or Split(). The dealer plays
    // automatically once every player is done.
    //
    // Round flow: PlaceBet() and any PlaceSideBet() for each player -> Deal() -> when the dealer
    // shows an ace, TakeInsurance()/DeclineInsurance() for each player while Phase is Insurance
    // -> Hit()/Stand()/Double()/Split() while Phase is PlayerTurn -> Phase becomes Settled
    // -> NewRound().
    public class Game
    {
        public const int MaxPlayers = 7;

        private readonly Shoe shoe;
        private readonly List<Player> players;
        private readonly Rules rules;
        private readonly IReadOnlyList<ISideBet> sideBets;
        private readonly List<(Player Player, Hand Hand)> turns = new();
        private readonly Dictionary<Player, Card[]> dealtCards = new();
        private Hand dealerHand = new();
        private int turn;
        private bool holeCardFromOldShoe;

        public Game(Rules rules, IEnumerable<Player> players, Shoe? shoe = null, IReadOnlyList<ISideBet>? sideBets = null)
        {
            ArgumentNullException.ThrowIfNull(rules);
            ArgumentNullException.ThrowIfNull(players);
            rules.Validate();

            this.rules = rules;
            this.players = players.ToList();
            if (this.players.Count is 0 or > MaxPlayers)
            {
                throw new ArgumentException($"A table seats 1 to {MaxPlayers} players.", nameof(players));
            }

            if (this.players.Distinct().Count() != this.players.Count)
            {
                throw new ArgumentException("A player can only take one seat.", nameof(players));
            }

            this.shoe = shoe ?? new Shoe(rules.DeckCount, Random.Shared.Next(), rules.Penetration);
            this.sideBets = sideBets ?? Array.Empty<ISideBet>();
        }

        // Raised for every card the table can see, after it lands in its hand.
        // The dealer's hole card is raised when it is turned over, not when it is dealt, and not at
        // all if the shoe was reshuffled in between: the new shoe still holds a copy of it.
        public event Action<Card>? CardRevealed;

        // Raised after the shoe is reshuffled, so counts can reset.
        public event Action? Shuffled;

        public Rules Rules => rules;
        public IReadOnlyList<ISideBet> SideBets => sideBets;
        public Phase Phase { get; private set; } = Phase.Betting;
        public IReadOnlyList<Player> Players => players;
        public Hand DealerHand => dealerHand;
        public bool HoleCardRevealed { get; private set; }
        public Card? DealerUpcard => dealerHand.Cards.Count > 0 ? dealerHand.Cards[0] : null;

        // Who is being asked about insurance, or whose move it is.
        public Player? CurrentPlayer => Phase is Phase.PlayerTurn or Phase.Insurance ? Current?.Player : null;
        public Hand? CurrentHand => Phase == Phase.PlayerTurn ? Current?.Hand : null;

        // Only false for split aces that can be split again but not hit.
        public bool CanHit => CurrentHand is { } hand && CanHitHand(hand);
        public bool CanDouble => Current is { } turn && Phase == Phase.PlayerTurn && CanDoubleHand(turn.Player, turn.Hand);
        public bool CanSplit => Current is { } turn && Phase == Phase.PlayerTurn && CanSplitHand(turn.Player, turn.Hand);
        public int CardsRemaining => shoe.CardsRemaining;
        public int ShoeNumber => shoe.ShoeNumber;
        public decimal DecksRemaining => shoe.CardsRemaining / (decimal)Shoe.CardsPerDeck;

        public void PlaceBet(Player player, decimal amount)
        {
            RequirePhase(Phase.Betting);
            RequireSeated(player);

            if (player.Hands.Count > 0)
            {
                throw new InvalidOperationException($"{player.Name} has already bet this round.");
            }

            if (amount < rules.MinimumBet)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), $"The minimum bet is {rules.MinimumBet}.");
            }

            player.PlaceBet(amount);
        }

        // One stake per offered side bet, after the main bet and never larger than it.
        public void PlaceSideBet(Player player, ISideBet bet, decimal amount)
        {
            RequirePhase(Phase.Betting);
            RequireSeated(player);
            ArgumentNullException.ThrowIfNull(bet);

            if (!sideBets.Contains(bet))
            {
                throw new ArgumentException($"This table doesn't offer {bet.Name}.", nameof(bet));
            }

            if (player.Hands.Count == 0)
            {
                throw new InvalidOperationException($"{player.Name} needs a bet on the table before a side bet.");
            }

            if (player.SideBets.Any(s => s.Bet == bet))
            {
                throw new InvalidOperationException($"{player.Name} has already staked {bet.Name} this round.");
            }

            if (amount <= 0m || amount > player.Hands[0].Bet)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "A side bet is more than 0 and at most the main bet.");
            }

            player.PlaceSideBet(bet, amount);
        }

        public void Deal()
        {
            RequirePhase(Phase.Betting);

            turns.Clear();
            foreach (var player in players.Where(p => p.Hands.Count > 0))
            {
                turns.Add((player, player.Hands[0]));
            }

            if (turns.Count == 0)
            {
                throw new InvalidOperationException("No bets have been placed.");
            }

            // Two passes round the table. The dealer's second card is the hole card, dealt face down.
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var (_, hand) in turns)
                {
                    DealTo(hand, faceUp: true);
                }

                DealTo(dealerHand, faceUp: pass == 0);
            }

            // Side bets look at the two cards each player was dealt, even after a split moves one.
            dealtCards.Clear();
            foreach (var (player, hand) in turns)
            {
                dealtCards[player] = hand.Cards.ToArray();
            }

            // With an ace up, insurance is offered before the dealer peeks.
            if (DealerUpcard!.Rank == Rank.Ace)
            {
                Phase = Phase.Insurance;
                turn = -1;
                OfferInsurance();
            }
            else
            {
                Peek();
            }
        }

        // Insurance is half the main bet, and pays 2:1 if the dealer has a natural.
        public void TakeInsurance()
        {
            RequirePhase(Phase.Insurance);

            var (player, hand) = turns[turn];
            var stake = hand.Bet / 2;
            player.Stake(stake);
            player.Insurance = stake;
            OfferInsurance();
        }

        public void DeclineInsurance()
        {
            RequirePhase(Phase.Insurance);
            OfferInsurance();
        }

        public void Hit()
        {
            RequirePhase(Phase.PlayerTurn);
            if (!CanHit)
            {
                throw new InvalidOperationException("Split aces take one card each.");
            }

            var (player, hand) = turns[turn];
            DealTo(hand, faceUp: true);

            if (hand.IsBust)
            {
                Settle(player, hand, Outcome.Lose);
                Advance();
            }
            else if (hand.Value == 21)
            {
                // Nobody hits 21, so stand for them.
                hand.IsStood = true;
                Advance();
            }
        }

        public void Stand()
        {
            RequirePhase(Phase.PlayerTurn);

            turns[turn].Hand.IsStood = true;
            Advance();
        }

        // Doubles the bet for exactly one more card, after which the hand stands.
        public void Double()
        {
            RequirePhase(Phase.PlayerTurn);
            if (!CanDouble)
            {
                throw new InvalidOperationException("This hand can't be doubled.");
            }

            var (player, hand) = turns[turn];
            player.Stake(hand.Bet);
            hand.Bet *= 2;
            hand.IsDoubled = true;
            DealTo(hand, faceUp: true);

            if (hand.IsBust)
            {
                Settle(player, hand, Outcome.Lose);
            }
            else
            {
                hand.IsStood = true;
            }

            Advance();
        }

        // Moves the second card to a new hand with the same bet, played right after this one, and
        // gives each a card. Play goes on with this hand unless it is finished at once.
        public void Split()
        {
            RequirePhase(Phase.PlayerTurn);
            if (!CanSplit)
            {
                throw new InvalidOperationException("This hand can't be split.");
            }

            var (player, hand) = turns[turn];
            player.Stake(hand.Bet);
            var second = new Hand { Bet = hand.Bet, IsSplit = true };
            second.Add(hand.RemoveLast());
            hand.IsSplit = true;
            player.Hands.Insert(player.Hands.IndexOf(hand) + 1, second);
            turns.Insert(turn + 1, (player, second));

            DealTo(hand, faceUp: true);
            DealTo(second, faceUp: true);

            // A two-card 21 is not hit, and split aces take one card each unless they can be split
            // again, which is the only move left to them then.
            hand.IsStood = hand.Value == 21 || !HasMove(player, hand);
            second.IsStood = second.Value == 21 || !HasMove(player, second);
            if (hand.IsStood)
            {
                Advance();
            }
        }

        // Clears the table after a settled round so bets can be placed again.
        public void NewRound()
        {
            RequirePhase(Phase.Settled);

            foreach (var player in players)
            {
                player.ClearRound();
            }

            turns.Clear();
            dealtCards.Clear();
            dealerHand = new Hand();
            HoleCardRevealed = false;
            holeCardFromOldShoe = false;
            Phase = Phase.Betting;
        }

        // Players can leave between rounds, but not with a bet on the table.
        public void RemovePlayer(Player player)
        {
            if (Phase is Phase.Insurance or Phase.PlayerTurn or Phase.DealerTurn || (Phase == Phase.Betting && player.Hands.Count > 0))
            {
                throw new InvalidOperationException($"{player.Name} can't leave in the middle of a round.");
            }

            players.Remove(player);
        }

        // Nobody, for the moment the dealer peeks after the last player has answered on insurance.
        private (Player Player, Hand Hand)? Current => turn >= 0 && turn < turns.Count ? turns[turn] : null;

        private bool CanHitHand(Hand hand) => rules.HitSplitAces || !IsSplitAces(hand);

        private bool HasMove(Player player, Hand hand) => CanHitHand(hand) || CanSplitHand(player, hand);

        private bool CanDoubleHand(Player player, Hand hand)
        {
            if (hand.Cards.Count != 2 || player.Chips < hand.Bet)
            {
                return false;
            }

            if (!rules.DoubleOnAnyTwo && (hand.IsSoft || hand.Value is < 9 or > 11))
            {
                return false;
            }

            return !hand.IsSplit || (rules.DoubleAfterSplit && (!IsSplitAces(hand) || rules.HitSplitAces));
        }

        // Any two ten-value cards are a pair.
        private bool CanSplitHand(Player player, Hand hand) =>
            hand.Cards.Count == 2
            && hand.Cards[0].PointValue == hand.Cards[1].PointValue
            && player.Hands.Count < rules.MaxSplitHands
            && (!IsSplitAces(hand) || rules.ResplitAces)
            && player.Chips >= hand.Bet;

        // The first card of a split hand is the one kept from the pair.
        private static bool IsSplitAces(Hand hand) => hand.IsSplit && hand.Cards[0].Rank == Rank.Ace;

        private static bool IsSplitTwentyOne(Hand hand) => hand.IsSplit && hand.Cards.Count == 2 && hand.Value == 21;

        // Moves on to the next player who can afford insurance, or peeks once everyone has answered.
        private void OfferInsurance()
        {
            do
            {
                turn++;
            }
            while (turn < turns.Count && turns[turn].Player.Chips < turns[turn].Hand.Bet / 2);

            if (turn >= turns.Count)
            {
                Peek();
            }
        }

        // The dealer checks for a natural, which ends the round for everyone and pays insurance.
        private void Peek()
        {
            if (dealerHand.IsNatural)
            {
                RevealHoleCard();
                foreach (var (player, hand) in turns)
                {
                    if (player.Insurance > 0m)
                    {
                        player.InsurancePayout = player.Insurance * 3;
                        player.Receive(player.InsurancePayout);
                    }

                    Settle(player, hand, hand.IsNatural ? Outcome.Push : Outcome.Lose);
                }

                ResolveSideBets(SideBetTiming.OnDeal);
                ResolveSideBets(SideBetTiming.AfterDealer);
                EndRound();
                return;
            }

            ResolveSideBets(SideBetTiming.OnDeal);

            // A player natural settles for that player alone.
            foreach (var (player, hand) in turns.Where(t => t.Hand.IsNatural))
            {
                Settle(player, hand, Outcome.Blackjack);
            }

            Phase = Phase.PlayerTurn;
            turn = -1;
            Advance();
        }

        // Finds the next hand with a move to make; a split ace left without one stands.
        private void Advance()
        {
            while (++turn < turns.Count)
            {
                var (player, hand) = turns[turn];
                if (hand.Outcome is not null || hand.IsStood)
                {
                    continue;
                }

                if (HasMove(player, hand))
                {
                    return;
                }

                hand.IsStood = true;
            }

            PlayDealer();
        }

        // The dealer always turns over the hole card, but only draws if a hand is still live or a
        // side bet is waiting on the final hand.
        private void PlayDealer()
        {
            Phase = Phase.DealerTurn;
            RevealHoleCard();

            var live = turns.Where(t => t.Hand.Outcome is null).ToList();
            if (live.Count > 0 || HasUnresolvedSideBets(SideBetTiming.AfterDealer))
            {
                while (DealerShouldHit())
                {
                    DealTo(dealerHand, faceUp: true);
                }
            }

            foreach (var (player, hand) in live)
            {
                Settle(player, hand, Compare(hand));
            }

            ResolveSideBets(SideBetTiming.AfterDealer);
            EndRound();
        }

        private bool DealerShouldHit() =>
            dealerHand.Value < 17 || (dealerHand.Value == 17 && dealerHand.IsSoft && rules.DealerHitsSoft17);

        private Outcome Compare(Hand hand)
        {
            if (dealerHand.IsBust || hand.Value > dealerHand.Value)
            {
                return IsSplitTwentyOne(hand) && !rules.SplitBlackjackPaysEven ? Outcome.Blackjack : Outcome.Win;
            }

            return hand.Value == dealerHand.Value ? Outcome.Push : Outcome.Lose;
        }

        // Bets come off the player's chips when placed, so the payout includes the returned stake.
        private void Settle(Player player, Hand hand, Outcome outcome)
        {
            hand.Outcome = outcome;
            hand.Payout = outcome switch
            {
                Outcome.Blackjack => hand.Bet + hand.Bet * rules.BlackjackPayout,
                Outcome.Win => hand.Bet * 2,
                Outcome.Push => hand.Bet,
                _ => 0m
            };

            if (hand.Payout > 0m)
            {
                player.Receive(hand.Payout);
            }
        }

        private bool HasUnresolvedSideBets(SideBetTiming timing) =>
            players.Any(p => p.SideBets.Any(s => s.Bet.Timing == timing && !s.IsResolved));

        // Side bets see the two cards each player was dealt and whatever of the dealer's hand is face up.
        private void ResolveSideBets(SideBetTiming timing)
        {
            var dealerCards = HoleCardRevealed ? dealerHand.Cards : dealerHand.Cards.Take(1).ToList();
            foreach (var player in players.Where(dealtCards.ContainsKey))
            {
                foreach (var stake in player.SideBets.Where(s => s.Bet.Timing == timing && !s.IsResolved))
                {
                    var result = stake.Bet.Evaluate(dealtCards[player], dealerCards);
                    stake.Result = result;
                    stake.Payout = result.Wins ? stake.Amount + stake.Amount * result.Pays : 0m;
                    if (stake.Payout > 0m)
                    {
                        player.Receive(stake.Payout);
                    }
                }
            }
        }

        // The cut card is checked between rounds, so counts reset before the next bets.
        private void EndRound()
        {
            Phase = Phase.Settled;
            if (shoe.NeedsReshuffle)
            {
                Reshuffle();
            }
        }

        private void DealTo(Hand hand, bool faceUp)
        {
            // A full table can outrun a short shoe mid-round.
            if (shoe.CardsRemaining == 0)
            {
                Reshuffle();
            }

            var card = shoe.Deal();
            hand.Add(card);
            if (faceUp)
            {
                CardRevealed?.Invoke(card);
            }
        }

        private void RevealHoleCard()
        {
            if (HoleCardRevealed)
            {
                return;
            }

            HoleCardRevealed = true;
            if (!holeCardFromOldShoe)
            {
                CardRevealed?.Invoke(dealerHand.Cards[1]);
            }
        }

        private void Reshuffle()
        {
            shoe.Shuffle();

            // A hole card still face down came out of the old shoe, so counts of the new one skip it.
            holeCardFromOldShoe = dealerHand.Cards.Count >= 2 && !HoleCardRevealed;
            Shuffled?.Invoke();
        }

        private void RequireSeated(Player player)
        {
            if (!players.Contains(player))
            {
                throw new ArgumentException($"{player.Name} isn't seated at this table.", nameof(player));
            }
        }

        private void RequirePhase(Phase expected)
        {
            if (Phase != expected)
            {
                throw new InvalidOperationException($"That can't be done during {Phase}; it needs {expected}.");
            }
        }
    }
}
