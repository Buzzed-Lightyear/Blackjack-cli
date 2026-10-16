using blackjack.Domain;

namespace blackjack.Strategies
{
    // Never busts: draws only while one more card can't go over 21 (hard 11 or less, soft 17 or
    // less), stands on everything else, and never doubles, splits or insures. Bets the minimum.
    public sealed class CautiousBot : IPlayerStrategy
    {
        public decimal DecideBet(decimal chips, TableContext table) =>
            chips >= table.Rules.MinimumBet ? table.Rules.MinimumBet : 0m;

        public bool DecideInsurance(Hand hand, TableContext table) => false;

        public Move DecideMove(Hand hand, Card dealerUpcard, MoveOptions options, TableContext table) =>
            options.CanHit && (hand.IsSoft ? hand.Value <= 17 : hand.Value <= 11) ? Move.Hit : Move.Stand;
    }
}
