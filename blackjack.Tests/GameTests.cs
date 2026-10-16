using blackjack.Domain;
using blackjack.Engine;
using blackjack.Strategies;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    // Deal order with N players: one card to each player, the dealer's upcard, a second card to
    // each player, then the dealer's hole card. Hits and dealer draws follow.
    public class GameTests
    {
        private static Player NewPlayer(string name = "P", decimal chips = 100m) =>
            new(name, chips, new BasicStrategyBot());

        private static Game NewGame(string stack, params Player[] players) =>
            NewGame(new Rules(), stack, players);

        private static Game NewGame(Rules rules, string stack, params Player[] players) =>
            new(rules, players, Shoe.Stacked(Of(stack)));

        private static void BetAndDeal(Game game, decimal bet = 10m)
        {
            foreach (var player in game.Players)
            {
                game.PlaceBet(player, bet);
            }

            game.Deal();
        }

        // Plays rounds with every seat on its own strategy, and returns every card the table saw.
        private static List<Card> PlayRounds(Game game, int rounds)
        {
            var seen = new List<Card>();
            game.CardRevealed += seen.Add;
            for (var round = 0; round < rounds; round++)
            {
                if (game.Phase == Phase.Settled)
                {
                    game.NewRound();
                }

                BetAndDeal(game);
                while (game.Phase == Phase.PlayerTurn)
                {
                    if (game.CurrentPlayer!.Strategy.DecideMove(game.CurrentHand!, game.DealerUpcard!) == Move.Hit)
                    {
                        game.Hit();
                    }
                    else
                    {
                        game.Stand();
                    }
                }
            }

            return seen;
        }

        [Fact]
        public void Deal_GivesEachHandTwoCards_AndKeepsTheHoleCardDown()
        {
            var p1 = NewPlayer("P1");
            var p2 = NewPlayer("P2");
            var game = NewGame("10h 9c 5s 6d 8h 7c", p1, p2);
            var revealed = new List<Card>();
            game.CardRevealed += revealed.Add;

            BetAndDeal(game);

            Assert.Equal(Of("10h 6d"), p1.Hands[0].Cards);
            Assert.Equal(Of("9c 8h"), p2.Hands[0].Cards);
            Assert.Equal(Of("5s 7c"), game.DealerHand.Cards);
            Assert.Equal(C("5s"), game.DealerUpcard);
            Assert.False(game.HoleCardRevealed);
            Assert.DoesNotContain(C("7c"), revealed);
            Assert.Equal(Phase.PlayerTurn, game.Phase);
            Assert.Same(p1, game.CurrentPlayer);
            Assert.Equal(90m, p1.Chips);
        }

        [Fact]
        public void Turns_PassInSeatOrder()
        {
            var p1 = NewPlayer("P1");
            var p2 = NewPlayer("P2");
            var game = NewGame("10h 9c 5s 7d 8h 10c 4s", p1, p2);

            BetAndDeal(game);
            Assert.Same(p1, game.CurrentPlayer);

            game.Stand();
            Assert.Same(p2, game.CurrentPlayer);
            Assert.True(p1.Hands[0].IsStood);
            Assert.Null(p1.Hands[0].Outcome);
        }

        [Fact]
        public void PlayerNatural_PaysThreeToTwo_AndSkipsTheirTurn()
        {
            var natural = NewPlayer("Natural");
            var other = NewPlayer("Other");
            var game = NewGame("As 10c 9d Kh 8s 7c", natural, other);

            BetAndDeal(game);

            Assert.Equal(Outcome.Blackjack, natural.Hands[0].Outcome);
            Assert.Equal(115m, natural.Chips);
            Assert.Same(other, game.CurrentPlayer);
        }

        [Fact]
        public void PlayerNatural_PaysSixToFive_UnderThatRule()
        {
            var player = NewPlayer();
            var game = NewGame(new Rules { BlackjackPayout = 1.2m }, "As 9d Kh 7c", player);

            BetAndDeal(game);

            Assert.Equal(112m, player.Chips);
        }

        [Fact]
        public void DealerNatural_EndsTheRoundForEveryone()
        {
            var plain = NewPlayer("Plain");
            var natural = NewPlayer("Natural");
            var game = NewGame("10h As Ad 9c Kh Kd", plain, natural);

            BetAndDeal(game);

            Assert.Equal(Phase.Settled, game.Phase);
            Assert.True(game.HoleCardRevealed);
            Assert.Equal(Outcome.Lose, plain.Hands[0].Outcome);
            Assert.Equal(Outcome.Push, natural.Hands[0].Outcome);
            Assert.Equal(90m, plain.Chips);
            Assert.Equal(100m, natural.Chips);
        }

        [Fact]
        public void DealerTenUp_WithAceInTheHole_IsStillANatural()
        {
            var player = NewPlayer();
            var game = NewGame("10h Kd 9c As", player);

            BetAndDeal(game);

            Assert.Equal(Phase.Settled, game.Phase);
            Assert.Equal(Outcome.Lose, player.Hands[0].Outcome);
        }

        [Fact]
        public void Bust_SettlesAtOnce_AndPassesTheTurn()
        {
            var buster = NewPlayer("Buster");
            var other = NewPlayer("Other");
            var game = NewGame("10h 9c 7s 6d 8h 10c Ks", buster, other);

            BetAndDeal(game);
            game.Hit();

            Assert.True(buster.Hands[0].IsBust);
            Assert.Equal(Outcome.Lose, buster.Hands[0].Outcome);
            Assert.Equal(90m, buster.Chips);
            Assert.Same(other, game.CurrentPlayer);
        }

        [Fact]
        public void EveryHandBust_DealerTurnsOverButDoesNotDraw()
        {
            var player = NewPlayer();
            var game = NewGame("10h 10s 6d 5c Ks 9h", player);

            BetAndDeal(game);
            game.Hit();

            Assert.Equal(Phase.Settled, game.Phase);
            Assert.True(game.HoleCardRevealed);
            Assert.Equal(2, game.DealerHand.Cards.Count);
            Assert.Equal(Outcome.Lose, player.Hands[0].Outcome);
        }

        [Fact]
        public void HittingToTwentyOne_StandsAutomatically()
        {
            var p1 = NewPlayer("P1");
            var p2 = NewPlayer("P2");
            var game = NewGame("10h 9c 7s 5d 8h 10c 6s", p1, p2);

            BetAndDeal(game);
            game.Hit();

            Assert.Equal(21, p1.Hands[0].Value);
            Assert.True(p1.Hands[0].IsStood);
            Assert.Same(p2, game.CurrentPlayer);
        }

        [Fact]
        public void DealerDrawsToSeventeen_ThenSettles()
        {
            var player = NewPlayer();
            var game = NewGame("10h 5s 8d 4c 2h 6c", player);

            BetAndDeal(game);
            game.Stand();

            // Dealer 5 + 4 = 9, draws 2 = 11, draws 6 = 17 and stands.
            Assert.Equal(17, game.DealerHand.Value);
            Assert.Equal(4, game.DealerHand.Cards.Count);
            Assert.Equal(Outcome.Win, player.Hands[0].Outcome);
            Assert.Equal(110m, player.Chips);
        }

        [Fact]
        public void DealerStandsOnSoft17_ByDefault()
        {
            var player = NewPlayer();
            var game = NewGame("10h As 8d 6c 4s", player);

            BetAndDeal(game);
            game.Stand();

            Assert.Equal(17, game.DealerHand.Value);
            Assert.Equal(2, game.DealerHand.Cards.Count);
            Assert.Equal(Outcome.Win, player.Hands[0].Outcome);
        }

        [Fact]
        public void DealerHitsSoft17_WhenTheRuleSaysSo()
        {
            var player = NewPlayer();
            var game = NewGame(new Rules { DealerHitsSoft17 = true }, "10h As 8d 6c 4s", player);

            BetAndDeal(game);
            game.Stand();

            Assert.Equal(21, game.DealerHand.Value);
            Assert.Equal(Outcome.Lose, player.Hands[0].Outcome);
            Assert.Equal(90m, player.Chips);
        }

        [Fact]
        public void DealerBust_PaysEveryLiveHand()
        {
            var p1 = NewPlayer("P1");
            var p2 = NewPlayer("P2");
            var game = NewGame("10h 10c 6s 2d 3h 10d Ks", p1, p2);

            BetAndDeal(game);
            game.Stand();
            game.Stand();

            // Dealer 6 + 10 = 16, draws K and busts.
            Assert.True(game.DealerHand.IsBust);
            Assert.Equal(Outcome.Win, p1.Hands[0].Outcome);
            Assert.Equal(Outcome.Win, p2.Hands[0].Outcome);
            Assert.Equal(110m, p1.Chips);
            Assert.Equal(110m, p2.Chips);
        }

        [Fact]
        public void EqualTotals_Push()
        {
            var player = NewPlayer();
            var game = NewGame("10h 10s 8d 8c", player);

            BetAndDeal(game);
            game.Stand();

            Assert.Equal(Outcome.Push, player.Hands[0].Outcome);
            Assert.Equal(100m, player.Chips);
        }

        [Fact]
        public void LowerTotal_Loses()
        {
            var player = NewPlayer();
            var game = NewGame("10h 10s 7d 9c", player);

            BetAndDeal(game);
            game.Stand();

            Assert.Equal(Outcome.Lose, player.Hands[0].Outcome);
            Assert.Equal(90m, player.Chips);
        }

        [Fact]
        public void PlayerWhoDoesNotBet_SitsOutTheRound()
        {
            var playing = NewPlayer("Playing");
            var sitting = NewPlayer("Sitting");
            var game = NewGame("10h 7s 9d 10c", playing, sitting);

            game.PlaceBet(playing, 10m);
            game.Deal();

            Assert.Empty(sitting.Hands);
            Assert.Same(playing, game.CurrentPlayer);
            game.Stand();
            Assert.Equal(Phase.Settled, game.Phase);
            Assert.Equal(100m, sitting.Chips);
        }

        [Fact]
        public void NewRound_ClearsTheTable()
        {
            var player = NewPlayer();
            var game = NewGame("10h 10s 7d 9c 10h 10s 7d 9c", player);
            BetAndDeal(game);
            game.Stand();

            game.NewRound();

            Assert.Equal(Phase.Betting, game.Phase);
            Assert.Empty(player.Hands);
            Assert.Empty(game.DealerHand.Cards);
            Assert.False(game.HoleCardRevealed);
            Assert.Null(game.DealerUpcard);
        }

        [Fact]
        public void EmptyShoeMidRound_IsReshuffled()
        {
            var player = NewPlayer();
            var game = NewGame("10h 5s 6c 4d", player);
            var shuffled = false;
            game.Shuffled += () => shuffled = true;

            BetAndDeal(game);
            game.Hit(); // the stacked shoe is empty, so it reshuffles and deals 10h again

            Assert.True(shuffled);
            Assert.Equal(26, player.Hands[0].Value);
        }

        [Fact]
        public void SameSeed_DealsTheSameCards_RoundAfterRound()
        {
            // One deck cut half way, so ten rounds cross a shuffle or two.
            var rules = new Rules { DeckCount = 1, Penetration = 0.5m };
            var a = new Game(rules, new[] { NewPlayer("P1", 1000m), NewPlayer("P2", 1000m) }, new Shoe(1, 99, rules.Penetration));
            var b = new Game(rules, new[] { NewPlayer("P1", 1000m), NewPlayer("P2", 1000m) }, new Shoe(1, 99, rules.Penetration));

            var dealt = PlayRounds(a, 10);

            Assert.True(a.ShoeNumber > 1);
            Assert.Equal(dealt, PlayRounds(b, 10));
        }

        [Fact]
        public void ActionsOutOfPhase_Throw()
        {
            var player = NewPlayer();
            var game = NewGame("10h 10s 7d 9c", player);

            Assert.Throws<InvalidOperationException>(() => game.Hit());
            Assert.Throws<InvalidOperationException>(() => game.Stand());
            Assert.Throws<InvalidOperationException>(() => game.Deal()); // no bets
            Assert.Throws<InvalidOperationException>(() => game.NewRound());

            game.PlaceBet(player, 10m);
            Assert.Throws<InvalidOperationException>(() => game.PlaceBet(player, 10m));
            Assert.Throws<InvalidOperationException>(() => game.RemovePlayer(player));

            game.Deal();
            Assert.Throws<InvalidOperationException>(() => game.PlaceBet(player, 10m));
            Assert.Throws<InvalidOperationException>(() => game.Deal());
        }

        [Fact]
        public void PlaceBet_ChecksTheStake()
        {
            var player = NewPlayer(chips: 50m);
            var stranger = NewPlayer("Stranger");
            var game = NewGame("10h 10s 7d 9c", player);

            Assert.Throws<ArgumentOutOfRangeException>(() => game.PlaceBet(player, 5m));
            Assert.Throws<InvalidOperationException>(() => game.PlaceBet(player, 60m));
            Assert.Throws<ArgumentException>(() => game.PlaceBet(stranger, 10m));
            Assert.Equal(50m, player.Chips);
        }

        [Fact]
        public void Constructor_ChecksTheTable()
        {
            var player = NewPlayer();

            Assert.Throws<ArgumentException>(() => new Game(new Rules(), Array.Empty<Player>()));
            Assert.Throws<ArgumentException>(() => new Game(new Rules(), Enumerable.Range(0, 8).Select(i => NewPlayer($"P{i}"))));
            Assert.Throws<ArgumentException>(() => new Game(new Rules(), new[] { player, player }));
            Assert.Throws<InvalidOperationException>(() => new Game(new Rules { MinimumBet = 0m }, new[] { player }));
        }

        [Fact]
        public void RemovePlayer_BetweenRounds()
        {
            var stays = NewPlayer("Stays");
            var leaves = NewPlayer("Leaves");
            var game = NewGame("10h 7s 9d 10c", stays, leaves);

            game.RemovePlayer(leaves);

            Assert.Equal(new[] { stays }, game.Players);
        }
    }
}
