using blackjack.Cli;

namespace blackjack.Tests
{
    public class KeyBindingsTests
    {
        private static ConsoleKeyInfo Press(char c, ConsoleKey key) => new(c, key, false, false, false);

        private static KeyChord Chord(string name) =>
            KeyChord.TryParse(name, out var chord) ? chord : throw new ArgumentException(name);

        private static string TempFile(string contents)
        {
            var path = Path.Combine(Path.GetTempPath(), $"blackjack-keys-{Guid.NewGuid():N}.ini");
            File.WriteAllText(path, contents);
            return path;
        }

        [Fact]
        public void Defaults_KeepEverythingUnderTheLeftHand()
        {
            var keys = KeyBindings.Defaults();

            Assert.Equal("D", keys.Label(KeyAction.Hit));
            Assert.Equal("S", keys.Label(KeyAction.Stand));
            Assert.Equal("A", keys.Label(KeyAction.Double));
            Assert.Equal("W", keys.Label(KeyAction.Split));
            Assert.Equal("Space", keys.Label(KeyAction.Deal));
            Assert.Equal("T", keys.Label(KeyAction.SideBet1));
            Assert.Equal("G", keys.Label(KeyAction.SideBet2));
            Assert.Equal("Z", keys.Label(KeyAction.Insurance));
            Assert.True(keys.Is(KeyAction.Hit, Press('d', ConsoleKey.D)));
            Assert.True(keys.Is(KeyAction.Hit, Press('D', ConsoleKey.D)));
            Assert.True(keys.Is(KeyAction.Hit, Press('\0', ConsoleKey.D)));
            Assert.True(keys.Is(KeyAction.Deal, Press(' ', ConsoleKey.Spacebar)));
            Assert.False(keys.Is(KeyAction.Hit, Press('s', ConsoleKey.S)));
        }

