using blackjack.Engine;
using blackjack.Strategies;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    public class BasicStrategyBotTests
    {
        private static readonly MoveOptions Anything = new(CanDouble: true, CanSplit: true);
        private static readonly MoveOptions HitOrStand = new(CanDouble: false, CanSplit: false);

        private readonly BasicStrategyBot bot = new();

        [Theory]
        // Hard 17+: stand.
        [InlineData("10h 7c", "As", Move.Stand)]
        [InlineData("10h 9c", "10s", Move.Stand)]
        // Hard 13 to 16: stand against 2 to 6, otherwise hit.
        [InlineData("10h 3c", "2s", Move.Stand)]
        [InlineData("10h 6c", "6s", Move.Stand)]
        [InlineData("10h 6c", "7s", Move.Hit)]
        [InlineData("10h 3c", "As", Move.Hit)]
        [InlineData("5h 4c 7d", "Ks", Move.Hit)]
        // Hard 12: stand against 4 to 6, otherwise hit.
        [InlineData("10h 2c", "3s", Move.Hit)]
        [InlineData("10h 2c", "4s", Move.Stand)]
        [InlineData("10h 2c", "6s", Move.Stand)]
        [InlineData("10h 2c", "7s", Move.Hit)]
        // Hard 11: double against everything but an ace.
        [InlineData("6h 5c", "2s", Move.Double)]
        [InlineData("6h 5c", "10s", Move.Double)]
        [InlineData("6h 5c", "As", Move.Hit)]
        // Hard 10: double against 2 to 9.
        [InlineData("6h 4c", "2s", Move.Double)]
        [InlineData("6h 4c", "9s", Move.Double)]
        [InlineData("6h 4c", "10s", Move.Hit)]
        [InlineData("6h 4c", "As", Move.Hit)]
        // Hard 9: double against 3 to 6.
        [InlineData("5h 4c", "2s", Move.Hit)]
        [InlineData("5h 4c", "3s", Move.Double)]
        [InlineData("5h 4c", "6s", Move.Double)]
        [InlineData("5h 4c", "7s", Move.Hit)]
        // Hard 8 or less: hit.
        [InlineData("5h 3c", "6s", Move.Hit)]
        [InlineData("2h 3c", "4s", Move.Hit)]
        // Three cards never double.
        [InlineData("5h 4c 2d", "6s", Move.Hit)]
        [InlineData("Ah 2c 3d", "5s", Move.Hit)]
        // Soft 19+: stand.
        [InlineData("Ah 8c", "6s", Move.Stand)]
        [InlineData("Ah 8c", "10s", Move.Stand)]
        [InlineData("Ah 9c", "As", Move.Stand)]
        // Soft 18: double against 3 to 6, stand against 2, 7 and 8, hit against 9, 10 or ace.
        [InlineData("Ah 7c", "2s", Move.Stand)]
        [InlineData("Ah 7c", "3s", Move.Double)]
        [InlineData("Ah 7c", "6s", Move.Double)]
        [InlineData("Ah 7c", "7s", Move.Stand)]
        [InlineData("Ah 7c", "8s", Move.Stand)]
        [InlineData("Ah 7c", "9s", Move.Hit)]
        [InlineData("Ah 7c", "Qs", Move.Hit)]
        [InlineData("Ah 7c", "As", Move.Hit)]
        // Soft 17: double against 3 to 6.
        [InlineData("Ah 6c", "2s", Move.Hit)]
        [InlineData("Ah 6c", "3s", Move.Double)]
        [InlineData("Ah 6c", "6s", Move.Double)]
        [InlineData("Ah 6c", "7s", Move.Hit)]
        // Soft 15 and 16: double against 4 to 6.
        [InlineData("Ah 4c", "3s", Move.Hit)]
        [InlineData("Ah 4c", "4s", Move.Double)]
        [InlineData("Ah 5c", "6s", Move.Double)]
        [InlineData("Ah 5c", "7s", Move.Hit)]
        // Soft 13 and 14: double against 5 and 6.
        [InlineData("Ah 2c", "4s", Move.Hit)]
        [InlineData("Ah 2c", "5s", Move.Double)]
        [InlineData("Ah 3c", "6s", Move.Double)]
        [InlineData("Ah 3c", "7s", Move.Hit)]
        // An ace that has dropped to 1 makes the hand hard again.
        [InlineData("Ah 6c 10d", "10s", Move.Stand)]
        [InlineData("Ah 5c 10d", "10s", Move.Hit)]
        // Pairs, with double after split allowed.
        [InlineData("Ah Ac", "5s", Move.Split)]
        [InlineData("Ah Ac", "As", Move.Split)]
        [InlineData("8h 8c", "10s", Move.Split)]
        [InlineData("8h 8c", "As", Move.Split)]
        [InlineData("10h Kc", "6s", Move.Stand)]
        [InlineData("9h 9c", "2s", Move.Split)]
        [InlineData("9h 9c", "6s", Move.Split)]
        [InlineData("9h 9c", "7s", Move.Stand)]
        [InlineData("9h 9c", "8s", Move.Split)]
        [InlineData("9h 9c", "9s", Move.Split)]
        [InlineData("9h 9c", "10s", Move.Stand)]
        [InlineData("9h 9c", "As", Move.Stand)]
        [InlineData("7h 7c", "2s", Move.Split)]
        [InlineData("7h 7c", "7s", Move.Split)]
        [InlineData("7h 7c", "8s", Move.Hit)]
        [InlineData("6h 6c", "2s", Move.Split)]
        [InlineData("6h 6c", "6s", Move.Split)]
        [InlineData("6h 6c", "7s", Move.Hit)]
        [InlineData("5h 5c", "9s", Move.Double)]
        [InlineData("5h 5c", "10s", Move.Hit)]
        [InlineData("4h 4c", "4s", Move.Hit)]
        [InlineData("4h 4c", "5s", Move.Split)]
        [InlineData("4h 4c", "6s", Move.Split)]
        [InlineData("4h 4c", "7s", Move.Hit)]
        [InlineData("3h 3c", "2s", Move.Split)]
        [InlineData("3h 3c", "7s", Move.Split)]
        [InlineData("3h 3c", "8s", Move.Hit)]
        [InlineData("2h 2c", "2s", Move.Split)]
        [InlineData("2h 2c", "7s", Move.Split)]
        [InlineData("2h 2c", "8s", Move.Hit)]
        public void DecideMove_FollowsTheBook(string hand, string upcard, Move expected)
        {
            Assert.Equal(expected, bot.DecideMove(HandOf(hand), C(upcard), Anything));
        }

        [Theory]
        [InlineData("6h 5c", "As", Move.Double)]
        [InlineData("Ah 7c", "2s", Move.Double)]
        [InlineData("Ah 8c", "6s", Move.Double)]
        [InlineData("Ah 8c", "5s", Move.Stand)]
        [InlineData("Ah 8c", "7s", Move.Stand)]
        public void DecideMove_DoublesMore_WhenTheDealerHitsSoft17(string hand, string upcard, Move expected)
        {
            var h17 = new BasicStrategyBot(new Rules { DealerHitsSoft17 = true });

            Assert.Equal(expected, h17.DecideMove(HandOf(hand), C(upcard), Anything));
        }

        [Theory]
        [InlineData("2h 2c", "2s", Move.Hit)]
        [InlineData("2h 2c", "3s", Move.Hit)]
        [InlineData("2h 2c", "4s", Move.Split)]
        [InlineData("2h 2c", "7s", Move.Split)]
        [InlineData("3h 3c", "3s", Move.Hit)]
        [InlineData("3h 3c", "4s", Move.Split)]
        [InlineData("4h 4c", "5s", Move.Hit)]
        [InlineData("4h 4c", "6s", Move.Hit)]
        [InlineData("6h 6c", "2s", Move.Hit)]
        [InlineData("6h 6c", "3s", Move.Split)]
        [InlineData("8h 8c", "10s", Move.Split)]
        public void DecideMove_SplitsLess_WithoutDoubleAfterSplit(string hand, string upcard, Move expected)
        {
            var noDas = new BasicStrategyBot(new Rules { DoubleAfterSplit = false });

            Assert.Equal(expected, noDas.DecideMove(HandOf(hand), C(upcard), Anything));
        }

        [Theory]
        // Doubles fall back to a hit on hard 9 to 11 and soft 13 to 17, and to a stand on soft 18.
        [InlineData("5h 4c", "5s", Move.Hit)]
        [InlineData("6h 5c", "6s", Move.Hit)]
        [InlineData("Ah 2c", "5s", Move.Hit)]
        [InlineData("Ah 6c", "5s", Move.Hit)]
        [InlineData("Ah 7c", "5s", Move.Stand)]
        // Pairs that can't be split play as their total.
        [InlineData("Ah Ac", "6s", Move.Hit)]
        [InlineData("8h 8c", "10s", Move.Hit)]
        [InlineData("8h 8c", "6s", Move.Stand)]
        [InlineData("9h 9c", "6s", Move.Stand)]
        [InlineData("7h 7c", "7s", Move.Hit)]
        [InlineData("6h 6c", "5s", Move.Stand)]
        [InlineData("5h 5c", "9s", Move.Hit)]
        [InlineData("4h 4c", "5s", Move.Hit)]
        [InlineData("2h 2c", "3s", Move.Hit)]
        public void DecideMove_FallsBack_WhenTheTableAllowsOnlyHitOrStand(string hand, string upcard, Move expected)
        {
            Assert.Equal(expected, bot.DecideMove(HandOf(hand), C(upcard), HitOrStand));
        }

        [Fact]
        public void DecideMove_StillDoubles_WhenOnlySplittingIsOff()
        {
            var options = new MoveOptions(CanDouble: true, CanSplit: false);

            Assert.Equal(Move.Double, bot.DecideMove(HandOf("5h 5c"), C("9s"), options));
            Assert.Equal(Move.Hit, bot.DecideMove(HandOf("8h 8c"), C("10s"), options));
        }

        [Fact]
        public void DecideMove_FallsBackToAStand_OnSoft19_UnderH17()
        {
            var h17 = new BasicStrategyBot(new Rules { DealerHitsSoft17 = true });

            Assert.Equal(Move.Stand, h17.DecideMove(HandOf("Ah 8c"), C("6s"), HitOrStand));
        }

        [Fact]
        public void DecideMove_SplitsOrStands_OnSplitAcesThatCannotBeHit()
        {
            var again = new MoveOptions(CanDouble: false, CanSplit: true, CanHit: false);
            var done = new MoveOptions(CanDouble: false, CanSplit: false, CanHit: false);

            Assert.Equal(Move.Split, bot.DecideMove(HandOf("Ah Ac"), C("10s"), again));
            Assert.Equal(Move.Stand, bot.DecideMove(HandOf("Ah Ac"), C("10s"), done));
        }

        [Fact]
        public void DecideInsurance_IsAlwaysNo()
        {
            Assert.False(bot.DecideInsurance(HandOf("10h Kc")));
            Assert.False(bot.DecideInsurance(HandOf("10h 6c")));
        }

        [Fact]
        public void DecideBet_BetsTheMinimum_OrSitsOutWhenBroke()
        {
            Assert.Equal(10m, bot.DecideBet(500m, 10m));
            Assert.Equal(10m, bot.DecideBet(10m, 10m));
            Assert.Equal(0m, bot.DecideBet(9m, 10m));
        }
    }
}
