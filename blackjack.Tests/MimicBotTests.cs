using blackjack.Engine;
using blackjack.Strategies;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    public class MimicBotTests
    {
        private static readonly MoveOptions Anything = new(CanDouble: true, CanSplit: true);

        private readonly MimicBot bot = new(new Rules());

        [Theory]
        [InlineData("10h 6c", "2s", Move.Hit)]
        [InlineData("10h 6c", "10s", Move.Hit)]
        [InlineData("2h 3c", "6s", Move.Hit)]
        [InlineData("10h 7c", "As", Move.Stand)]
        [InlineData("10h 7c", "6s", Move.Stand)]
        [InlineData("Ah 6c", "6s", Move.Stand)]   // soft 17 stands under S17
        [InlineData("Ah 7c", "9s", Move.Stand)]
        [InlineData("6h 5c", "6s", Move.Hit)]     // never doubles
        [InlineData("8h 8c", "6s", Move.Hit)]     // never splits
        [InlineData("Ah Ac", "6s", Move.Hit)]     // not even aces
        [InlineData("10h 10c", "6s", Move.Stand)]
        public void DecideMove_PlaysLikeTheDealer(string hand, string upcard, Move expected)
        {
            Assert.Equal(expected, bot.DecideMove(HandOf(hand), C(upcard), Anything, Tables.Fresh()));
        }

        [Fact]
        public void DecideMove_HitsSoft17_OnlyWhenTheDealerDoes()
        {
            var h17 = new MimicBot(new Rules { DealerHitsSoft17 = true });

            Assert.Equal(Move.Hit, h17.DecideMove(HandOf("Ah 6c"), C("6s"), Anything, Tables.Fresh()));
            Assert.Equal(Move.Stand, h17.DecideMove(HandOf("Ah 7c"), C("6s"), Anything, Tables.Fresh()));
            Assert.Equal(Move.Stand, h17.DecideMove(HandOf("Ah 6c 10d"), C("6s"), Anything, Tables.Fresh())); // hard 17
            Assert.Equal(Move.Stand, bot.DecideMove(HandOf("Ah 6c"), C("6s"), Anything, Tables.Fresh()));
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
