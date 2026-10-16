using blackjack.Domain;
using blackjack.Engine;
using blackjack.Strategies;

namespace blackjack.Tests
{
    public class BotsTests
    {
        private sealed class NotABot : IPlayerStrategy
        {
            public decimal DecideBet(decimal chips, TableContext table) => table.Rules.MinimumBet;

            public bool DecideInsurance(Hand hand, TableContext table) => false;

            public Move DecideMove(Hand hand, Card dealerUpcard, MoveOptions options, TableContext table) => Move.Stand;
        }

        [Theory]
        [InlineData(BotType.Basic, typeof(BasicStrategyBot), "book")]
        [InlineData(BotType.Counter, typeof(CounterBot), "count")]
        [InlineData(BotType.Cautious, typeof(CautiousBot), "safe")]
        [InlineData(BotType.Mimic, typeof(MimicBot), "mimic")]
        [InlineData(BotType.Gambler, typeof(GamblerBot), "gambler")]
        public void Create_BuildsTheType_WhichTypeOfAndTagNameAgain(BotType type, Type strategy, string tag)
        {
            var bot = Bots.Create(type, new Rules(), new Random(1));

            Assert.IsType(strategy, bot);
            Assert.Equal(type, Bots.TypeOf(bot));
            Assert.Equal(tag, Bots.Tag(type));
        }

        [Fact]
        public void TypeOf_IsNullForASeatThatIsNotABot()
        {
            Assert.Null(Bots.TypeOf(new NotABot()));
        }

        [Fact]
        public void Parse_RoundTripsEveryName_InAnyCase()
        {
            Assert.Equal(new[] { BotType.Basic, BotType.Counter, BotType.Cautious, BotType.Mimic, BotType.Gambler }, Bots.All);
            foreach (var type in Bots.All)
            {
                Assert.Equal(type, Bots.Parse(type.ToString()));
                Assert.Equal(type, Bots.Parse(type.ToString().ToLowerInvariant()));
                Assert.Equal(type, Bots.Parse($"  {type.ToString().ToUpperInvariant()} "));
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("wizard")]
        [InlineData("1")]
        [InlineData("counter, gambler")]
        [InlineData("random")]
        public void Parse_RejectsWhatIsNotAType(string name)
        {
            Assert.False(Bots.TryParse(name, out _));
            Assert.Throws<ArgumentException>(() => Bots.Parse(name));
        }

        [Fact]
        public void RandomFor_IsFixedByTheSeedAndTheSeat()
        {
            static List<int> Draws(Random random) => Enumerable.Range(0, 10).Select(_ => random.Next()).ToList();

            var draws = Draws(Bots.RandomFor(42, 1));

            Assert.Equal(draws, Draws(Bots.RandomFor(42, 1)));
            Assert.NotEqual(draws, Draws(Bots.RandomFor(42, 2)));
            Assert.NotEqual(draws, Draws(Bots.RandomFor(43, 1)));
        }
    }
}
