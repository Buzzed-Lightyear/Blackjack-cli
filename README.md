# Blackjack

A console blackjack table: you, up to six bots of five kinds (the book, a card counter, a cautious
player, a dealer mimic and a Martingale gambler), the book's advice on every move, and a live Hi-Lo
card count. Double down, split pairs, take insurance, and try your luck on two side bets.

## Play

```sh
dotnet run --project blackjack
```

Options go after `--`, for example `dotnet run --project blackjack -- --fast --seed 42`:

- `--fast` skips the pauses between bot moves and dealer draws
- `--seed <n>` shuffles every shoe from this seed, so the same cards come out again; without it a
  seed is picked at random and shown, so any session can be replayed
- `--shoe <n>` starts at shoe `n` of that seed instead of shoe 1
- `--keys` prints the key bindings and which file they came from
- `--keys-file <path>` reads and saves key bindings somewhere other than `keys.ini`

Setup asks for your name, whether to change the table rules (decks, minimum bet, soft 17, the
blackjack payout, penetration, the doubling and splitting rules, and the shuffle seed, which starts
out as the session's seed), your starting chips, how many bots to seat and of what kinds, whether
to change the side bets, and whether to change the keys. Every question has a default, so Enter all
the way through starts a game with two bots on the book. The game wants a terminal at least 80 by
24; taller ones get bigger cards.

## The table

The header shows the round and the rules on the left, and the shoe in play, its seed and your
chips on the right; on an 80-column terminal the payout and minimum make way for them. The dealer
is under it. Below are the other seats in the order they act, each bot's kind in grey under its
name, with a `▼ YOU` marker where you sit. Your seat is the panel at the bottom with the count
beside it, then the last few things that happened, then a bar that always says what the keys do
right now.

Default keys, all under the left hand:

| When | Key | Does |
| --- | --- | --- |
| Between hands | `Space` (or `Enter`) | deal, at the bet shown |
| | `Q` / `E` | lower / raise the bet by the minimum |
| | digits | type an exact bet (`Backspace` and `Esc` to fix it) |
| | `T` / `G` | add the minimum to the first / second side bet |
| | `F` | autopilot: the book plays your seat while you watch |
| | `R` | skip to the next shoe |
| | `X` twice | cash out |
| Dealer shows an ace | `Z` / `Space` | take / decline insurance |
| Your move | `D` / `S` | hit / stand |
| | `A` / `W` | double down / split, when the bar offers them |
| Any time | `B` / `C` | show or hide the book's advice / the count |
| On autopilot | `Q` / `E` | slower / faster |
| | `R` | skip one more shoe |
| | any other key | stop after this hand |

The book's choice is highlighted on every move. If you go against it, the log says what the book
would have done, and the cash-out summary shows how often you followed it. On autopilot your seat
bets the table minimum, never takes insurance and never stakes a side bet. Keys pressed while the
bots and dealer play are ignored, so a stray press can't deal the next hand.

### Doubling, splitting and insurance

The bar lists `A double` only while the hand can be doubled: two cards, enough chips for a second
bet, and whatever the table rules allow. Doubling doubles the bet, takes exactly one more card and
stands. It lists `W split` only for a pair (any two ten-value cards count) when you hold fewer hands
than the table allows and can cover another bet. Splitting moves the second card to a new hand with
the same bet, deals a card to each, and plays the first hand to the end before the second; a split
hand can be split again. Split aces get one card each and stand; a two-card 21 after a split is an
ordinary 21, not a blackjack. Your seat shows every hand with its bet, a `▶` on the one being
played, and each hand's result once the round is settled; the panel header gives the round's net
when there is more than one.

When the dealer shows an ace, everyone who can afford half their bet is asked about insurance before
the dealer peeks. `Z` takes it for half the bet, `Space` declines. A dealer blackjack pays insurance
2:1 (and the hand loses, unless it is a blackjack too, which pushes); otherwise the insurance is
lost and play goes on. The book never takes insurance.

### Rules

Default rules: 6 decks, dealer stands on soft 17, blackjack pays 3:2, minimum bet 10, reshuffle
after 75% of the shoe. A dealer blackjack ends the round for everyone; a player blackjack is paid
straight away. The setup also asks about these, all shown with their defaults:

- double on any two cards: yes (no limits doubling to hard 9, 10 and 11)
- double after a split: yes
- most hands after splitting: 4 (1 turns splitting off)
- resplit aces: no (yes lets a split ace that draws another ace be split again, though still not hit)
- hit split aces: no
- a 21 after a split pays even money: yes (no pays it like a blackjack when it beats the dealer)

There is no surrender.

### Side bets

The table offers two side bets, 21+3 and Bust It by default; the setup can swap in any two of the
six below. While betting, `T` and `G` add the table minimum to the first and second side bet, and
wrap back to nothing once the stake would pass its cap: a side bet can be at most the main bet, as
far as your chips stretch after the main bet. The stakes show in your seat with their keys, are
remembered between hands like the main bet, and are trimmed when a smaller main bet or stack no
longer covers them. The bots never stake them.

Odds are "to 1": a win returns the stake plus the stake times the odds. House edges are for six
decks; Bust It depends on the soft-17 rule.

| Bet | Settled | Pays | House edge |
| --- | --- | --- | --- |
| 21+3 | at the deal: your two cards and the dealer's upcard as a three-card poker hand | suited three of a kind 100, straight flush 40, three of a kind 30, straight 10, flush 5 | 4.62% |
| Bust It | after the dealer plays, which they do even when every hand has already bust | dealer busts with 3 or 4 cards 2, 5 cards 4, 6 cards 12, 7 cards 50, 8 or more 250; a dealer blackjack loses | 6.21% hitting soft 17, 8.3% standing |
| Perfect Pairs | at the deal: your two cards | perfect pair (same rank and suit) 25, coloured pair (same rank and colour) 12, mixed pair 6 | 6.11% |
| Lucky Ladies | at the deal: your two cards total 20 | two queens of hearts with a dealer blackjack 1000, two queens of hearts 125, matched 20 (same rank and suit) 19, suited 20 9, any 20 4 | 24.71% |
| Royal Match | at the deal: your two cards | suited king and queen 25, any two suited cards 2.5 | 6.67% |
| Lucky Lucky | at the deal: your two cards and the dealer's upcard, aces 1 or 11 | suited 7-7-7 200, suited 6-7-8 100, 7-7-7 50, 6-7-8 30, suited 21 15, 21 3, 20 or 19 2 | 2.66% |

Deal-time bets are settled right after the dealer peeks, so the log and your seat show them before
you play. The cash-out summary counts the side bets placed and won and gives their net on its own
row, apart from the main game's.

## Bots

The bots fill the seats either side of you and come in five kinds:

| Kind | Tag | Bets | Plays |
| --- | --- | --- | --- |
| `basic` | `book` | the table minimum | the full multi-deck book; never takes insurance |
| `counter` | `count` | by the Hi-Lo true count, in units of the minimum: 1 below +2, then 2, 4 and 8 at +2, +3 and +4, and 12 from +5, as far as its chips go | the book with the usual index plays on top (16 and 15 against a ten, 12 and 13 against small cards, doubling 9, 10 and 11 when the count says so); takes insurance from +3 |
| `cautious` | `safe` | the minimum | never busts: hits only hard 11 or less and soft 17 or less, and never doubles, splits or insures |
| `mimic` | `mimic` | the minimum | like the dealer: hits below 17, and soft 17 when the dealer does; never doubles, splits or insures |
| `gambler` | `gambler` | Martingale: the minimum, doubled after every round it lost chips on and back to the minimum after one it didn't, as far as its chips go | the book, except that on hard 12 to 16 it goes against it on a hunch one time in eight, and takes insurance one time in four |

With at least one bot at the table, setup asks "Bot types?". Answer with one kind for all of them,
`random` for a mix, or a comma-separated list with one entry per bot in seat order, such as
`counter, gambler`; `random` works inside a list too, and anything else is refused and asked again.
The default is `basic`. The mix and the gambler's hunches are drawn from the session seed, so the
same seed seats the same bots and they make the same choices.

Each bot's tag shows in grey under its name on the table. The cash-out summary adds a row per bot,
such as `Bot 1 (counter)`, with its net against the chips it started with; a bot that went broke and
left the table is still listed.

## Replaying a session

Every session shuffles from a seed: the one `--seed` gives, or one picked at random. Each shoe is
shuffled from that seed and its own number, so shoe 3 of seed 42 is the same shoe however shoes 1
and 2 were played. The shuffle messages say which shoe is coming out, and the cash-out summary
lists the seed and the shoes you played with the command that deals them again, such as
`dotnet run --project blackjack -- --seed 42 --shoe 3` (`--shoe` only appears when you started
past shoe 1). Answer setup the same way, since the decks, the number of bots and their kinds decide
who gets which card, and the same cards come out.

## Key bindings

Keys live in `keys.ini` next to the solution (a copy beside the program covers running from Visual
Studio). Edit it, or choose "Change the key bindings?" in setup and press the new keys, which saves
the file. Each action takes one or more keys: a letter or symbol, or a name such as `Space`, `Tab`,
`Up` or `F5`. Enter, Escape, Backspace and the digits are kept for typing bets. A key only has to
differ from the other keys read at the same moment, so hit and lower-bet could share one; the deal
key also declines insurance, so it can't double as the insurance key.

## Layout

- `blackjack/Domain`: `Card`, `Hand`, `Player`, `Shoe`
- `blackjack/Engine`: `Game` (the round state machine), `Rules`, `CardCounter`
- `blackjack/Engine/SideBets`: `ISideBet` and the six bets, `SideBets.All`, `SideBetStake`
- `blackjack/Strategies`: `IPlayerStrategy` and the `TableContext` (rules and count) every decision
  gets, `Move`, `MoveOptions`, the bots (`BasicStrategyBot`, the full multi-deck book built for the
  table's rules, `CounterBot`, `CautiousBot`, `MimicBot` and `GamblerBot`) and `BotType` with
  `Bots`, which builds and names them
- `blackjack/Cli`: the Spectre.Console front end: `TableScreen` draws the table, `ConsoleStrategy`
  is your seat, `FastForward` is the autopilot, `KeyBindings` reads `keys.ini`
- `blackjack.Tests`: xUnit tests, using a stacked shoe to rig hands; the side-bet house edges are
  checked by exact enumeration of a six-deck shoe, and each bot kind's by simulation

```sh
dotnet test
```
