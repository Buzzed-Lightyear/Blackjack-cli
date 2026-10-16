using blackjack.Domain;

namespace blackjack.Engine.SideBets
{
    // Pays when the dealer busts, more the longer it takes; a dealer blackjack loses.
    public sealed class BustIt : ISideBet
    {
        public string Name => "Bust It";
        public string Description => "dealer busts with 3-4 cards 2, 5 cards 4, 6 cards 12, 7 cards 50, 8+ cards 250";
        public SideBetTiming Timing => SideBetTiming.AfterDealer;

        public SideBetResult Evaluate(IReadOnlyList<Card> playerCards, IReadOnlyList<Card> dealerCards)
        {
            var dealer = new Hand();
            foreach (var card in dealerCards)
            {
                dealer.Add(card);
            }

            if (dealer.IsNatural)
            {
                return new SideBetResult(0m, "dealer blackjack");
            }

            if (!dealer.IsBust)
            {
                return new SideBetResult(0m, "no bust");
            }

            var count = dealerCards.Count;
            var pays = count switch
            {
                <= 4 => 2m,
                5 => 4m,
                6 => 12m,
                7 => 50m,
                _ => 250m
            };
            return new SideBetResult(pays, $"{count}-card bust");
        }
    }
}
