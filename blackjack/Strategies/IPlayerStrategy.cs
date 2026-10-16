using blackjack.Domain;

namespace blackjack.Strategies
{
    public interface IPlayerStrategy
    {
        // The stake for the next round, or 0 to sit it out. The table gives the minimum and the count.
        decimal DecideBet(decimal chips, TableContext table);

        // Asked when the dealer shows an ace; insurance costs half the hand's bet.
        bool DecideInsurance(Hand hand, TableContext table);

        // Double and Split are only valid when options allow them.
        Move DecideMove(Hand hand, Card dealerUpcard, MoveOptions options, TableContext table);
    }
}
