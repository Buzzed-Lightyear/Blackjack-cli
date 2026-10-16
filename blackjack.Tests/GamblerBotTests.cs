using blackjack.Engine;
using blackjack.Strategies;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    public class GamblerBotTests
    {
        private static readonly MoveOptions Anything = new(CanDouble: true, CanSplit: true);
        private static readonly MoveOptions HitOrStand = new(CanDouble: false, CanSplit: false);
        private static readonly TableContext Table = Tables.Fresh();

        // new Random(14) opens with a 0 from both Next(8) and Next(4), so a fresh bot on it starts
        // with a hunch; new Random(0) opens with neither.
        private const int HunchSeed = 14;
        private const int BookSeed = 0;

        private static GamblerBot NewBot(int seed) => new(new Rules(), new Random(seed));

        [Fact]
        public void DecideBet_DoublesAfterALoss_AndGoesBackToTheMinimumAfterAnythingElse()
        {
            var bot = NewBot(BookSeed);

            Assert.Equal(10m, bot.DecideBet(1000m, Table));  // 990 left in the stack
            Assert.Equal(20m, bot.DecideBet(990m, Table));   // lost: nothing came back
            Assert.Equal(40m, bot.DecideBet(970m, Table));   // lost again
            Assert.Equal(10m, bot.DecideBet(1010m, Table));  // the 40 won
            Assert.Equal(10m, bot.DecideBet(1010m, Table));  // pushed
            Assert.Equal(20m, bot.DecideBet(1000m, Table));  // lost
            Assert.Equal(10m, bot.DecideBet(1000m, Table));  // split, won one hand and lost the other: no loss
            Assert.Equal(20m, bot.DecideBet(995m, Table));   // pushed, but 5 of insurance went: a loss
            Assert.Equal(10m, bot.DecideBet(1035m, Table));  // doubled and won
        }

        [Fact]
        public void DecideBet_IsCappedByTheChips_AndSitsOutUnderTheMinimum()
        {
            var bot = NewBot(BookSeed);

            Assert.Equal(10m, bot.DecideBet(100m, Table));
            Assert.Equal(20m, bot.DecideBet(90m, Table));
            Assert.Equal(40m, bot.DecideBet(70m, Table));
            Assert.Equal(30m, bot.DecideBet(30m, Table));  // 80 wanted, 30 left
            Assert.Equal(0m, bot.DecideBet(0m, Table));
            Assert.Equal(0m, bot.DecideBet(9m, Table));
            Assert.Equal(10m, bot.DecideBet(50m, Table));  // a fresh stack starts over
            Assert.Equal(25m, NewBot(BookSeed).DecideBet(1000m, Tables.Fresh(new Rules { MinimumBet = 25m })));
        }

        [Fact]
        public void DecideMove_GoesWithAHunch_OnHardTwelveToSixteen()
        {
            // 16 against a ten: the book hits, the hunch stands. 13 against a 6: the other way round.
            Assert.Equal(Move.Stand, NewBot(HunchSeed).DecideMove(HandOf("10h 6c"), C("10s"), HitOrStand, Table));
            Assert.Equal(Move.Hit, NewBot(HunchSeed).DecideMove(HandOf("10h 3c"), C("6s"), HitOrStand, Table));
            Assert.Equal(Move.Hit, NewBot(BookSeed).DecideMove(HandOf("10h 6c"), C("10s"), HitOrStand, Table));
            Assert.Equal(Move.Stand, NewBot(BookSeed).DecideMove(HandOf("10h 3c"), C("6s"), HitOrStand, Table));
        }

        // No hunch is drawn off hard 12 to 16, so even the hunch seed plays the book there.
        [Theory]
        [InlineData("6h 5c", "6s", Move.Double)]
        [InlineData("10h 7c", "10s", Move.Stand)]
        [InlineData("5h 3c", "10s", Move.Hit)]
        [InlineData("Ah 5c", "10s", Move.Hit)]    // soft 16
        [InlineData("Ah 7c", "3s", Move.Double)]
        [InlineData("8h 8c", "10s", Move.Split)]  // a pair of 8s is split, not played as 16
        [InlineData("6h 6c", "3s", Move.Split)]
        public void DecideMove_FollowsTheBook_OffHardTwelveToSixteen(string hand, string upcard, Move expected)
        {
            Assert.Equal(expected, NewBot(HunchSeed).DecideMove(HandOf(hand), C(upcard), Anything, Table));
        }

        [Fact]
        public void DecideInsurance_IsAHunchToo()
        {
            Assert.True(NewBot(HunchSeed).DecideInsurance(HandOf("10h 6c"), Table));
            Assert.False(NewBot(BookSeed).DecideInsurance(HandOf("10h 6c"), Table));
        }

        [Fact]
        public void TwoBots_FromTheSameSeed_MakeTheSameChoices()
        {
            var book = new BasicStrategyBot();
            var hands = new[] { "10h 6c", "10h 2c", "9h 4c", "10h 5c", "8h 6c", "7h 5c 2d", "10h 3c", "9h 7c" };

            // Both walk the same hard hands against a ten and a 6 in turn, where the book hits and
            // stands respectively, so every move draws a hunch.
            List<(Move Move, bool Insures)> Play(int seed)
            {
                var bot = NewBot(seed);
                return Enumerable.Range(0, 40)
                    .Select(i => (bot.DecideMove(HandOf(hands[i % hands.Length]), C(i % 2 == 0 ? "10s" : "6s"), HitOrStand, Table), bot.DecideInsurance(HandOf(hands[i % hands.Length]), Table)))
                    .ToList();
            }

            var choices = Play(7);
            var deviations = choices.Where((c, i) => c.Move != book.DecideMove(HandOf(hands[i % hands.Length]), C(i % 2 == 0 ? "10s" : "6s"), HitOrStand, Table)).Count();

            Assert.Equal(choices, Play(7));
            Assert.InRange(deviations, 1, 20);
            Assert.Contains(choices, c => c.Insures);
            Assert.NotEqual(choices, Play(8));
        }
    }
}
