using blackjack.Domain;

namespace blackjack.Engine.SideBets
{
    // The player's two cards and the dealer's upcard as a three-card poker hand.
    public sealed class TwentyOnePlusThree : ISideBet
    {
        public string Name => "21+3";
        public string Description => "suited trips 100, straight flush 40, trips 30, straight 10, flush 5";
        public SideBetTiming Timing => SideBetTiming.OnDeal;

        public SideBetResult Evaluate(IReadOnlyList<Card> playerCards, IReadOnlyList<Card> dealerCards)
        {
            var cards = new[] { playerCards[0], playerCards[1], dealerCards[0] };
            var ranks = cards.Select(c => (int)c.Rank).OrderBy(r => r).ToArray();
            var suited = cards.All(c => c.Suit == cards[0].Suit);
            var trips = ranks[0] == ranks[2];
            var straight = IsStraight(ranks);

            if (trips && suited)
            {
                return new SideBetResult(100m, "suited three of a kind");
            }

            if (straight && suited)
            {
                return new SideBetResult(40m, "straight flush");
            }

            if (trips)
            {
                return new SideBetResult(30m, "three of a kind");
            }

            if (straight)
            {
                return new SideBetResult(10m, "straight");
            }

            return suited ? new SideBetResult(5m, "flush") : new SideBetResult(0m, "no hand");
        }

        // An ace is high or low: Q-K-A and A-2-3 both count.
        private static bool IsStraight(int[] sorted) =>
            (sorted[0] != sorted[1] && sorted[1] != sorted[2] && sorted[2] - sorted[0] == 2)
            || (sorted[0] == (int)Rank.Two && sorted[1] == (int)Rank.Three && sorted[2] == (int)Rank.Ace);
    }
}
