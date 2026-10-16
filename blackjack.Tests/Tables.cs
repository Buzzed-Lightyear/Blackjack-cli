using blackjack.Engine;
using blackjack.Strategies;

namespace blackjack.Tests
{
    // Table contexts for strategy tests: a fresh shoe, or one whose count has reached a value.
    internal static class Tables
    {
        public static TableContext Fresh(Rules? rules = null) => At(0m, rules);

        // The running count is whatever gives this true count with the whole shoe still to deal.
        public static TableContext At(decimal trueCount, Rules? rules = null)
        {
            rules ??= new Rules();
            var decks = rules.DeckCount;
            return new TableContext(rules, (int)(trueCount * decks), trueCount, decks);
        }
    }
}
