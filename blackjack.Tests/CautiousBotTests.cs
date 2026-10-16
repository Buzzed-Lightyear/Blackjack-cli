using blackjack.Strategies;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    public class CautiousBotTests
    {
        private static readonly MoveOptions Anything = new(CanDouble: true, CanSplit: true);

        private readonly CautiousBot bot = new();

        [Theory]
        [InlineData("6h 5c", "10s", Move.Hit)]   // hard 11
        [InlineData("2h 3c", "2s", Move.Hit)]
        [InlineData("Ah 6c", "6s", Move.Hit)]    // soft 17
        [InlineData("Ah Ac", "6s", Move.Hit)]    // soft 12, and no split
        [InlineData("10h 2c", "2s", Move.Stand)] // hard 12
        [InlineData("10h 6c", "10s", Move.Stand)]
        [InlineData("Ah 7c", "6s", Move.Stand)]  // soft 18
        [InlineData("6h 5c", "6s", Move.Hit)]    // never doubles
        [InlineData("5h 5c", "9s", Move.Hit)]
        [InlineData("8h 8c", "6s", Move.Stand)]  // never splits
        public void DecideMove_HitsOnlyWhatCannotBust(string hand, string upcard, Move expected)
        {
            Assert.Equal(expected, bot.DecideMove(HandOf(hand), C(upcard), Anything, Tables.Fresh()));
        }

        [Fact]
        public void DecideMove_StandsOnSplitAcesThatCannotBeHit()
        {
            var done = new MoveOptions(CanDouble: false, CanSplit: true, CanHit: false);

            Assert.Equal(Move.Stand, bot.DecideMove(HandOf("Ah Ac"), C("6s"), done, Tables.Fresh()));
        }

        [Fact]
        public void DecideBet_BetsTheMinimum_AndNeverInsures()
        {
            Assert.Equal(10m, bot.DecideBet(1000m, Tables.At(6m)));
            Assert.Equal(0m, bot.DecideBet(9m, Tables.Fresh()));
            Assert.False(bot.DecideInsurance(HandOf("10h 10c"), Tables.At(6m)));
        }
    }
}
