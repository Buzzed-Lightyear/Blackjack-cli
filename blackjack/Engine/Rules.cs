namespace blackjack.Engine
{
    public class Rules
    {
        public int DeckCount { get; set; } = 6;

        // Paid on top of the stake: 1.5 is 3:2, 1.2 is 6:5.
        public decimal BlackjackPayout { get; set; } = 1.5m;
        public bool DealerHitsSoft17 { get; set; }

        // Fraction of the shoe dealt before the cut card comes out.
        public decimal Penetration { get; set; } = 0.75m;
        public decimal MinimumBet { get; set; } = 10m;

        // False limits doubling to hard 9, 10 and 11.
        public bool DoubleOnAnyTwo { get; set; } = true;
        public bool DoubleAfterSplit { get; set; } = true;

        // How many hands a player can hold after splitting; 1 turns splitting off.
        public int MaxSplitHands { get; set; } = 4;
        public bool ResplitAces { get; set; }

        // Split aces normally get one card each and stand.
        public bool HitSplitAces { get; set; }

        // A two-card 21 after a split is an ordinary 21 unless this is false, when it is paid
        // like a blackjack if it beats the dealer.
        public bool SplitBlackjackPaysEven { get; set; } = true;

        public void Validate()
        {
            if (DeckCount < 1)
            {
                throw new InvalidOperationException("A shoe needs at least one deck.");
            }

            if (BlackjackPayout <= 0m)
            {
                throw new InvalidOperationException("The blackjack payout must be positive.");
            }

            if (Penetration <= 0m || Penetration > 1m)
            {
                throw new InvalidOperationException("Penetration must be above 0 and at most 1.");
            }

            if (MinimumBet <= 0m)
            {
                throw new InvalidOperationException("The minimum bet must be positive.");
            }

            if (MaxSplitHands < 1)
            {
                throw new InvalidOperationException("A player must be able to hold at least one hand.");
            }
        }
    }
}
