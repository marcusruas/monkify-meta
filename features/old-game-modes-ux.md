# Feature — Better UX/UI in the classic modes (Cosmic Sprint + Space Jackpot)

> Status: **shipped.** Authoritative spec: [`../specs/old-game-modes-ux.md`](../specs/old-game-modes-ux.md)
> (merged via `feature/old-game-modes-ux` on 2026-10-05, Trello
> [#197](https://trello.com/c/QtLMPhjU/197-better-ux-ui-in-old-game-modes)). The same branch also carried
> #199 (partner UI, see [`partner-self-registration.md`](partner-self-registration.md)) and the owner's
> review rounds listed under "Rode along" below.

## What it does

Cosmic Sprint and Space Jackpot used to run on the first-generation screen: a scrolling text transcript, a
decorative keyboard and eight dead keys. They now have the same standard as Hyper Roll and Monkey Duel: one
shared console where every key does something, a tabbed screen that never scrolls (round, favourites,
winners), a start countdown, live race lanes, and win / loss / voided / spectator banners. Practice
(`/game/practice`, race) mirrors the new console and screen. Hyper Roll, Monkey Duel and preset-word types
keep their own consoles.

## Core rules

### Console (`RacePlate` on the shared `ConsolePlate`)

- **Left well:** the word LCD (full width), then `?` **HELP** (same destination as the on-screen HOW TO PLAY
  chip; lit only while that chip would show), `R` **RANDOM** (random characters, like the duel's key) and
  three round **tab keys** over a strip naming the active tab. No price LCD: a read-only value in an LCD
  reads as typable, so the price is on the screen header and the lever tag.
- **Lever tag** says what the lever does next: Sprint `PLAY <price>` / `BET IS IN` / `LIVE` / `NEXT RACE`;
  Jackpot `ENTER FREE` / `ENTERED` / `NEXT DRAW`.
- **Right well:** keys 1-8 are **favourite words**, engraved `FAVORITES`. Blue = saved, gold = the word on the
  LCD, grey = empty, red = saved while the FAVORITES tab is open. No wallet → the keys are inert.
- Every key is a working action; there are no LAST / SAVE / CLEAR / arrow / OK keys. Monkey Duel's `?` key
  and Hyper Roll's MIN key also became **HELP**.

### Favourite words and the last word

- **Auto-store:** a new word the player bets goes to the lowest free key once the server accepts the bet;
  storing stops when all 8 are full; a word already on a key is not stored twice; words Orbit bets are not
  stored.
- **Pressing an empty key saves the word on the LCD** without betting it (amended 2026-10-02, AC 12). While
  there is a storable word, empty keys show the save hint: disabled grey → neutral grey (no pulse, no
  movement).
- A saved key loads its word onto the LCD on the main and WINNERS tabs; on the FAVORITES tab it **clears**
  that slot. The FAVORITES tab lists all 8 slots with a notice that keys clear there.
- One list shared by both modes, per wallet, in this browser (`space-stroker.favorite-words.<wallet>`).
- After a round the LCD is refilled with the player's **last bet word** (`space-stroker.race-word.<wallet>`)
  — only into an empty field or one it filled itself, never over typing.
- Storage unavailable or another device → no favourites and no last word; nothing else breaks.

### Screen (`RaceScreen`) — three view-only tabs, nothing scrolls

- Sprint: `RACE` · `FAVORITES` · `WINNERS`. Jackpot: `DRAW` · `FAVORITES` · `WINNERS`. Switched by the tab
  keys or by clicking the tabs. When a round starts the screen returns to the main tab once.
- **Main tab:** a POT plate (gold frame, `sol.svg`), then a timer plate — boxed `RACE STARTS IN` / `DRAW IN`
  digits when a start time exists, otherwise a `PLAYERS n / m` plate (a Sprint still taking bets has no
  server start time). Space Jackpot adds `YOUR $STROKE` with ELIGIBLE / NOT ENOUGH. Below, **player cards**
  in two columns (avatar tile from the wallet, public name, word); the viewer's own card is amber with a
  YOU tag and comes first; overflow ends in `+N MORE`. A voided jackpot ticket shows as voided.
- **Countdown splash** while the round is starting (server `startDate`).
- **Race lanes** during the round: one per word, characters away; the viewer's own lanes pinned on top, the
  rest the closest words re-sorted live, about six, `+N MORE RACING` beyond.
- **Banners** (`RaceResultFx`): bettor won → `YOU WON!` / `JACKPOT!` with a coin shower and the payout
  counting up (the figure only once the paid amount arrives; a rewards error makes no payout claim); lost →
  `NOT THIS TIME` with `-stake` / `NO LUCK TODAY` (no stake); voided ticket → `TICKET VOIDED`; spectator →
  a smaller neutral banner (word, winner, pot), no coins. No banner for a round that never starts. Reduced
  motion: no coins, shake or count-up.
- A Space Jackpot winner also gets the win toast.
- **WINNERS tab:** the latest rounds that fit (about six) — when, word, pot, winner — own wins highlighted;
  titled `> LAST RACES` / `> RECENT DRAWS`. A finished round is prepended locally because the history page
  is cached for 5 minutes.
- **Mode entrance:** switching into a game plays its own short display animation once (Sprint: plates and
  cards glide in; Jackpot: fade-up). See [`frontend-ux.md`](frontend-ux.md).

### Backend — the winner's name on history rows

- `GameHistoryDto.WinnerName`: the earliest non-voided winning bet's player — display name, else the wallet
  shortened four-and-four (`GameHistoryService.PublicName`); null when nobody was paid. The full wallet
  never enters the DTO. Copied by `ClonePublic()`.
- Served by `GET /games/{parametersId}/history` and, through the shared `BuildRow`, the recent-rounds feed.
  Cache keys bumped to `GamesHistory:v2:*` and `RecentRounds:v2:*` because the cached blob changed shape.
  No entity, migration, consumer or SignalR change. An older API simply omits the field; the front shows a
  dash.

### Rode along on the same branch (owner review rounds, 2026-10-02 → 10-05)

- One console plate and size for every game (`ConsolePlate`, `plate.png`) — see `frontend-ux.md`.
- Header game selector (`GameSwitcher`) — see `frontend-ux.md`.
- Hyper Roll key colours ranked per board — see [`hyper-roll-modes.md`](hyper-roll-modes.md).
- Monkey Duel's empty word LCD shows one slot per letter — see [`monkey-duel.md`](monkey-duel.md).
- **Start countdown 10 s → 5 s** (`GeneralSettings:Games:TimeUntilGameStarts` = 5). Shared by Cosmic Sprint
  and Space Jackpot; practice's `countdownMs` (5000) and Edson's glasses beat (3800 ms) follow it.
- **Space Jackpot pot and minimum** — the pot is now `0.03 + 0.005 per full 10 SOL` the treasury holds and
  the holder minimum rounds up on a finer ladder; see
  [`space-jackpot-refactor.md`](space-jackpot-refactor.md).

## Key code touchpoints

- **Backend:** `SpaceStroker.Infrastructure/Contracts/Games/GameHistoryDto.cs`,
  `Services/Games/GameHistoryService.cs`, `Services/Cache/CacheHelper.cs`; tests `GameHistoryDtoTests`,
  `GameHistoryServiceTests`, integration `GamesHistoryFlowTests`.
- **Front, console:** `components/game/ConsolePlate.vue`, `RacePlate.vue`, `GameConsole.vue`.
- **Front, screen:** `components/game/RaceScreen.vue`, `RaceTabs.vue`, `RaceMainTab.vue`,
  `RaceFavoritesTab.vue`, `RaceWinnersTab.vue`, `RaceCountdown.vue`, `RaceLanes.vue`, `RaceResultFx.vue`,
  `GameTerminal.vue`.
- **Front, state:** `hooks/useFavoriteWords.ts`, `useRaceWord.ts`, `useRaceTabs.ts`, `useRaceWinners.ts`,
  `useNearMiss.ts`, `useFitRows.ts`, `useHowToPlay.ts`, `useModeIntro.ts`, `useGame.ts`;
  `utils/favoriteWords.ts`, `raceWord.ts`, `randomWord.ts`, `playerCard.ts`, `gameArchetype.ts`.
- **Practice:** `components/practice/PracticeConsole.vue`, `PracticeTerminal.vue`,
  `hooks/usePracticeGame.ts`, `constants/practiceGame.ts`.
- **Guide:** `components/guide/sections/GuideHowTheGameWorks.vue` (tabs and favourites).
