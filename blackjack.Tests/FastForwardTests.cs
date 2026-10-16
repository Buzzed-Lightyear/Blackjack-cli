using blackjack.Cli;
using blackjack.Domain;
using blackjack.Strategies;

namespace blackjack.Tests
{
    public class FastForwardTests
    {
        private static readonly KeyBindings Keys = KeyBindings.Defaults();

        private static ConsoleKeyInfo Press(ConsoleKey key) => new((char)key, key, false, false, false);

        [Fact]
        public void Watch_ChangesSpeed_AndStopsOnAnyOtherKey()
        {
            var autopilot = new FastForward();
            autopilot.Watch();

            Assert.True(autopilot.IsActive);
            Assert.Equal(FastForwardSpeed.Fast, autopilot.Speed);

            autopilot.HandleKey(Press(ConsoleKey.E), Keys);
            autopilot.HandleKey(Press(ConsoleKey.E), Keys);
            Assert.Equal(FastForwardSpeed.Max, autopilot.Speed);

            autopilot.HandleKey(Press(ConsoleKey.Q), Keys);
            Assert.Equal(FastForwardSpeed.Fast, autopilot.Speed);
            Assert.False(autopilot.ShouldStop);

            autopilot.HandleKey(Press(ConsoleKey.Z), Keys);
            Assert.True(autopilot.ShouldStop);
        }

        [Fact]
        public void SkipShoe_StopsOnceEnoughShoesHaveComeOut()
        {
            var autopilot = new FastForward();
            autopilot.SkipShoe();
            autopilot.HandleKey(Press(ConsoleKey.R), Keys); // one more

            Assert.Equal(FastForwardSpeed.Max, autopilot.Speed);
            Assert.Equal(2, autopilot.ShoesLeft);

            autopilot.OnShuffled();
            Assert.False(autopilot.ShouldStop);

            autopilot.OnShuffled();
            Assert.True(autopilot.ShouldStop);
        }

        [Fact]
        public void Watch_RemembersTheLastWatchingSpeed()
        {
            var autopilot = new FastForward();
            autopilot.Watch();
            autopilot.HandleKey(Press(ConsoleKey.Q), Keys);
            autopilot.Stop();

            autopilot.SkipShoe();
            autopilot.HandleKey(Press(ConsoleKey.Q), Keys); // slowing a skip doesn't change the watching speed
            autopilot.Stop();

            autopilot.Watch();
            Assert.Equal(FastForwardSpeed.Normal, autopilot.Speed);
        }

        [Fact]
        public void Hands_AndChips_AreTalliedPerRun()
        {
            var autopilot = new FastForward();
            autopilot.Watch();
            var player = new Player("P", 100m, new BasicStrategyBot());
            player.PlaceBet(10m);
            autopilot.RecordRound(player);

            Assert.Equal(1, autopilot.Hands);
            Assert.Equal(-10m, autopilot.Net); // unsettled, so nothing paid back

            autopilot.Stop();
            autopilot.Watch();
            Assert.Equal(0, autopilot.Hands);
        }
    }
}
