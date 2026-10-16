using blackjack.Domain;

namespace blackjack.Engine
{
    // One table, one round at a time. Game is passive: the caller asks the current player's
    // strategy for a move and then calls Hit() or Stand(). The dealer plays automatically once
    // every player is done.
    //
    // Round flow: PlaceBet() for each player -> Deal() -> Hit()/Stand() while Phase is PlayerTurn
    // -> Phase becomes Settled -> NewRound().
    public class Game
    {
        public const int MaxPlayers = 7;

        private readonly Shoe shoe;
        private readonly List<Player> players;
        private readonly Rules rules;
        private readonly List<(Player Player, Hand Hand)> turns = new();
        private Hand dealerHand = new();
        private int turn;
        private bool holeCardFromOldShoe;

        public Game(Rules rules, IEnumerable<Player> players, Shoe? shoe = null)
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
        }

        // Raised for every card the table can see, after it lands in its hand.
        // The dealer's hole card is raised when it is turned over, not when it is dealt, and not at
        // all if the shoe was reshuffled in between: the new shoe still holds a copy of it.
        public event Action<Card>? CardRevealed;

        // Raised after the shoe is reshuffled, so counts can reset.
        public event Action? Shuffled;

        public Rules Rules => rules;
        public Phase Phase { get; private set; } = Phase.Betting;
        public IReadOnlyList<Player> Players => players;
        public Hand DealerHand => dealerHand;
        public bool HoleCardRevealed { get; private set; }
        public Card? DealerUpcard => dealerHand.Cards.Count > 0 ? dealerHand.Cards[0] : null;
        public Player? CurrentPlayer => Phase == Phase.PlayerTurn ? turns[turn].Player : null;
        public Hand? CurrentHand => Phase == Phase.PlayerTurn ? turns[turn].Hand : null;
        public int CardsRemaining => shoe.CardsRemaining;
        public int ShoeNumber => shoe.ShoeNumber;
        public decimal DecksRemaining => shoe.CardsRemaining / (decimal)Shoe.CardsPerDeck;

        public void PlaceBet(Player player, decimal amount)
        {
            RequirePhase(Phase.Betting);
            if (!players.Contains(player))
            {
                throw new ArgumentException($"{player.Name} isn't seated at this table.", nameof(player));
            }

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

            // The dealer peeks: a dealer natural ends the round for everyone.
            if (dealerHand.IsNatural)
            {
                RevealHoleCard();
                foreach (var (player, hand) in turns)
                {
                    Settle(player, hand, hand.IsNatural ? Outcome.Push : Outcome.Lose);
                }

                EndRound();
                return;
            }

            // A player natural settles for that player alone.
            foreach (var (player, hand) in turns.Where(t => t.Hand.IsNatural))
            {
                Settle(player, hand, Outcome.Blackjack);
            }

            Phase = Phase.PlayerTurn;
            turn = -1;
            Advance();
        }

        public void Hit()
        {
            RequirePhase(Phase.PlayerTurn);

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

        // Clears the table after a settled round so bets can be placed again.
        public void NewRound()
        {
            RequirePhase(Phase.Settled);

            foreach (var player in players)
            {
                player.Hands.Clear();
            }

            turns.Clear();
            dealerHand = new Hand();
            HoleCardRevealed = false;
            holeCardFromOldShoe = false;
            Phase = Phase.Betting;
        }

        // Players can leave between rounds, but not with a bet on the table.
        public void RemovePlayer(Player player)
        {
            if (Phase is Phase.PlayerTurn or Phase.DealerTurn || (Phase == Phase.Betting && player.Hands.Count > 0))
            {
                throw new InvalidOperationException($"{player.Name} can't leave in the middle of a round.");
            }

            players.Remove(player);
        }

        private void Advance()
        {
            do
            {
                turn++;
            }
            while (turn < turns.Count && turns[turn].Hand.Outcome is not null);

            if (turn >= turns.Count)
            {
                PlayDealer();
            }
        }

        // The dealer always turns over the hole card, but only draws if a hand is still live.
        private void PlayDealer()
        {
            Phase = Phase.DealerTurn;
            RevealHoleCard();

            var live = turns.Where(t => t.Hand.Outcome is null).ToList();
            if (live.Count > 0)
            {
                while (DealerShouldHit())
                {
                    DealTo(dealerHand, faceUp: true);
                }

                foreach (var (player, hand) in live)
                {
                    Settle(player, hand, Compare(hand));
                }
            }

            EndRound();
        }

        private bool DealerShouldHit() =>
            dealerHand.Value < 17 || (dealerHand.Value == 17 && dealerHand.IsSoft && rules.DealerHitsSoft17);

        private Outcome Compare(Hand hand)
        {
            if (dealerHand.IsBust || hand.Value > dealerHand.Value)
            {
                return Outcome.Win;
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

        private void RequirePhase(Phase expected)
        {
            if (Phase != expected)
            {
                throw new InvalidOperationException($"That can't be done during {Phase}; it needs {expected}.");
            }
        }
    }
}
