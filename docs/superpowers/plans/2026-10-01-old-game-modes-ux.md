# Better UX/UI in Old Game Modes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cosmic Sprint and Space Jackpot get one shared three-plate console where every key works (word LCD, HELP, RANDOM, three tab keys, lever tag, eight favourite keys) and a tabbed, non-scrolling screen (RACE/DRAW · FAVORITES · WINNERS) with player cards, a countdown splash, race lanes and win / loss / voided / spectator banners; practice mirrors it; the per-type history row carries the winner's public name.

**Architecture:** The duel's pattern: pure views (`Race*.vue`, props in / events out, mountable by practice) with the live logic behind them (`GameConsole.vue` keeps the bet/lever logic, a new `RaceScreen.vue` reads `useGame`). Everything shared between the console and the screen — active tab, favourites, last word, winners rows — is module-scope state, because `GameConsole` is mounted from three places. The near-miss matcher gains a second, separate lane map so the duel and the legacy dock keep reading own-words only. Backend: one nullable DTO field filled in the one place a round becomes a row, plus two cache-key bumps.

**Tech Stack:** Vue 3.2 + TypeScript 5.5 (Vue CLI 5, yarn), maska; .NET 8 (`SpaceStroker.Infrastructure`, xUnit + Shouldly + Moq, Testcontainers).