        [Theory]
        [InlineData("d", "D")]
        [InlineData("Space", "Space")]
        [InlineData("f5", "F5")]
        [InlineData("/", "/")]
        [InlineData("up", "Up")]
        [InlineData("esc", "Esc")]
        public void Parse_TakesLettersSymbolsAndNamedKeys(string text, string label)
        {
            Assert.True(KeyChord.TryParse(text, out var chord));
            Assert.Equal(label, chord.Label);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Hyper")]
        [InlineData("F13")]
        [InlineData("ab")]
        public void Parse_RejectsWhatIsNotAKey(string text)
        {
            Assert.False(KeyChord.TryParse(text, out _));
        }

        [Fact]
        public void TypingKeys_AreReserved()
        {
            Assert.True(Chord("Enter").IsReserved);
            Assert.True(Chord("Backspace").IsReserved);
            Assert.True(Chord("Esc").IsReserved);
            Assert.True(Chord("7").IsReserved);
            Assert.False(Chord("Space").IsReserved);
        }

        [Fact]
        public void PressedKeys_BecomeChords()
        {
            Assert.True(KeyChord.TryFrom(Press('a', ConsoleKey.A), out var letter));
            Assert.Equal("A", letter.Label);
            Assert.True(KeyChord.TryFrom(Press('\0', ConsoleKey.F5), out var function));
            Assert.Equal("F5", function.Label);
            Assert.True(KeyChord.TryFrom(Press(' ', ConsoleKey.Spacebar), out var space));
            Assert.Equal("Space", space.Label);
            Assert.True(KeyChord.TryFrom(Press('\r', ConsoleKey.Enter), out var enter));
            Assert.True(enter.IsReserved);
        }

        [Fact]
        public void SameKey_IsFine_WhenNeverReadAtTheSameTime()
        {
            var keys = KeyBindings.Defaults();

            Assert.Null(keys.Conflict(KeyAction.Hit, Chord("F")));    // autopilot is only read between hands
            Assert.Null(keys.Conflict(KeyAction.Slower, Chord("Q"))); // already Q, and lower-bet is never read on autopilot
            Assert.Null(keys.Conflict(KeyAction.Split, Chord("T")));  // side bets are only read between hands
            Assert.Null(keys.Conflict(KeyAction.Insurance, Chord("D")));
            Assert.Equal(KeyAction.Book, keys.Conflict(KeyAction.Deal, Chord("B")));
            Assert.Equal(KeyAction.Stand, keys.Conflict(KeyAction.Hit, Chord("S")));
            Assert.Equal(KeyAction.Double, keys.Conflict(KeyAction.Hit, Chord("A")));
            Assert.Equal(KeyAction.Split, keys.Conflict(KeyAction.Stand, Chord("W")));
            Assert.Equal(KeyAction.FastForward, keys.Conflict(KeyAction.SideBet1, Chord("F")));
            Assert.Equal(KeyAction.Insurance, keys.Conflict(KeyAction.Deal, Chord("Z")));  // the deal key declines insurance
            Assert.Equal(KeyAction.Deal, keys.Conflict(KeyAction.Insurance, Chord("Space")));
        }

        [Fact]
        public void Load_ReadsTheFile_AndWarnsAboutWhatItCannotUse()
        {
            var path = TempFile("""
                ; a comment
                [keys]
                hit = F
                stand = G, Space
                fly = Z
                deal = Hyper
                cash_out = 5

                [elsewhere]
                hit = Q
                """);
            try
            {
                var warnings = new List<string>();
                var keys = KeyBindings.Load(path, warnings);

                Assert.Equal("F", keys.Label(KeyAction.Hit));
                Assert.True(keys.Is(KeyAction.Stand, Press(' ', ConsoleKey.Spacebar)));
                Assert.Equal("Space", keys.Label(KeyAction.Deal));  // Hyper isn't a key
                Assert.Equal("X", keys.Label(KeyAction.CashOut));   // digits are for typing bets
                Assert.Equal("A", keys.Label(KeyAction.Double));    // untouched
                Assert.Equal(3, warnings.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Load_PutsAClashingActionBackOnItsDefault()
        {
            var path = TempFile("[keys]\nhit = S\n");
            try
            {
                var warnings = new List<string>();
                var keys = KeyBindings.Load(path, warnings);

                Assert.Equal("D", keys.Label(KeyAction.Hit));
                Assert.Equal("S", keys.Label(KeyAction.Stand));
                Assert.Single(warnings);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Load_WithNoFile_GivesTheDefaults()
        {
            var warnings = new List<string>();
            var keys = KeyBindings.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.ini"), warnings);

            Assert.Empty(warnings);
            Assert.Equal("D", keys.Label(KeyAction.Hit));
        }

        [Fact]
        public void Save_ThenLoad_KeepsTheBindings()
        {
            var keys = KeyBindings.Defaults();
            keys.Set(KeyAction.Hit, Chord("H"));
            keys.Set(KeyAction.Deal, Chord("Tab"));
            keys.Set(KeyAction.SideBet2, Chord("F7"));
            var path = Path.Combine(Path.GetTempPath(), $"blackjack-keys-{Guid.NewGuid():N}.ini");
            try
            {
                keys.Save(path);
                var warnings = new List<string>();
                var loaded = KeyBindings.Load(path, warnings);

                Assert.Empty(warnings);
                Assert.All(Enum.GetValues<KeyAction>(), a => Assert.Equal(keys.Label(a), loaded.Label(a)));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ShippedKeysFile_HoldsTheDefaults()
        {
            var path = Path.Combine(AppContext.BaseDirectory, KeyBindings.FileName);
            var warnings = new List<string>();
            var shipped = KeyBindings.Load(path, warnings);
            var defaults = KeyBindings.Defaults();

            Assert.True(File.Exists(path));
            Assert.Empty(warnings);
            Assert.All(Enum.GetValues<KeyAction>(), a => Assert.Equal(defaults.Label(a), shipped.Label(a)));
        }
    }
}
