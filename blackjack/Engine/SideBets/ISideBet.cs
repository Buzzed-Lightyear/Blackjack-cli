using blackjack.Domain;

namespace blackjack.Engine.SideBets
{
    // A wager on the cards rather than on beating the dealer. Evaluate is pure: it gets the
    // player's first two cards and the dealer's cards the table can see at the time (the upcard
    // alone until the hole card is turned over) and says what the stake pays.
    public interface ISideBet
    {
        string Name { get; }

        // One line: what it pays.
        string Description { get; }

        SideBetTiming Timing { get; }

        SideBetResult Evaluate(IReadOnlyList<Card> playerCards, IReadOnlyList<Card> dealerCards);
    }
}
