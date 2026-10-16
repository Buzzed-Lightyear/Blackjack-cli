using blackjack.Engine;
using blackjack.Strategies;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    public class CounterBotTests
    {
        private static readonly MoveOptions Anything = new(CanDouble: true, CanSplit: true);
        private static readonly MoveOptions HitOrStand = new(CanDouble: false, CanSplit: false);

        private readonly CounterBot bot = new(new Rules());

        [Theory]
        [InlineData(-3, 10)]
        [InlineData(1, 10)]
        [InlineData(1.9, 10)]
        [InlineData(2, 20)]
        [InlineData(2.9, 20)]
        [InlineData(3, 40)]
        [InlineData(4, 80)]
        [InlineData(5, 120)]
        [InlineData(7, 120)]
        public void DecideBet_SpreadsByTheTrueCount_InUnitsOfTheMinimum(double trueCount, int bet)
        {
            Assert.Equal(bet, bot.DecideBet(1000m, Tables.At((decimal)trueCount)));
        }

        [Fact]
        public void DecideBet_IsCappedByTheChips_AndSitsOutOneUnitShort()
        {
            Assert.Equal(50m, bot.DecideBet(50m, Tables.At(5m)));
            Assert.Equal(10m, bot.DecideBet(10m, Tables.At(3m)));
            Assert.Equal(0m, bot.DecideBet(9m, Tables.At(5m)));
            Assert.Equal(100m, bot.DecideBet(1000m, Tables.At(3m, new Rules { MinimumBet = 25m })));
        }

        [Fact]
        public void DecideInsurance_TakesIt_FromATrueCountOfThree()
        {
            var hand = HandOf("10h 6c");

            Assert.True(bot.DecideInsurance(hand, Tables.At(3m)));
            Assert.True(bot.DecideInsurance(hand, Tables.At(5m)));
            Assert.False(bot.DecideInsurance(hand, Tables.At(2m)));
            Assert.False(bot.DecideInsurance(hand, Tables.At(2.9m)));
            Assert.False(bot.DecideInsurance(HandOf("Ah Kc"), Tables.Fresh()));
        }

        [Theory]
        // 16 against a ten stands from 0, with any number of cards.
        [InlineData("10h 6c", "10s", 0, Move.Stand)]
        [InlineData("10h 6c", "Ks", -0.5, Move.Hit)]
        [InlineData("5h 4c 7d", "10s", 1, Move.Stand)]
        // 15 against a ten stands from +4.
        [InlineData("10h 5c", "10s", 4, Move.Stand)]
        [InlineData("10h 5c", "10s", 3.9, Move.Hit)]
        // 12 against a 3 stands from +2, and against a 2 from +3.
        [InlineData("10h 2c", "3s", 2, Move.Stand)]
        [InlineData("10h 2c", "3s", 1, Move.Hit)]
        [InlineData("10h 2c", "2s", 3, Move.Stand)]
        [InlineData("10h 2c", "2s", 2, Move.Hit)]
        // 13 against a 2 hits below -1, and 12 against a 4 below 0.
        [InlineData("10h 3c", "2s", -1, Move.Stand)]
        [InlineData("10h 3c", "2s", -2, Move.Hit)]
        [InlineData("10h 2c", "4s", 0, Move.Stand)]
        [InlineData("10h 2c", "4s", -1, Move.Hit)]
        // 10 against a ten doubles from +4, 11 against an ace from +1, 9 against a 2 from +1 and
        // against a 7 from +3.
        [InlineData("6h 4c", "10s", 4, Move.Double)]
        [InlineData("6h 4c", "10s", 3, Move.Hit)]
        [InlineData("6h 5c", "As", 1, Move.Double)]
        [InlineData("6h 5c", "As", 0, Move.Hit)]
        [InlineData("5h 4c", "2s", 1, Move.Double)]
        [InlineData("5h 4c", "2s", 0, Move.Hit)]
        [InlineData("5h 4c", "7s", 3, Move.Double)]
        [InlineData("5h 4c", "7s", 2, Move.Hit)]
        public void DecideMove_PlaysTheIndexes(string hand, string upcard, double trueCount, Move expected)
        {
            Assert.Equal(expected, bot.DecideMove(HandOf(hand), C(upcard), Anything, Tables.At((decimal)trueCount)));
        }

        [Fact]
        public void DecideMove_IndexDoubles_NeedTheTableToAllowThem()
        {
            Assert.Equal(Move.Hit, bot.DecideMove(HandOf("6h 4c"), C("10s"), HitOrStand, Tables.At(5m)));
            Assert.Equal(Move.Hit, bot.DecideMove(HandOf("6h 5c"), C("As"), HitOrStand, Tables.At(2m)));
            Assert.Equal(Move.Hit, bot.DecideMove(HandOf("5h 4c"), C("7s"), HitOrStand, Tables.At(4m)));
            Assert.Equal(Move.Hit, bot.DecideMove(HandOf("5h 3c 2d"), C("10s"), Anything, Tables.At(5m))); // three cards
        }

        [Theory]
        [InlineData("10h 6c", "7s", 5, Move.Hit)]      // 16 against a 7 has no index
        [InlineData("10h 3c", "6s", -4, Move.Stand)]   // nor does 13 against a 6
        [InlineData("Ah 5c", "10s", 5, Move.Hit)]      // soft 16 is not a 16
        [InlineData("Ah 7c", "3s", -3, Move.Double)]   // soft doubles stay
        [InlineData("8h 8c", "10s", 3, Move.Split)]    // a pair splits before 16 against a ten stands
        [InlineData("6h 6c", "2s", 5, Move.Split)]
        [InlineData("6h 5c", "6s", -5, Move.Double)]
        [InlineData("10h 7c", "As", -5, Move.Stand)]
        public void DecideMove_FollowsTheBook_OffTheIndexes(string hand, string upcard, double trueCount, Move expected)
        {
            Assert.Equal(expected, bot.DecideMove(HandOf(hand), C(upcard), Anything, Tables.At((decimal)trueCount)));
        }

        [Fact]
        public void DecideMove_StandsOnSplitAcesThatCannotBeHit()
        {
            var done = new MoveOptions(CanDouble: false, CanSplit: false, CanHit: false);

            Assert.Equal(Move.Stand, bot.DecideMove(HandOf("Ah 5c"), C("10s"), done, Tables.At(5m)));
        }
    }
}
