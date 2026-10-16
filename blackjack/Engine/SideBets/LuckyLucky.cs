using blackjack.Domain;

namespace blackjack.Engine.SideBets
{
    // The player's two cards and the dealer's upcard total 19, 20 or 21, with 7-7-7 and 6-7-8 on top.
    public sealed class LuckyLucky : ISideBet
    {
        public string Name => "Lucky Lucky";
        public string Description => "suited 7-7-7 200, suited 6-7-8 100, 7-7-7 50, 6-7-8 30, suited 21 15, 21 3, 20 or 19 2";
        public SideBetTiming Timing => SideBetTiming.OnDeal;

        public SideBetResult Evaluate(IReadOnlyList<Card> playerCards, IReadOnlyList<Card> dealerCards)
        {
            var cards = new[] { playerCards[0], playerCards[1], dealerCards[0] };
            var suited = cards.All(c => c.Suit == cards[0].Suit);
            var ranks = cards.Select(c => c.Rank).OrderBy(r => r).ToArray();

            if (ranks.All(r => r == Rank.Seven))
            {
                return suited ? new SideBetResult(200m, "suited 7-7-7") : new SideBetResult(50m, "7-7-7");
            }

            if (ranks[0] == Rank.Six && ranks[1] == Rank.Seven && ranks[2] == Rank.Eight)
            {
                return suited ? new SideBetResult(100m, "suited 6-7-8") : new SideBetResult(30m, "6-7-8");
            }

            var hand = new Hand();
            foreach (var card in cards)
            {
                hand.Add(card);
            }

            return hand.Value switch
            {
                21 => suited ? new SideBetResult(15m, "suited 21") : new SideBetResult(3m, "21"),
                20 => new SideBetResult(2m, "20"),
                19 => new SideBetResult(2m, "19"),
                _ => new SideBetResult(0m, "no 19, 20 or 21")
            };
        }
    }
}
