using blackjack.Domain;

namespace blackjack.Cli
{
    internal enum FastForwardSpeed
    {
        Slow,
        Normal,
        Fast,
        Max
    }

    // Autopilot: the book plays your seat (at the table minimum) while you watch. It runs until a
    // key stops it, or until the requested number of fresh shoes has come out.
    internal sealed class FastForward
    {
        private FastForwardSpeed watchSpeed = FastForwardSpeed.Fast;

        public bool IsActive { get; private set; }
        public FastForwardSpeed Speed { get; private set; } = FastForwardSpeed.Fast;

        // Shoes still to skip; 0 means keep going until stopped.
        public int ShoesLeft { get; private set; }
        public bool SkippingShoes { get; private set; }
        public bool StopRequested { get; private set; }
        public int Hands { get; private set; }
        public decimal Net { get; private set; }

        public bool ShouldStop => StopRequested || (SkippingShoes && ShoesLeft == 0);

        public void Watch()
        {
            Start();
            SkippingShoes = false;
            Speed = watchSpeed;
        }

        // Skips to the next shoe at full speed; asking again adds another shoe.
        public void SkipShoe()
        {
            if (!IsActive)
            {
                Start();
                Speed = FastForwardSpeed.Max;
            }

            ShoesLeft = SkippingShoes ? ShoesLeft + 1 : 1;
            SkippingShoes = true;
        }

        public void OnShuffled()
        {
            if (IsActive && SkippingShoes && ShoesLeft > 0)
            {
                ShoesLeft--;
            }
        }

        public void HandleKey(ConsoleKeyInfo key, KeyBindings keys)
        {
            if (keys.Is(KeyAction.Slower, key))
            {
                SetSpeed(Speed == FastForwardSpeed.Slow ? Speed : Speed - 1);
            }
            else if (keys.Is(KeyAction.Faster, key))
            {
                SetSpeed(Speed == FastForwardSpeed.Max ? Speed : Speed + 1);
            }
            else if (keys.Is(KeyAction.NextShoe, key))
            {
                SkipShoe();
            }
            else
            {
                StopRequested = true;
            }
        }

        public void RecordRound(Player player)
        {
            Hands += player.Hands.Count;
            Net += player.RoundNet;
        }

        public void Stop()
        {
            IsActive = false;
            SkippingShoes = false;
            ShoesLeft = 0;
        }

        private void Start()
        {
            IsActive = true;
            StopRequested = false;
            Hands = 0;
            Net = 0m;
        }

        private void SetSpeed(FastForwardSpeed speed)
        {
            Speed = speed;
            if (!SkippingShoes)
            {
                watchSpeed = speed;
            }
        }
    }
}