**Spec:** `monkify-meta/specs/old-game-modes-ux.md` (authoritative; order of authority spec → card #197 → design canvas `https://claude.ai/artifact/QiuF5iL6D7B52rLCHvwsaC`). Card: <https://trello.com/c/QtLMPhjU>.

## Global Constraints

- Worktrees: `C:\Users\kurok\Desktop\monkify\.worktrees\old-game-modes-ux\{monkify-backend,monkify-front,monkify-meta}`, branch `feature/old-game-modes-ux`. Never edit a hub. **No commits** until the owner approves (implement-feature Step 9).
- Scope gate: the new console and screen apply only when `resolveGameArchetype(selectedGame)` is `'race'` or `'lottery'` (`isClassicGame`). Never gate on a name or id.
- **Do not edit** `HyperRollWinFx.vue`, `HyperRollModeDial.vue`, `DuelConsole.vue`, `DuelPlate.vue` or any `Duel*` file. Behaviour reused from them is copied into new files.
- The legacy template branches of `GameConsole.vue` and the race/lottery blocks of `GameTerminal.vue` stay byte-identical (preset types and Hyper Roll still use them — AC2).
- No server message, status flow, SignalR contract, entity or migration changes. No new config key or env var.
- Money on screen: pot and price through `formatGameAmount` / `formatSol` (2 dp), a paid figure through `formatPayout` (≤ 4 dp). Pot mark is `assets/sol.svg`, never the coin.
- No audio added; the victory/lose one-shot in `useGame` is untouched.
- Every key is a working action: no price LCD, no LAST / SAVE / CLEAR / arrow / OK keys.
- Views under `components/game/Race*.vue` that practice mounts (`RacePlate`, `RaceTabs`, `RaceMainTab`, `RaceCountdown`, `RaceLanes`, `RaceResultFx`) must not import `useGame`, `mainService`, a wallet hook or any module singleton.
- Front has no test runner: verification per task is `yarn lint` (0 errors) + `yarn build` from the front worktree, then the browser checks in Task 10. Backend: `scripts/run_tests.bat` and `scripts/run_integration_tests.bat` from the backend worktree (never a bare `dotnet test`).
- Browser storage keys: `space-stroker.favorite-words.<wallet>` (JSON array of exactly 8 entries, lowercase word or `null`) and `space-stroker.race-word.<wallet>`. Every access wrapped in `try/catch`.
- Commit trailer (Step 10 only): `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

## Spec corrections and decisions taken while planning

| # | Spec says | Plan does | Why |
|---|---|---|---|
| P1 | `useFavoriteWords` is "keyed on `useGame().currentPlayerId`" | The composable holds the wallet it was last given (`setWallet(wallet)`); `GameConsole`, `RaceScreen` and `useGame.rememberPlayedWord` sync it | `useGame` must call it on an accepted bet, and `useGame()` cannot be called from inside `useGame` or from an async callback (it registers per-call watchers). Passing the wallet in removes the import cycle. |
| P2 | `useRaceWord().afterBet(word)` "remembers the word" | `afterBet` only clears and refills; the write to storage is `useGame.rememberPlayedWord` | The server-accepted point is the only one that may store a word (decision 10). `placeBet`'s "not refused" flag is true for paths that have not been accepted yet (popup-blocked hand-off). |
| P3 | The banner "opens at `Ended` without [the payout]" | Trigger is exactly the spec's computed (`finishedTyping && status > InProgress && winnerChoice`). `finishedTyping` is only set by `GameTyper` at `RewardForWinnersCompleted`, so in practice the banner opens with the reward frame and the figure counts up from the start | Same gating `HyperRollWinFx` and the duel banner already have. An errored payout never sets `finishedTyping`, so no banner and no claim (AC20). |
| P4 | Tab keys / HELP / RANDOM "keep a 44px tap target through padding" | Hit areas are widened as far as the neighbouring key allows (the three tab keys are 13px apart at 360px); the on-screen tab strip is the comfortable target | Three adjacent 44px targets cannot fit in 40px of plate. Reported at hand-off. |
| P5 | `RaceLanes` renders `<game-typer />` | `RaceLanes` has a `stream` slot; `RaceScreen` fills it with `<game-typer :max-chars>` and practice with `<practice-typer>` | Keeps the view store-free. |
| P6 | Idle resume "the tab returns to `main`" | Return-to-main fires once per round id on `GameStarting`/`InProgress`; a plain rollover into `WaitingBets` leaves the player on their tab | Spec §7.3's rule; a rollover every ~40 s must not eject a player reading WINNERS. |
| P7 | `PracticeGameStore` exposes lane entries | `laneEntries` is an **optional** member, set only by the race store | Avoids touching `usePracticeHyperRoll.ts` / `usePracticeDuel.ts`. |
| P8 | Loss banner colour (canvas: blue frame) | Dim terminal green, per spec §7.11 "no red/blue duel colours" | Spec outranks canvas. |

### Found while implementing (2026-10-01)

| # | Finding | What was built |
|---|---|---|
| I1 | The legacy phone band (`h-[clamp(140px,22dvh,195px)]`) stretched the plate ~4% at 360px (measured 360×162.8 against the 155.9 the art asks for), and the 600–1023px band left the plate against the left edge (433px plate at x = 0 in a 768px band). | `GamePage.vue` sizes both mobile bands for the classic plate: on a phone the width is given and the height follows (capped through `--race-plate-max-w`); the wide band is a full-width strip of set height with the plate centred. Legacy types keep their bands byte-for-byte. |
| I2 | `RacePlate`'s `fitHeight` could not hold the aspect in the practice band, which has a set height AND width. | The prop is `fill`: `PracticeConsole` cuts the largest 970:420 box that fits the band (container query units) and the plate fills it. |
| I3 | `GameTerminal.userWon` reads the adapter wallet only, so a player whose identity came from the mobile deeplink session got a LOSS banner on a win. | `classicUserWon` also accepts the viewer's own bet on the winning word (`currentPlayerId`), for the banner and the classic result lines. The legacy screens' `userWon` is untouched. |
| I4 | Spec §7.6 row 4 offered `ENTER FREE` to a wallet that already holds its one ticket whenever the refilled LCD showed a different word. | The tag also reads `ENTERED` / `BET IS IN` when the round takes no second bet from this player (holder lottery, or `betLimitBlocks`). |
| I5 | The stream strip was pinned to the bottom of a mostly empty screen with few lanes. | It sits inside the lane slot, right under the last lane; its height is reserved from the lane capacity. |
| I6 | Space Jackpot's phone header overflowed (`FREE DAILY DRAW · 22:00 UTC`). | The header wraps to two lines on a phone when the halves do not fit. |

## Review Focus

Inputs the spec implies and no automated test exercises (front has no runner) — each is pinned by a named browser check in Task 10:

1. **A wallet with corrupt or foreign `localStorage`** (`favorite-words` not an array, wrong length, non-string entries) → favourites read as eight empty slots, nothing throws. *(Task 2 guards; Task 10 check Q3.)*
2. **A remembered word that no longer fits the type** (Sprint is 6 chars `a-z0-9`, Jackpot differs) → skipped, never truncated; a favourite key that does not fit twitches the error sound and leaves the LCD alone. *(Task 3; Q8.)*
3. **A Space Jackpot round with hundreds of tickets** → lanes stay at one rebuild per frame, cards and lanes show `+N MORE`, nothing scrolls or clips. *(Task 4 rAF coalescing; Q9.)*
4. **A viewer sitting on FAVORITES/WINNERS when the reward frame lands** → the round still gets its result and banner (main tab stays mounted). *(Task 6; Q4.)*
5. **A history row from an API that predates the field** (`winnerName` absent) → a dash, never `undefined`. *(Task 6; Q5.)*

---

## File structure

**Backend (`monkify-backend/src`)**

| File | Responsibility |
|---|---|
| `SpaceStroker.Infrastructure/Contracts/Games/GameHistoryDto.cs` | + `WinnerName`, copied in `ClonePublic()` |
| `SpaceStroker.Infrastructure/Services/Games/GameHistoryService.cs` | `BetRow` carries `CreatedDate`, `Status`, `DisplayName`, `Wallet`; `BuildRow` names the winner; `PublicName` |
| `SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs` | `GamesHistory:v2:…`, `RecentRounds:v2:…` |
| `SpaceStroker.Tests/UnitTests/Contracts/GameHistoryDtoTests.cs`, `…/Services/GameHistoryServiceTests.cs` | §11.1 |
| `SpaceStroker.Tests.Integration/GamesHistoryFlowTests.cs` | §11.2 |

**Front (`monkify-front/src`)** — new files

| File | Kind | Responsibility |
|---|---|---|
| `assets/game/race/{key-random,label-help,label-random,label-favorites}.png` | art | copied from `monkify-meta/specs/assets/old-game-modes-ux/` |
| `utils/randomWord.ts` | pure | `alphabetFor`, `randomWord`, `fitsType` |
| `utils/playerCard.ts` | pure | `shortWallet`, `publicName`, `avatarColor`, `avatarInitial`, `RaceCard` |
| `utils/favoriteWords.ts`, `utils/raceWord.ts` | storage | wrapped per-wallet `localStorage` |
| `hooks/useHowToPlay.ts` | derivation | `practiceMode`, `showHowToPlay`, `openPractice` (moved out of `GameTerminal`) |
| `hooks/useRaceTabs.ts` | module state | active tab + return-to-main |
| `hooks/useFavoriteWords.ts` | module state | the 8 slots |
| `hooks/useRaceWord.ts` | module state | last-word refill |
| `hooks/useRaceWinners.ts` | module state | WINNERS rows + local prepend |
| `components/game/RacePlate.vue` | view | the console |
| `components/game/RaceTabs.vue`, `RaceMainTab.vue`, `RaceCountdown.vue`, `RaceLanes.vue`, `RaceFavoritesTab.vue`, `RaceWinnersTab.vue`, `RaceResultFx.vue` | views | the screen pieces |
| `components/game/RaceScreen.vue` | live | wires the views to `useGame` |

Modified: `utils/gameArchetype.ts` (+`isClassicGame`), `types/index.ts`, `hooks/useNearMiss.ts`, `hooks/useGame.ts`, `components/game/GameConsole.vue`, `components/game/GameTerminal.vue`, `pages/GamePage.vue` (only if Task 10's measurement requires), `components/practice/PracticeConsole.vue`, `components/practice/PracticeTerminal.vue`, `hooks/usePracticeGame.ts`, `constants/practiceGame.ts`, `components/guide/sections/GuideHowTheGameWorks.vue`.

---

### Task 1: Backend — the winner's name on the history row

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/GameHistoryDto.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameHistoryService.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Contracts/GameHistoryDtoTests.cs`, `src/SpaceStroker.Tests/UnitTests/Services/GameHistoryServiceTests.cs`, `src/SpaceStroker.Tests.Integration/GamesHistoryFlowTests.cs`

**Interfaces:**
- Produces: `GameHistoryDto.WinnerName : string?` (wire: `winnerName`); `GameHistoryService.PublicName(string? displayName, string? wallet) : string?` (public static).

- [ ] **Step 1: Write the failing unit tests** (names from spec §11.1)

`GameHistoryDtoTests`: `ClonePublic_CarriesTheWinnerName`.

`GameHistoryServiceTests` — a `Player(string wallet, string? displayName)` helper seeded into `context.Players`, bets created with that player's id:

```csharp
[Fact] public async Task GetHistory_WinnerName_IsTheDisplayName_WhenTheWinnerHasOne()
[Fact] public async Task GetHistory_WinnerName_IsTheShortWallet_WhenTheWinnerHasNoDisplayName()   // "7xKf…9aQ2"; full wallet nowhere in the row
[Fact] public async Task GetHistory_WinnerName_IsNull_WhenNobodyBetTheWinningChoice()            // WinnerCount == 0
[Fact] public async Task GetHistory_WinnerName_IsNull_OnAFreeBetOnlyRound()
[Fact] public async Task GetHistory_WinnerName_HolderRound_NamesTheTicketHolder()                // Amount 0, EntryKind Holder
[Fact] public async Task GetHistory_WinnerName_SeveralWinners_NamesTheEarliestBet()              // WinnerCount == 2
[Fact] public async Task GetHistory_WinnerName_IsServedFromThePublicCache_ToAnotherCaller()      // real in-memory cache, second callerPlayerId
[Fact] public async Task GetRecentRounds_CarriesTheWinnerName()
[Theory] public void PublicName_TrimsTheDisplayName_AndFallsBackToTheShortWallet(string? name, string? wallet, string? expected)
```

- [ ] **Step 2: Run them, confirm they fail** — `scripts\run_tests.bat --filter "FullyQualifiedName~GameHistory"` → compile error on `WinnerName` / `PublicName`.

- [ ] **Step 3: Implement**

```csharp
// GameHistoryDto
/// <summary>#197 - the public name of the round's winner … (spec §4 text)</summary>
public string? WinnerName { get; set; }
// ClonePublic(): WinnerName = WinnerName,

// GameHistoryService.LoadBetsAsync projection
.Select(b => new BetRow {
    GameId = b.GameId, Choice = b.Choice, Amount = b.Amount, EntryKind = b.EntryKind,
    CreatedDate = b.CreatedDate, Status = b.Status,
    DisplayName = b.Player != null ? b.Player.DisplayName : null,
    Wallet = b.Player != null ? b.Player.ExternalPlayerId : null,
})

// BuildRow, after `payable`
var winner = payable
    .Where(b => b.Status != BetStatus.Voided
             && string.Equals(b.Choice, game.WinningChoice, StringComparison.OrdinalIgnoreCase))
    .OrderBy(b => b.CreatedDate)
    .FirstOrDefault();
// … WinnerName = winner == null ? null : PublicName(winner.DisplayName, winner.Wallet),

public static string? PublicName(string? displayName, string? wallet)
{
    if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
    if (string.IsNullOrWhiteSpace(wallet)) return null;
    return wallet.Length <= 10 ? wallet : $"{wallet[..4]}…{wallet[^4..]}";
}

// CacheHelper
=> new($"GamesHistory:v2:{parametersId}:{pageNumber}:{pageSize}", TimeSpan.FromMinutes(5));
=> new($"RecentRounds:v2:{take}", TimeSpan.FromSeconds(30));
```

Confirm the real property names on `Bet` (`Player`, `PlayerId`, `Status`) and `Player` (`DisplayName`, `ExternalPlayerId`) before writing; if `WinnerCount` does not exclude `BetStatus.Voided` today, leave `WinnerCount` exactly as it is (the spec changes only the name).

- [ ] **Step 4: Run the unit suite** — `scripts\run_tests.bat` → all green.

- [ ] **Step 5: Integration test** — `HappyPath_SettledRound_HistoryRowCarriesTheWinnersName` in `GamesHistoryFlowTests.cs`, on the existing dedicated two-choice preset type: two paid bets, one player given a display name through the fixture's existing display-name path (or directly on the `Players` row via `CreateConsumersScope`), wait for `RewardForWinnersCompleted`, `GET /history` as the bettor and again as a different wallet. Assert `WinnerName` equals the winner's display name when they have one and `wallet[..4] + "…" + wallet[^4..]` otherwise; both calls return the same `WinnerName`; `MyWon` differs per caller.

- [ ] **Step 6: Run** `scripts\run_integration_tests.bat --filter "FullyQualifiedName~GamesHistoryFlowTests"`, then the full integration suite in the background.

---

### Task 2: Front foundations — sprites, pure utils, types

**Files:** Create `assets/game/race/*.png`, `utils/randomWord.ts`, `utils/playerCard.ts`, `utils/favoriteWords.ts`, `utils/raceWord.ts`. Modify `utils/gameArchetype.ts`, `types/index.ts`.

**Interfaces — Produces:**

```ts
// utils/gameArchetype.ts
export const isClassicGame = (game?: ActiveGame | null): boolean   // archetype 'race' | 'lottery'

// utils/randomWord.ts
export const alphabetFor = (type?: GameCharacterType | null): string   // digits | a-z | a-z0-9
export const randomWord = (length: number, alphabet: string, unique: boolean): string
export const fitsType = (word: string | null | undefined, game?: ActiveGame | null): word is string

// utils/playerCard.ts
export interface RaceCard { key: string; name: string; word: string; own: boolean; tag: string | null; voided: boolean; color: string; initial: string }
export const shortWallet = (wallet?: string | null): string           // 4…4, whole when ≤ 10
export const publicName = (bet: { displayName?: string | null; externalPlayerId?: string | null }): string  // name → short wallet → 'ANON'
export const avatarColor = (wallet?: string | null): string           // 8-colour palette, first colour with no wallet
export const avatarInitial = (name: string): string
export const OWN_CARD_COLOR = '#fde047'

// utils/favoriteWords.ts
export const FAVORITE_SLOTS = 8
export const readFavoriteWords = (wallet?: string | null): (string | null)[]   // always length 8
export const writeFavoriteWords = (wallet: string | null | undefined, slots: (string | null)[]): void

// utils/raceWord.ts
export const readRaceWord = (wallet?: string | null): string | null
export const rememberRaceWord = (wallet: string | null | undefined, word: string): void

// types/index.ts — GameHistoryRow
winnerName?: string | null;
```

- [ ] **Step 1:** Copy the four sprites. `fitsType`: length ≠ `choiceRequiredLength`, a character outside `alphabetFor(game.gameCharacterType)`, or repeated characters when `acceptDuplicatedCharacters` is false → false. `randomWord` is `DuelConsole.rollRandomWord`'s loop, parameterised. `readFavoriteWords` normalises: not an array → eight nulls; pad/truncate to 8; every non-string or empty entry → `null`; strings lowercased.
- [ ] **Step 2:** `yarn lint` + `yarn build`.

---

### Task 3: Module-scope composables

**Files:** Create `hooks/useHowToPlay.ts`, `hooks/useRaceTabs.ts`, `hooks/useFavoriteWords.ts`, `hooks/useRaceWord.ts`, `hooks/useRaceWinners.ts`. Modify `components/game/GameTerminal.vue` (use `useHowToPlay`).

**Interfaces — Produces:**

```ts
// useHowToPlay — call in setup (uses useRouter + useGame)
export const useHowToPlay = (): { practiceMode: ComputedRef<'duel'|'hyper-roll'|'race'>; showHowToPlay: ComputedRef<boolean>; openPractice: () => void }

// useRaceTabs — call in setup; state is module scope
export type RaceTab = 'main' | 'favorites' | 'winners'
export const useRaceTabs = (): {
  tab: Ref<RaceTab>; setTab: (t: RaceTab) => void;          // setTab plays button_press.mp3
  mainLabel: ComputedRef<'RACE'|'DRAW'>; activeLabel: ComputedRef<string>;
  tabs: ComputedRef<{ id: RaceTab; label: string }[]>
}

// useFavoriteWords — no useGame import (P1)
export const useFavoriteWords = (): {
  slots: Ref<(string|null)[]>; count: ComputedRef<number>;
  setWallet: (wallet: string | null) => void;                // re-reads storage when it changes
  store: (word: string) => void; clear: (index: number) => void
}

// useRaceWord — call in setup
export const useRaceWord = (): { fill: () => void; handToPlayer: () => void; afterBet: () => void }

// useRaceWinners — module rows; RaceScreen drives it
export const useRaceWinners = (): {
  rows: ComputedRef<GameHistoryRow[]>; loading: Ref<boolean>;
  load: (gameTypeId: string, wallet?: string | null) => Promise<void>;
  prependLocal: (gameTypeId: string, row: GameHistoryRow) => void;
  resetFor: (gameTypeId: string | undefined) => void
}
```

- [ ] **Step 1: `useHowToPlay`** — the three bodies moved verbatim from `GameTerminal.vue:329-364`; `GameTerminal` destructures them from the composable (template unchanged).
- [ ] **Step 2: `useRaceTabs`** — per-setup watchers (idempotent writes to module state): reset to `main` on `selectedGame.gameTypeId` change; on `[gameId, status]`, when status is `GameStarting` or `InProgress` and `returnedForGame !== gameId` → `tab = 'main'`, `returnedForGame = gameId`.
- [ ] **Step 3: `useFavoriteWords`** — `store`: lowercase; no-op with no wallet, when already on a key, or with no free slot; else lowest free index, then `writeFavoriteWords`.
- [ ] **Step 4: `useRaceWord`** — `DuelConsole.vue:132-195` without the Orbit precedence: module `autoFilledWord`; `fill()` returns unless `isClassicGame(selectedGame)`; reads `readRaceWord(currentPlayerId)`, requires `fitsType`; writes only into an empty field or one equal to `autoFilledWord`. Watch `[currentPlayerId, gameTypeId, gameInstance.gameId]` → `fill` (immediate). Watch `userWord`: `now === '' && autoFilledWord !== null && previous === autoFilledWord` → `fill`. A wallet switch that leaves a word this composable filled and has no word for the new wallet clears the field.
- [ ] **Step 5: `useRaceWinners`** — `load` = `mainService.getGamesHistory(typeId, { pageNumber: 1, pageSize: 6, wallet })` with a request-sequence guard; failure sets `err.handled = true` and keeps rows. `rows` = local rows not present in the server page, then the server page, sorted by `endedAt` descending, de-duplicated by `gameId` (server row wins).
- [ ] **Step 6:** `yarn lint` + `yarn build`.

---

### Task 4: Lanes — the matcher and the feed

**Files:** Modify `hooks/useNearMiss.ts`, `hooks/useGame.ts`.

**Interfaces — Produces:**

```ts
export interface LaneEntry { choice: string; away: number; matched: string; L: number; own: boolean; order: number; bestAway: number }
// useNearMiss() additionally returns
trackLane: (choice: string, L: number, meta: { own: boolean; order: number }) => void   // idempotent per choice; upgrades own:false → true
setLanes: (list: { choice: string; own: boolean; order: number }[], L: number) => void   // adds missing, drops absent, keeps the rest's progress
laneEntries: ComputedRef<LaneEntry[]>
```

- [ ] **Step 1: `useNearMiss`** — a `lanes` Map beside `attempts` with `displayK`, `bestK`, `lastImproveAt`. `feed`, the stall loop, `finish` (types the winning lane out whoever owns it) and `reset` run over both. `rebuildLanes` is coalesced to one per `requestAnimationFrame`. `nearMissEntries` is unchanged.
- [ ] **Step 2: `useGame`**
  - `reseedLanesFromInstance()` — classic only: `nearMiss.setLanes(bets.filter(b => !b.voided).map((b, i) => ({ choice, own: b.externalPlayerId === me, order: i })), L)`. Called next to both `reseedOwnBetsFromInstance()` calls and after `refreshHolderRoundBets` merges.
  - `onBets` — classic only: `nearMiss.trackLane(newBet.choice, L, { own, order: bets.length - 1 })`.
  - `rememberPlayedWord(choice)` — returns unless `isClassicGame(selectedGame.value)` and a wallet; `favorites.setWallet(wallet); favorites.store(choice); rememberRaceWord(wallet, choice)`. Called after `recordOwnChoice(choice)` in `submitBet` and after `recordOwnChoice(body.choice)` in `placeFromOrbit`. Not in `onBets`, not in the reseed.
  - `myWinningWalletBet` — `(b.amount > 0 || isHolderLottery(selectedGame.value) || gameInstance.value?.holderOnly === true) && !b.viaOrbit && !b.voided`.
- [ ] **Step 3:** `yarn lint` + `yarn build`.

---

### Task 5: The console — `RacePlate.vue` and its wiring

**Files:** Create `components/game/RacePlate.vue`. Modify `components/game/GameConsole.vue`.

**Interfaces — Produces** (`RacePlate`): props `mobile`, `wide`, `fitHeight`, `word`, `wordLength`, `mask`, `inputMode`, `placeholder`, `wordDisabled`, `orbitLockChoice`, `helpEnabled`, `randomDisabled`, `tabs`, `activeTab`, `tabsDisabled`, `leverSrc`, `leverTag`, `leverTagClass`, `favorites: { word: string | null; state: 'empty'|'saved'|'loaded'|'clear' }[]`, `favoritesDisabled`, `walletPopupBlocked`. Events `update:word`, `focus-change`, `enter`, `lever`, `help`, `random`, `tab`, `favorite`, `retry-wallet`. Exposes `inputEl`.

- [ ] **Step 1: `RacePlate.vue`** — root `.race-console` copies `DuelPlate`'s sizing contract (`container: racecon / inline-size; width: auto; max-width: 100%; aspect-ratio: 970 / 420; background-size: 100% 100%`; class by `fitHeight` / `mobile` / `wide` / desktop exactly as `DuelPlate.vue:186-195`), background `terminal.png`. Every control is absolutely positioned at `px / 696` × `px / 301` percentages from spec §7.4's table. Word LCD = a real `<input v-maska>` over `duel/lcd-word.png`, VT323 `#6af594`, font `max(16px, …)` on `mobile`. Keys 1-8: art by state (`-gray` 55% / `-blue` / `-gold` + one art-pixel down / plain red). `OrbitBetLock` over the LCD screen when `orbitLockChoice`. Lever tag in `cqi` units like `DuelPlate`'s.
- [ ] **Step 2: `GameConsole.vue`** — add `isClassic`, `racePlate` ref, `liftInput = computed(() => isClassic ? racePlate.value?.inputEl ?? null : wordInput.value)` handed to `useKeyboardDismiss`; classic `leverTag` rows (spec §7.6) before the `isHolderLottery` line; `raceLeverSrc` (rests on `leverdown` while the tag is `LIVE`); favourites mapping and `onFavorite`; `onRandom`; in `placeBet`, a `refused` flag set inside `cbBetFail` and `if (isClassic) { if (!refused) raceWord.afterBet(); } else userWord = ''`. Template: one `<race-plate v-else-if="isClassic" …>` branch after `<DuelConsole>`; nothing below it changes.
- [ ] **Step 3:** `yarn lint` + `yarn build`; open Cosmic Sprint on desktop and confirm AC1 against `Main.dc.html`.

---

### Task 6: The screen — tabs, main tab, countdown, lanes, favourites, winners

**Files:** Create `RaceTabs.vue`, `RaceMainTab.vue`, `RaceCountdown.vue`, `RaceLanes.vue`, `RaceFavoritesTab.vue`, `RaceWinnersTab.vue`, `RaceScreen.vue`. Modify `GameTerminal.vue`.

**Interfaces — Produces:**

```ts
// RaceTabs: props { tabs: {id,label}[]; active: string; favoriteCount: number; mobile?: boolean; disabledIds?: string[] }  emits 'select'
// RaceMainTab: props { mobile?, lottery?, headerLeft, headerRight, potLabel, potText, potPaused?,
//   timer: { label: string; groups: { value: string; unit: string }[] } | null,
//   players: { value: string; sub: string } | null,
//   holder: { value: string; badge: string | null; badgeKind: 'ok'|'warn'|'muted' } | null,
//   cardsLabel, cardsCount, cards: RaceCard[], moreLabel: (n: number) => string }   slot default = status lines
// RaceCountdown: props { mobile?, headerLeft, headerRight, title, seconds: number | null, words: { word: string; own: boolean }[], footer }  slot default
// RaceLanes: props { mobile?, headerLeft, headerRight, lanes: LaneEntry-like[], moreLabel }  slots: top, stream
// RaceFavoritesTab: props { mobile?, slots: (string|null)[], hasWallet: boolean }
// RaceWinnersTab: props { mobile?, title, rows: { key, when, word, pot, winner, own }[], footer, loading, }
// RaceScreen: props { mobile?, statusLabel: string, contador?: number, nowTick: number, playerInfo, userWon: boolean, revealedServerSeed: string, paidFigure: number | null }
```

- [ ] **Step 1: views** — colours, sizes and copy from spec §7.7–§7.10 and the artboards. Capacity for cards, lanes and winner rows comes from one small `ResizeObserver` helper per view (`capacity = max(1, floor((height + gap) / (itemHeight + gap)))`), defaults 6 desktop / 4 phone before the first measurement; overflow renders `capacity − 1` items plus the `+N MORE` cell.
- [ ] **Step 2: `RaceScreen.vue`** — builds every prop from `useGame`, `useHolderJackpot`, `useNearMiss`, `useRaceTabs`, `useFavoriteWords`, `useRaceWinners`. Body by status (spec §7.7 table). The main tab is `v-show` (finding F2); FAVORITES and WINNERS are `v-if`. Result lines are today's, unchanged, including the mobile seed-first rule. Winners: load on tab open and once at `RewardForWinnersCompleted`, plus the local prepend.
- [ ] **Step 3: `GameTerminal.vue`** — `isClassic`; form class `isDuel || isClassic ? 'flex flex-col' : ''` and `overflow-hidden` for classic; `<race-screen v-else-if="isClassic" …/>` after the duel block; scroll watchers skip classic.
- [ ] **Step 4:** `yarn lint` + `yarn build`.

---

### Task 7: Banners — `RaceResultFx.vue`

**Files:** Create `components/game/RaceResultFx.vue`. Modify `GameTerminal.vue`.

**Interfaces — Produces:** props `mobile`, `trigger: string | null`, `kind: 'win'|'loss'|'voided'|'spectator'`, `mode: 'race'|'lottery'`, `word`, `payout: number | null`, `stake: number`, `winnerName: string | null`, `pot: string`, `away: number | null`, `minimum: string`, `nextDraw: string`, `tokenSymbol`.

- [ ] **Step 1:** Banner copy and rules from spec §7.11's table. Win: 5 000 ms, others 3 000 ms, leaving 350 ms before hiding. Coin shower = a copy of `HyperRollWinFx`'s canvas with a fixed count (60 race / 95 lottery, ÷ 3 under `html.light-graphics`); shake on win. Reduced motion: no coins, no shake, payout set at once. The payout line counts up when `payout` becomes truthy (watch on the prop while visible). `role="status"`, no audio.
- [ ] **Step 2: `GameTerminal`** — `raceFxTrigger` (round id when `isClassic && finishedTyping && status > InProgress && !!winnerChoice`), `raceFxKind` (voided → win → loss → spectator), props from live state (`away` = smallest own `bestAway`, omitted when it equals the word length). Mounted beside `HyperRollWinFx`.
- [ ] **Step 3:** `yarn lint` + `yarn build`.

---

### Task 8: Practice

**Files:** Modify `hooks/usePracticeGame.ts`, `constants/practiceGame.ts`, `components/practice/PracticeConsole.vue`, `components/practice/PracticeTerminal.vue`.

- [ ] **Step 1: store** — a second matcher for the opponent's word created when its bet lands; `laneEntries?: ComputedRef<PracticeLaneEntry[]>` (own first); `finishNearMiss` types out the winner's lane.
- [ ] **Step 2: constants** — `PRACTICE_RACE_BET_IN_LINE = '> YOUR BET IS IN! GOOD LUCK.'`, `PRACTICE_RACE_STARTING_LINE = '> RACE STARTING. BETS STILL OPEN.'`, kept word-for-word with the live copy.
- [ ] **Step 3: `PracticeConsole`** — the race face becomes a wrapper (`PracticeDuelConsole`'s shape) around `<race-plate fit-height>`: RANDOM over `PRACTICE_ALPHABET`, HELP dimmed, tabs disabled with `main` lit, eight `empty` keys disabled, lever tag (`PLAY 0.01` / `BET IS IN` / `LIVE` / `NEXT RACE`); the step-1 arrow placed over the word LCD in plate percentages. The Hyper Roll face is untouched.
- [ ] **Step 4: `PracticeTerminal`** — race branch renders `RaceTabs` (FAVORITES / WINNERS inert), `RaceMainTab`, `RaceCountdown`, `RaceLanes` (stream slot = `<practice-typer>`), the unchanged result lines, and `RaceResultFx` with `kind="win"`, `payout = pot`, trigger `practice-${ownBetSignal}`. The form is a flex column for the race too.
- [ ] **Step 5:** `yarn lint` + `yarn build`; `/game/practice` with the API stopped.

---

### Task 9: Guide copy

**Files:** Modify `components/guide/sections/GuideHowTheGameWorks.vue`.

- [ ] **Step 1:** Add one paragraph after the lead, in the section's existing type styles, no em dashes: three tabs that only show things; the words you play are saved on keys 1 to 8 on this device and both tables share them; a saved key loads its word, and on the FAVORITES tab it clears it; the R key picks random characters; your last word comes back each round.
- [ ] **Step 2:** `yarn lint` + `yarn build`.

---

### Task 10: Verify

- [ ] **Step 1:** Backend `scripts\run_tests.bat`, `scripts\run_integration_tests.bat`. Front `yarn lint`, `yarn build`.
- [ ] **Step 2:** Rebuild the local API from the worktree: `docker compose --project-name monkify-backend up -d --build --no-deps api consumers`; front on `yarn serve --port <verified free>`.
- [ ] **Step 3: browser checks** (spec §13 plus the Review Focus items)
  - Q1 Sprint desktop: AC1 console, RANDOM, two free bets → cards, favourites 1/2 blue, loaded gold, `BET IS IN`.
  - Q2 reload → favourites and last word persist.
  - Q3 corrupt `space-stroker.favorite-words.<wallet>` by hand → eight empty keys, no console error.
  - Q4 sit on WINNERS through a round start → returns to RACE; sit on FAVORITES at the reward frame → result + banner still appear.
  - Q5 WINNERS: newest round on top at once with the winner's name; a row with `winnerName` removed shows `—`.
  - Q6 win / loss / spectator banners; payout only with `> PAID:`.
  - Q7 Space Jackpot: `ELIGIBLE` / `NOT ENOUGH`, `ENTER FREE` → `ENTERED`, same eight favourites as Sprint.
  - Q8 a 6-char Sprint favourite on a type it does not fit → error twitch, LCD unchanged.
  - Q9 inject 200 synthetic lanes (`useNearMiss().setLanes` from the console) → `+N MORE RACING`, no jank.
  - Q10 true 360px and 768px (CDP emulation): plate box measured with `getBoundingClientRect` against its 970:420 aspect, no horizontal scroll, keyboard lift classes toggle on focus/blur.
  - Q11 Hyper Roll, Monkey Duel: screenshot compare against `main` — nothing moved (AC2).
  - Q12 `/game/practice` race with the API stopped → new console, countdown, lanes, win banner.
  - Q13 `prefers-reduced-motion` and `html.light-graphics`.

---

## Self-review

- **Spec coverage:** §4 → T1/T2; §6 → T1; §7.1–7.3 → T3; §7.4–7.6 → T5; §7.7–7.9 → T6; §7.10 → T4+T6; §7.11 → T7; §7.12 → T4; §7.13 → T5+T10 (Q10, with `GamePage.vue` edited only if the measurement fails); §7.14 → T8; §7.15 → T9; §7.16 edge cases → T3/T5/T6 + Q3/Q8; §11 → T1/T10; §13 → T10.
- **Types:** `RaceCard`, `LaneEntry`, `RaceTab` are defined once (T2/T4/T3) and consumed under the same names in T5–T8.
- **Open point carried to hand-off:** finding F1 (a waiting Cosmic Sprint shows `PLAYERS n / m` in the timer's place) and P4 (tap targets).
