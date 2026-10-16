using blackjack.Domain;
using blackjack.Engine;

namespace blackjack.Strategies
{
    // Plays its hand the way the dealer plays theirs: hits below 17, and soft 17 too when the
    // dealer does; never doubles, splits or insures. Bets the minimum.
    public sealed class MimicBot : IPlayerStrategy
    {
        private readonly Rules rules;

        public MimicBot(Rules rules)
        {
            ArgumentNullException.ThrowIfNull(rules);
            this.rules = rules;
        }

        public decimal DecideBet(decimal chips, TableContext table) =>
            chips >= table.Rules.MinimumBet ? table.Rules.MinimumBet : 0m;

        public bool DecideInsurance(Hand hand, TableContext table) => false;

        public Move DecideMove(Hand hand, Card dealerUpcard, MoveOptions options, TableContext table)
        {
            var hits = hand.Value < 17 || (hand.Value == 17 && hand.IsSoft && rules.DealerHitsSoft17);
            return options.CanHit && hits ? Move.Hit : Move.Stand;
        }
    }
}
