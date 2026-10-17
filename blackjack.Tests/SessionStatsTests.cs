using blackjack.Cli;
using blackjack.Domain;
using blackjack.Strategies;

namespace blackjack.Tests
{
    public class SessionStatsTests
    {
        [Fact]
        public void RecordAutopilot_KeepsTheBooksHandsAndNetApartFromYours()
        {
            var stats = new SessionStats(100m);
            var player = new Player("P", 100m, new BasicStrategyBot());
            player.PlaceBet(10m);
            stats.RecordAutopilot(player);  // unsettled, so the stake is the loss

            Assert.Equal(1, stats.AutopilotHands);
            Assert.Equal(-10m, stats.AutopilotNet);
            Assert.Equal(0, stats.Hands);
            Assert.Equal(0, stats.Losses);
        }
    }
}
