using blackjack.Domain;
using blackjack.Engine;
using blackjack.Engine.SideBets;
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

        private static void DeclineInsurance(Game game)
        {
            while (game.Phase == Phase.Insurance)
            {
                game.DeclineInsurance();
            }
        }

        private static readonly ISideBet TwentyOnePlusThree = SideBets.All[0];
        private static readonly ISideBet BustIt = SideBets.All[1];

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
                DeclineInsurance(game);
                while (game.Phase == Phase.PlayerTurn)
                {
                    var options = new MoveOptions(game.CanDouble, game.CanSplit, game.CanHit);
                    switch (game.CurrentPlayer!.Strategy.DecideMove(game.CurrentHand!, game.DealerUpcard!, options))
                    {
                        case Move.Hit:
                            game.Hit();
                            break;
                        case Move.Double:
                            game.Double();
                            break;
                        case Move.Split:
                            game.Split();
                            break;
                        default:
                            game.Stand();
                            break;
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
            Assert.Equal(Phase.Insurance, game.Phase);
            DeclineInsurance(game);

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
            DeclineInsurance(game);
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
            DeclineInsurance(game);
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
            var game = new Game(new Rules(), new[] { player }, Shoe.Stacked(Of("10h Ad 7s Ks 10h 10s 7d 9c")), new[] { BustIt });
            game.PlaceBet(player, 10m);
            game.PlaceSideBet(player, BustIt, 10m);
            game.Deal();
            game.TakeInsurance();

            game.NewRound();

            Assert.Equal(Phase.Betting, game.Phase);
            Assert.Empty(player.Hands);
            Assert.Empty(player.SideBets);
            Assert.Equal(0m, player.Insurance);
            Assert.Equal(0m, player.InsurancePayout);
            Assert.Equal(0m, player.RoundNet);
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
            Assert.Throws<InvalidOperationException>(() => new Game(new Rules { MaxSplitHands = 0 }, new[] { player }));
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

        [Fact]
        public void Double_TakesOneCard_DoublesTheBet_AndStands()
        {
            var player = NewPlayer();
            var game = NewGame("5h 9d 6c 8s 10s", player);

            BetAndDeal(game);
            Assert.True(game.CanDouble);
            game.Double();

            var hand = player.Hands[0];
            Assert.Equal(Of("5h 6c 10s"), hand.Cards);
            Assert.Equal(20m, hand.Bet);
            Assert.True(hand.IsDoubled);
            Assert.True(hand.IsStood);
            Assert.Equal(Phase.Settled, game.Phase);
            Assert.Equal(Outcome.Win, hand.Outcome); // 21 against the dealer's 17
            Assert.Equal(120m, player.Chips);
        }

        [Fact]
        public void Double_ThatBusts_SettlesAtOnce()
        {
            var player = NewPlayer();
            var game = NewGame("10h 9d 6c 8s 10s", player);

            BetAndDeal(game);
            game.Double();

            Assert.Equal(Outcome.Lose, player.Hands[0].Outcome);
            Assert.Equal(80m, player.Chips);
            Assert.Equal(2, game.DealerHand.Cards.Count);
            Assert.Equal(Phase.Settled, game.Phase);
        }

        [Fact]
        public void Double_PassesTheTurn()
        {
            var p1 = NewPlayer("P1");
            var p2 = NewPlayer("P2");
            var game = NewGame("5h 10h 9d 6c 7c 8s 10s", p1, p2);

            BetAndDeal(game);
            game.Double();

            Assert.Equal(21, p1.Hands[0].Value);
            Assert.Same(p2, game.CurrentPlayer);
        }

        [Fact]
        public void Double_IsRefused_OnThreeCards_OrWhenBroke()
        {
            var player = NewPlayer();
            var game = NewGame("5h 9d 3c 8s 2d", player);
            BetAndDeal(game);
            game.Hit();

            Assert.Equal(3, player.Hands[0].Cards.Count);
            Assert.False(game.CanDouble);
            Assert.Throws<InvalidOperationException>(() => game.Double());

            var broke = NewPlayer(chips: 10m);
            var game2 = NewGame("5h 9d 6c 8s", broke);
            BetAndDeal(game2);

            Assert.False(game2.CanDouble);
            Assert.Throws<InvalidOperationException>(() => game2.Double());
            Assert.Equal(0m, broke.Chips);
        }

        [Theory]
        [InlineData("5h 3c", false)]  // hard 8
        [InlineData("5h 4c", true)]   // hard 9
        [InlineData("6h 4c", true)]   // hard 10
        [InlineData("6h 5c", true)]   // hard 11
        [InlineData("10h 2c", false)] // hard 12
        [InlineData("Ah 6c", false)]  // soft 17
        [InlineData("Ah 9c", false)]  // soft 20
        public void Double_OnlyOnHardNineToEleven_UnderThatRule(string cards, bool allowed)
        {
            var player = NewPlayer();
            var (first, second) = (cards.Split(' ')[0], cards.Split(' ')[1]);
            var game = NewGame(new Rules { DoubleOnAnyTwo = false }, $"{first} 9d {second} 8s", player);

            BetAndDeal(game);

            Assert.Equal(allowed, game.CanDouble);
        }

        [Fact]
        public void Double_AfterSplit_NeedsTheRule()
        {
            var player = NewPlayer();
            var game = NewGame(new Rules { DoubleAfterSplit = false }, "8h 9d 8c 8s 3d 2c", player);
            BetAndDeal(game);
            game.Split();

            Assert.Equal(11, game.CurrentHand!.Value);
            Assert.False(game.CanDouble);

            var allowed = NewPlayer();
            var game2 = NewGame("8h 9d 8c 8s 3d 2c", allowed);
            BetAndDeal(game2);
            game2.Split();

            Assert.True(game2.CanDouble);
        }

        [Fact]
        public void Split_MakesTwoHands_PlayedInTurnOrder()
        {
            var p1 = NewPlayer("P1");
            var p2 = NewPlayer("P2");
            var game = NewGame("8h 10h 6d 8c 7c 10s 3d 10c 10d Ks", p1, p2);
            var revealed = new List<Card>();
            game.CardRevealed += revealed.Add;

            BetAndDeal(game);
            Assert.True(game.CanSplit);
            game.Split();

            Assert.Equal(2, p1.Hands.Count);
            var (first, second) = (p1.Hands[0], p1.Hands[1]);
            Assert.Equal(Of("8h 3d"), first.Cards);
            Assert.Equal(Of("8c 10c"), second.Cards);
            Assert.True(first.IsSplit);
            Assert.True(second.IsSplit);
            Assert.Equal(10m, second.Bet);
            Assert.Equal(80m, p1.Chips);
            Assert.Equal(Of("3d 10c"), revealed.TakeLast(2));
            Assert.Same(first, game.CurrentHand);

            game.Hit(); // 8 + 3 + 10 = 21 stands by itself
            Assert.Same(second, game.CurrentHand);
            game.Stand();
            Assert.Same(p2, game.CurrentPlayer);
            game.Stand();

            // Dealer 6 + 10 draws K and busts: every hand wins.
            Assert.Equal(Phase.Settled, game.Phase);
            Assert.All(p1.Hands, h => Assert.Equal(Outcome.Win, h.Outcome));
            Assert.Equal(120m, p1.Chips);
            Assert.Equal(110m, p2.Chips);
            Assert.Equal(20m, p1.RoundNet);
        }

        [Fact]
        public void Split_ResplitsUpToMaxHands_ThenRefuses()
        {
            Assert.Equal(4, new Rules().MaxSplitHands);

            var player = NewPlayer();
            var game = NewGame(new Rules { MaxSplitHands = 3 }, "8h 6d 8c 10s 8d 5s 8s 9h Ks", player);
            BetAndDeal(game);

            game.Split();
            Assert.Equal(Of("8h 8d"), game.CurrentHand!.Cards);
            Assert.True(game.CanSplit);
            game.Split();

            Assert.Equal(3, player.Hands.Count);
            Assert.Equal(Of("8h 8s"), player.Hands[0].Cards);
            Assert.Equal(Of("8d 9h"), player.Hands[1].Cards);
            Assert.Equal(Of("8c 5s"), player.Hands[2].Cards);
            Assert.Equal(70m, player.Chips);
            Assert.False(game.CanSplit); // still a pair, but three hands is the limit
            Assert.Throws<InvalidOperationException>(() => game.Split());

            game.Stand();
            game.Stand();
            game.Stand();

            Assert.True(game.DealerHand.IsBust);
            Assert.Equal(130m, player.Chips);
        }

        [Fact]
        public void Split_MaxHands_CanTurnSplittingOff()
        {
            var two = NewPlayer();
            var game = NewGame(new Rules { MaxSplitHands = 2 }, "8h 6d 8c 10s 8d 5s", two);
            BetAndDeal(game);
            game.Split();
            Assert.Equal(Of("8h 8d"), game.CurrentHand!.Cards);
            Assert.False(game.CanSplit);

            var one = NewPlayer();
            var game2 = NewGame(new Rules { MaxSplitHands = 1 }, "8h 6d 8c 10s", one);
            BetAndDeal(game2);
            Assert.False(game2.CanSplit);
            Assert.Throws<InvalidOperationException>(() => game2.Split());
        }

        [Fact]
        public void Split_TreatsAnyTwoTenValueCardsAsAPair()
        {
            var player = NewPlayer();
            var game = NewGame("Kh 6d Qc 10s 2d 3d", player);
            BetAndDeal(game);

            Assert.True(game.CanSplit);
            game.Split();
            Assert.Equal(Of("Kh 2d"), player.Hands[0].Cards);
            Assert.Equal(Of("Qc 3d"), player.Hands[1].Cards);

            var other = NewPlayer();
            var game2 = NewGame("Kh 6d 9c 10s", other);
            BetAndDeal(game2);
            Assert.False(game2.CanSplit);
        }

        [Fact]
        public void Split_IsRefused_WhenBroke()
        {
            var player = NewPlayer(chips: 10m);
            var game = NewGame("8h 6d 8c 10s", player);
            BetAndDeal(game);

            Assert.False(game.CanSplit);
            Assert.Throws<InvalidOperationException>(() => game.Split());
        }

        [Fact]
        public void Split_Aces_GetOneCardEach_AndStand()
        {
            var player = NewPlayer();
            var game = NewGame("Ah 6d Ac 10s 9d 10c Ks", player);
            BetAndDeal(game);

            game.Split();

            // Both hands stood at once, so the dealer played: 16, draws K, busts.
            Assert.Equal(Phase.Settled, game.Phase);
            var (first, second) = (player.Hands[0], player.Hands[1]);
            Assert.Equal(Of("Ah 9d"), first.Cards);
            Assert.Equal(Of("Ac 10c"), second.Cards);
            Assert.Equal(21, second.Value);
            Assert.False(second.IsNatural);
            Assert.Equal(Outcome.Win, second.Outcome);
            Assert.Equal(20m, second.Payout); // even money, not 3:2
            Assert.Equal(120m, player.Chips);
        }

        [Fact]
        public void Split_HitSplitAces_WhenTheRuleAllows()
        {
            var player = NewPlayer();
            var game = NewGame(new Rules { HitSplitAces = true }, "Ah 6d Ac 10s 9d 10c 2s Ks", player);
            BetAndDeal(game);

            game.Split();

            Assert.Equal(Phase.PlayerTurn, game.Phase);
            Assert.Same(player.Hands[0], game.CurrentHand);
            Assert.True(game.CanHit);
            Assert.True(player.Hands[1].IsStood); // 21 is never hit

            game.Hit();
            Assert.Equal(12, player.Hands[0].Value);
            game.Stand();

            Assert.Equal(Phase.Settled, game.Phase);
            Assert.Equal(120m, player.Chips);
        }

        [Fact]
        public void Split_ResplitAces_NeedsTheRule()
        {
            var player = NewPlayer();
            var game = NewGame("Ah 6d Ac 10s Ad 10c Ks", player);
            BetAndDeal(game);
            game.Split();

            // A second pair of aces stands with one card each.
            Assert.Equal(Phase.Settled, game.Phase);
            Assert.Equal(2, player.Hands.Count);
            Assert.Equal(Of("Ah Ad"), player.Hands[0].Cards);

            var resplitter = NewPlayer();
            var game2 = NewGame(new Rules { ResplitAces = true }, "Ah 6d Ac 10s Ad 10c 5h 7c Ks", resplitter);
            BetAndDeal(game2);
            game2.Split();

            // The pair of aces can be split again, but not hit.
            Assert.Equal(Phase.PlayerTurn, game2.Phase);
            Assert.Equal(Of("Ah Ad"), game2.CurrentHand!.Cards);
            Assert.True(game2.CanSplit);
            Assert.False(game2.CanHit);
            Assert.False(game2.CanDouble);
            Assert.Throws<InvalidOperationException>(() => game2.Hit());

            game2.Split();

            Assert.Equal(Phase.Settled, game2.Phase);
            Assert.Equal(3, resplitter.Hands.Count);
            Assert.Equal(Of("Ah 5h"), resplitter.Hands[0].Cards);
            Assert.Equal(Of("Ad 7c"), resplitter.Hands[1].Cards);
            Assert.Equal(Of("Ac 10c"), resplitter.Hands[2].Cards);
            Assert.Equal(130m, resplitter.Chips);
        }

        [Fact]
        public void SplitTwentyOne_IsNotANatural_AndPaysEvenMoney()
        {
            var player = NewPlayer();
            var game = NewGame("10h 6d Kc 10s Ad 5c Ks", player);
            BetAndDeal(game);
            game.Split();

            var twentyOne = player.Hands[0];
            Assert.Equal(21, twentyOne.Value);
            Assert.False(twentyOne.IsNatural);
            Assert.True(twentyOne.IsStood);
            Assert.Same(player.Hands[1], game.CurrentHand);

            game.Stand();

            Assert.Equal(Outcome.Win, twentyOne.Outcome);
            Assert.Equal(20m, twentyOne.Payout);
            Assert.Equal(120m, player.Chips);
        }

        [Fact]
        public void SplitTwentyOne_PaysLikeABlackjack_UnderThatRule()
        {
            var player = NewPlayer();
            var game = NewGame(new Rules { SplitBlackjackPaysEven = false }, "10h 6d Kc 10s Ad 5c Ks", player);
            BetAndDeal(game);
            game.Split();
            game.Stand();

            Assert.Equal(Outcome.Blackjack, player.Hands[0].Outcome);
            Assert.Equal(25m, player.Hands[0].Payout);
            Assert.Equal(125m, player.Chips);
        }

        [Fact]
        public void SplitTwentyOne_PushesAgainstADealerTwentyOne()
        {
            var player = NewPlayer();
            var game = NewGame(new Rules { SplitBlackjackPaysEven = false }, "10h 6d Kc 10s Ad 5c 5s", player);
            BetAndDeal(game);
            game.Split();
            game.Stand();

            Assert.Equal(21, game.DealerHand.Value);
            Assert.Equal(Outcome.Push, player.Hands[0].Outcome);
            Assert.Equal(Outcome.Lose, player.Hands[1].Outcome);
            Assert.Equal(90m, player.Chips);
        }

        [Fact]
        public void Insurance_IsOnlyOfferedOnAnAce()
        {
            var player = NewPlayer();
            var game = NewGame("10h 9d 7c 8s", player);
            BetAndDeal(game);

            Assert.Equal(Phase.PlayerTurn, game.Phase);
            Assert.Throws<InvalidOperationException>(() => game.TakeInsurance());

            var offered = NewPlayer();
            var game2 = NewGame("10h Ad 7c 8s", offered);
            BetAndDeal(game2);

            Assert.Equal(Phase.Insurance, game2.Phase);
            Assert.Same(offered, game2.CurrentPlayer);
            Assert.Null(game2.CurrentHand);
            Assert.False(game2.HoleCardRevealed);
        }

        [Fact]
        public void Insurance_PaysTwoToOne_OnADealerNatural()
        {
            var player = NewPlayer();
            var game = NewGame("10h Ad 7c Ks", player);
            BetAndDeal(game);

            game.TakeInsurance();

            Assert.Equal(Phase.Settled, game.Phase);
            Assert.Equal(5m, player.Insurance);
            Assert.Equal(15m, player.InsurancePayout);
            Assert.Equal(Outcome.Lose, player.Hands[0].Outcome);
            Assert.Equal(100m, player.Chips);
            Assert.Equal(0m, player.RoundNet);
        }

        [Fact]
        public void Insurance_IsLost_WhenTheDealerHasNoNatural()
        {
            var player = NewPlayer();
            var game = NewGame("10h Ad 7c 8s", player);
            BetAndDeal(game);

            game.TakeInsurance();

            Assert.Equal(Phase.PlayerTurn, game.Phase);
            Assert.Equal(85m, player.Chips);
            Assert.Equal(0m, player.InsurancePayout);
            Assert.Equal(-15m, player.RoundNet);

            game.Stand();

            Assert.Equal(Outcome.Lose, player.Hands[0].Outcome); // 17 against soft 19
            Assert.Equal(85m, player.Chips);
        }

        [Fact]
        public void Insurance_AsksEachPlayerWhoCanAffordIt()
        {
            var p1 = NewPlayer("P1");
            var broke = NewPlayer("Broke", chips: 10m);
            var p3 = NewPlayer("P3");
            var game = NewGame("10h 9c 5s Ad 7c 8d 6h Ks", p1, broke, p3);
            BetAndDeal(game);

            Assert.Same(p1, game.CurrentPlayer);
            game.TakeInsurance();
            Assert.Same(p3, game.CurrentPlayer);
            game.DeclineInsurance();

            Assert.Equal(Phase.Settled, game.Phase);
            Assert.True(game.DealerHand.IsNatural);
            Assert.Equal(100m, p1.Chips);
            Assert.Equal(0m, broke.Chips);
            Assert.Equal(90m, p3.Chips);
        }

        [Fact]
        public void Insurance_PhaseGuards()
        {
            var player = NewPlayer();
            var game = NewGame("10h Ad 7c 8s 5s", player);
            BetAndDeal(game);

            Assert.Equal(Phase.Insurance, game.Phase);
            Assert.False(game.CanDouble);
            Assert.False(game.CanSplit);
            Assert.Throws<InvalidOperationException>(() => game.Hit());
            Assert.Throws<InvalidOperationException>(() => game.Stand());
            Assert.Throws<InvalidOperationException>(() => game.Double());
            Assert.Throws<InvalidOperationException>(() => game.Split());
            Assert.Throws<InvalidOperationException>(() => game.Deal());
            Assert.Throws<InvalidOperationException>(() => game.PlaceBet(player, 10m));
            Assert.Throws<InvalidOperationException>(() => game.RemovePlayer(player));

            game.DeclineInsurance();

            Assert.Equal(Phase.PlayerTurn, game.Phase);
            Assert.Throws<InvalidOperationException>(() => game.DeclineInsurance());
        }

        [Fact]
        public void PlaceSideBet_ChecksTheTable()
        {
            var player = NewPlayer();
            var stranger = NewPlayer("Stranger");
            var game = new Game(new Rules(), new[] { player }, Shoe.Stacked(Of("10h 9d 7c 8s")), new[] { TwentyOnePlusThree, BustIt });

            Assert.Equal(new[] { TwentyOnePlusThree, BustIt }, game.SideBets);
            Assert.Throws<InvalidOperationException>(() => game.PlaceSideBet(player, BustIt, 10m)); // no main bet yet

            game.PlaceBet(player, 10m);
            Assert.Throws<ArgumentException>(() => game.PlaceSideBet(stranger, BustIt, 10m));
            Assert.Throws<ArgumentException>(() => game.PlaceSideBet(player, SideBets.All[2], 10m)); // not offered
            Assert.Throws<ArgumentOutOfRangeException>(() => game.PlaceSideBet(player, BustIt, 0m));
            Assert.Throws<ArgumentOutOfRangeException>(() => game.PlaceSideBet(player, BustIt, 11m)); // more than the main bet

            game.PlaceSideBet(player, BustIt, 10m);
            Assert.Throws<InvalidOperationException>(() => game.PlaceSideBet(player, BustIt, 10m)); // one stake per bet
            game.PlaceSideBet(player, TwentyOnePlusThree, 5m);

            Assert.Equal(75m, player.Chips);
            Assert.Equal(-25m, player.RoundNet);
            Assert.Equal(2, player.SideBets.Count);

            game.Deal();
            Assert.Throws<InvalidOperationException>(() => game.PlaceSideBet(player, BustIt, 10m));
        }

        [Fact]
        public void PlaceSideBet_NeedsTheChips()
        {
            var player = NewPlayer(chips: 15m);
            var game = new Game(new Rules(), new[] { player }, Shoe.Stacked(Of("10h 9d 7c 8s")), new[] { BustIt });
            game.PlaceBet(player, 10m);

            Assert.Throws<InvalidOperationException>(() => game.PlaceSideBet(player, BustIt, 10m));
            Assert.Equal(5m, player.Chips);
            Assert.Empty(player.SideBets);
        }

        [Fact]
        public void Tables_WithoutSideBets_RefuseThem()
        {
            var player = NewPlayer();
            var game = NewGame("10h 9d 7c 8s", player);
            game.PlaceBet(player, 10m);

            Assert.Empty(game.SideBets);
            Assert.Throws<ArgumentException>(() => game.PlaceSideBet(player, BustIt, 10m));
        }

        [Fact]
        public void DealTimeSideBets_SettleAfterThePeek()
        {
            var player = NewPlayer();
            var game = new Game(new Rules(), new[] { player }, Shoe.Stacked(Of("8h 8d 8c 10s")), new[] { TwentyOnePlusThree, BustIt });
            game.PlaceBet(player, 10m);
            game.PlaceSideBet(player, TwentyOnePlusThree, 10m);
            game.PlaceSideBet(player, BustIt, 10m);

            game.Deal();

            // 8-8-8 is three of a kind at 30 to 1; the Bust It stake waits for the dealer.
            Assert.Equal(Phase.PlayerTurn, game.Phase);
            var trips = player.SideBets[0];
            Assert.Equal("three of a kind", trips.Result?.Label);
            Assert.Equal(310m, trips.Payout);
            Assert.Equal(300m, trips.Net);
            Assert.False(player.SideBets[1].IsResolved);
            Assert.Equal(380m, player.Chips);
        }

        [Fact]
        public void BustIt_MakesTheDealerPlayOn_WhenEveryHandIsBust()
        {
            var player = NewPlayer();
            var game = new Game(new Rules(), new[] { player }, Shoe.Stacked(Of("10h 6d 6c 10s Ks 9h")), new[] { BustIt });
            game.PlaceBet(player, 10m);
            game.PlaceSideBet(player, BustIt, 10m);
            game.Deal();

            game.Hit(); // K busts the only hand

            // Without the stake the dealer would stop at two cards; with it, 16 draws 9 and busts.
            Assert.Equal(Phase.Settled, game.Phase);
            Assert.Equal(3, game.DealerHand.Cards.Count);
            Assert.True(game.DealerHand.IsBust);
            var stake = player.SideBets[0];
            Assert.Equal("3-card bust", stake.Result?.Label);
            Assert.Equal(30m, stake.Payout);
            Assert.Equal(Outcome.Lose, player.Hands[0].Outcome);
            Assert.Equal(110m, player.Chips);
            Assert.Equal(10m, player.RoundNet);
        }

        [Fact]
        public void BustIt_Loses_OnADealerNatural()
        {
            var player = NewPlayer();
            var game = new Game(new Rules(), new[] { player }, Shoe.Stacked(Of("10h Ad 7c Ks")), new[] { BustIt });
            game.PlaceBet(player, 10m);
            game.PlaceSideBet(player, BustIt, 10m);
            game.Deal();
            game.DeclineInsurance();

            Assert.Equal(Phase.Settled, game.Phase);
            var stake = player.SideBets[0];
            Assert.Equal("dealer blackjack", stake.Result?.Label);
            Assert.Equal(0m, stake.Payout);
            Assert.Equal(80m, player.Chips);
        }

        [Fact]
        public void BustIt_Loses_WhenTheDealerStands()
        {
            var player = NewPlayer();
            var game = new Game(new Rules(), new[] { player }, Shoe.Stacked(Of("10h 9d 7c 8s")), new[] { BustIt });
            game.PlaceBet(player, 10m);
            game.PlaceSideBet(player, BustIt, 10m);
            game.Deal();
            game.Stand();

            Assert.Equal("no bust", player.SideBets[0].Result?.Label);
            Assert.Equal(-10m, player.SideBetNet);
            Assert.Equal(90m, player.Chips); // 17 pushes against 17
        }

        [Fact]
        public void SideBets_ResolveForEveryPlayer_AndChipsAddUp()
        {
            var p1 = NewPlayer("P1");
            var p2 = NewPlayer("P2");
            var perfectPairs = SideBets.All[2];
            var game = new Game(new Rules(), new[] { p1, p2 }, Shoe.Stacked(Of("8h 7d 6d 8c 7h 10s Ks")), new[] { perfectPairs, BustIt });
            foreach (var player in game.Players)
            {
                game.PlaceBet(player, 10m);
                game.PlaceSideBet(player, perfectPairs, 10m);
                game.PlaceSideBet(player, BustIt, 10m);
            }

            game.Deal();
            game.Stand();
            game.Stand();

            // P1 holds a mixed pair (6 to 1), P2 a coloured pair (12 to 1); both hands win on the
            // dealer's three-card bust, which also pays Bust It 2 to 1.
            Assert.Equal(70m, p1.SideBets[0].Payout);
            Assert.Equal(130m, p2.SideBets[0].Payout);
            Assert.Equal(30m, p1.SideBets[1].Payout);
            Assert.Equal(30m, p2.SideBets[1].Payout);
            Assert.Equal(80m, p1.SideBetNet);
            Assert.Equal(90m, p1.RoundNet);
            Assert.Equal(190m, p1.Chips);
            Assert.Equal(250m, p2.Chips);
        }
    }
}
