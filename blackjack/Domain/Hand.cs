using blackjack.Engine;

namespace blackjack.Domain
{
    public class Hand
    {
        private readonly List<Card> cards = new();

        public IReadOnlyList<Card> Cards => cards;
        public decimal Bet { get; set; }
        public int Value { get; private set; }

        // True while an ace is still counted as 11.
        public bool IsSoft { get; private set; }
        public bool IsBust => Value > 21;

        // A 21 made from a split pair is not a natural.
        public bool IsNatural => cards.Count == 2 && Value == 21 && !IsSplit;

        // Set by Game as the round plays out.
        public bool IsSplit { get; internal set; }
        public bool IsDoubled { get; internal set; }
        public bool IsStood { get; internal set; }
        public Outcome? Outcome { get; internal set; }
        public decimal Payout { get; internal set; }

        public void Add(Card card)
        {
            cards.Add(card);
            Recount();
        }

        // Takes the last card back, for moving it to a new hand on a split.
        internal Card RemoveLast()
        {
            var card = cards[^1];
            cards.RemoveAt(cards.Count - 1);
            Recount();
            return card;
        }

        private void Recount()
        {
            var total = 0;
            var softAces = 0;
            foreach (var c in cards)
            {
                total += c.PointValue;
                if (c.Rank == Rank.Ace)
                {
                    softAces++;
                }
            }

            while (total > 21 && softAces > 0)
            {
                total -= 10;
                softAces--;
            }

            Value = total;
            IsSoft = softAces > 0;
        }
    }
}
