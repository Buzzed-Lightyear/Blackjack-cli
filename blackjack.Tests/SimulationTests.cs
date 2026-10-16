using blackjack.Domain;
using blackjack.Engine;
using blackjack.Engine.SideBets;
using blackjack.Strategies;

namespace blackjack.Tests
{
    public class SimulationTests
    {
        [Fact]
        public void BotsPlayManyRounds_ChipsAddUp_AndTheHouseKeepsItsEdge()
        {
            var rules = new Rules();
            var bots = Enumerable.Range(1, 5)
                .Select(i => new Player($"Bot {i}", 1_000_000m, new BasicStrategyBot(rules)))
                .ToList();
            var sideBets = new[] { SideBets.All[0], SideBets.All[1] };
            var game = new Game(rules, bots, new Shoe(rules.DeckCount, 12345, rules.Penetration), sideBets);

            var revealedSinceShuffle = new List<Card>();
            var counter = new CardCounter();
            game.CardRevealed += counter.OnCardRevealed;
            game.CardRevealed += revealedSinceShuffle.Add;
            game.Shuffled += counter.Reset;
            game.Shuffled += revealedSinceShuffle.Clear;

            var wagered = 0m;
            var net = 0m;
            var splits = 0;
            var doubles = 0;
            for (var round = 0; round < 20_000; round++)
            {
                if (game.Phase == Phase.Settled)
                {
                    game.NewRound();
                }

                // The first bot also takes every side bet, and insurance, to exercise the lot.
                var chipsBefore = bots.Sum(b => b.Chips);
                foreach (var bot in bots)
                {
                    game.PlaceBet(bot, bot.Strategy.DecideBet(bot.Chips, rules.MinimumBet));
                }

                foreach (var bet in sideBets)
                {
                    game.PlaceSideBet(bots[0], bet, rules.MinimumBet);
                }

                game.Deal();
                while (game.Phase == Phase.Insurance)
                {
                    if (game.CurrentPlayer == bots[0])
                    {
                        game.TakeInsurance();
                    }
                    else
                    {
                        game.DeclineInsurance();
                    }
                }

                while (game.Phase == Phase.PlayerTurn)
                {
                    var options = new MoveOptions(game.CanDouble, game.CanSplit, game.CanHit);
                    switch (game.CurrentPlayer!.Strategy.DecideMove(game.CurrentHand!, game.DealerUpcard!, options))
                    {
                        case Move.Hit:
                            game.Hit();
                            break;
                        case Move.Stand:
                            game.Stand();
                            break;
                        case Move.Double:
                            doubles++;
                            game.Double();
                            break;
                        default:
                            splits++;
                            game.Split();
                            break;
                    }
                }

                Assert.Equal(Phase.Settled, game.Phase);
                Assert.True(game.HoleCardRevealed);

                var hands = bots.SelectMany(b => b.Hands).ToList();
                Assert.All(hands, h => Assert.NotNull(h.Outcome));
                Assert.All(hands.Where(h => h.IsBust), h => Assert.Equal(Outcome.Lose, h.Outcome));
                Assert.All(hands.Where(h => h.IsDoubled), h => Assert.Equal(3, h.Cards.Count));
                Assert.All(bots, b => Assert.InRange(b.Hands.Count, 1, rules.MaxSplitHands));
                Assert.All(bots[0].SideBets, s => Assert.True(s.IsResolved));

                // If any hand was still live, the dealer played out to 17 or more.
                var dealerPlayed = !game.DealerHand.IsNatural && hands.Any(h => !h.IsBust && h.Outcome != Outcome.Blackjack);
                if (dealerPlayed)
                {
                    Assert.True(game.DealerHand.Value >= 17);
                }

                Assert.Equal(chipsBefore + bots.Sum(b => b.RoundNet), bots.Sum(b => b.Chips));
                Assert.Equal(revealedSinceShuffle.Sum(CardCounter.HiLoValue), counter.RunningCount);

                wagered += hands.Sum(h => h.Bet);
                net += hands.Sum(h => h.Payout - h.Bet);
            }

            Assert.True(splits > 0);
            Assert.True(doubles > 0);

            // Full basic strategy on this table gives up about half a percent of the main bets.
            // 100,000 hands leave a sampling error of around a third of a percent, so the band is
            // wide enough not to flake while still catching a payout bug.
            Assert.InRange(net / wagered, -0.02m, 0.008m);
        }
    }
}
