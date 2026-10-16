using blackjack.Domain;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    public class ShoeTests
    {
        private static List<Card> DealAll(Shoe shoe) =>
            Enumerable.Range(0, shoe.CardsRemaining).Select(_ => shoe.Deal()).ToList();

        [Fact]
        public void NewShoe_HoldsEveryCardOncePerDeck()
        {
            var shoe = new Shoe(6, 1);
            var dealt = Enumerable.Range(0, 6 * 52).Select(_ => shoe.Deal()).ToList();

            Assert.Equal(0, shoe.CardsRemaining);
            Assert.All(dealt.GroupBy(c => c), g => Assert.Equal(6, g.Count()));
            Assert.Equal(52, dealt.Distinct().Count());
        }

        [Fact]
        public void SameSeed_DealsSameOrder_ShoeAfterShoe()
        {
            var a = new Shoe(2, 42);
            var b = new Shoe(2, 42);

            Assert.Equal(DealAll(a), DealAll(b));
            a.Shuffle();
            b.Shuffle();
            Assert.Equal(DealAll(a), DealAll(b));
        }

        [Fact]
        public void ShoeNumber_DealsTheSameShoe_HoweverTheEarlierOnesWerePlayed()
        {
            var played = new Shoe(6, 7);
            for (var i = 0; i < 40; i++)
            {
                played.Deal();
            }

            played.Shuffle();
            for (var i = 0; i < 200; i++)
            {
                played.Deal();
            }

            played.Shuffle();
            var fresh = new Shoe(6, 7, firstShoe: 3);

            Assert.Equal(3, played.ShoeNumber);
            Assert.Equal(3, fresh.ShoeNumber);
            Assert.Equal(DealAll(played), DealAll(fresh));
        }

        [Fact]
        public void DifferentSeedsOrShoeNumbers_DealDifferentOrders()
        {
            var first = DealAll(new Shoe(6, 7));

            Assert.NotEqual(first, DealAll(new Shoe(6, 7, firstShoe: 2)));
            Assert.NotEqual(first, DealAll(new Shoe(6, 8)));
        }

        [Fact]
        public void ShoeNumber_CountsUpFromTheFirstShoe()
        {
            var shoe = new Shoe(1, 1, firstShoe: 4);
            Assert.Equal(4, shoe.ShoeNumber);

            shoe.Shuffle();
            shoe.Shuffle();
            Assert.Equal(6, shoe.ShoeNumber);

            var stacked = Shoe.Stacked(C("As"));
            Assert.Equal(1, stacked.ShoeNumber);
            stacked.Shuffle();
            Assert.Equal(2, stacked.ShoeNumber);
        }

        [Fact]
        public void NeedsReshuffle_OnceThePenetrationIsDealt()
        {
            var shoe = new Shoe(1, 1, penetration: 0.75m);

            for (var i = 0; i < 38; i++)
            {
                shoe.Deal();
            }

            Assert.False(shoe.NeedsReshuffle);
            shoe.Deal();
            Assert.True(shoe.NeedsReshuffle); // 39 of 52 dealt, 13 left
        }

        [Fact]
        public void Shuffle_RefillsTheShoe()
        {
            var shoe = new Shoe(1, 1);
            for (var i = 0; i < 45; i++)
            {
                shoe.Deal();
            }

            shoe.Shuffle();

            Assert.Equal(52, shoe.CardsRemaining);
            Assert.False(shoe.NeedsReshuffle);
        }

        [Fact]
        public void Deal_FromEmptyShoe_Throws()
        {
            var shoe = Shoe.Stacked(C("As"));
            shoe.Deal();

            Assert.Throws<InvalidOperationException>(() => shoe.Deal());
        }

        [Fact]
        public void Stacked_DealsInOrder_AndShuffleRestoresIt()
        {
            var shoe = Shoe.Stacked(Of("As Kd 7c"));

            Assert.Equal(C("As"), shoe.Deal());
            Assert.Equal(C("Kd"), shoe.Deal());
            shoe.Shuffle();
            Assert.Equal(C("As"), shoe.Deal());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void DeckCount_MustBePositive(int decks)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Shoe(decks, 1));
        }

        [Fact]
        public void FirstShoe_MustBeOneOrMore()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Shoe(1, 1, firstShoe: 0));
        }
    }
}
