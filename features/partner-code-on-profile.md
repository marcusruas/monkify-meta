# Feature — Partner code on the profile page

> Status: **shipped.** Authoritative spec: `../specs/partner-code-on-profile.md`
> (merged via `feature/partner-code-on-profile` on 2026-10-27). Trello
> [#213](https://trello.com/c/dGgU5VOY).

## What it does

A player who has no partner yet can link one **once**, from their own profile page. On the hero's
right side a dashed **+ PARTNER CODE** button ("Have a partner code? One time only.") opens a small
dialog: type the code, sign with the wallet, done. From then on every paid bet the player places,
Orbit auto-bets included, earns that partner commission. Bets placed before linking never do. The code
can never be changed or removed afterwards; the button becomes a **PARTNER · CODE** badge with a lock
and "since <date>". It complements `?ref=` attribution ([`referral-program.md`](referral-program.md)),
which only works before a player's first bet or Orbit enrolment.

## Core rules

- **Write-once, from every path.** `Player.ReferralCode` is set only while it is NULL. All three
  writers go through `IPlayerReferralWriter.TryLinkAsync`, which runs one atomic
  `UPDATE … WHERE "ReferralCode" IS NULL` (and creates the `Player` row when missing, retrying the
  update once on a 23505 from a concurrent insert). No player or admin path changes or clears a linked
  code. An ops correction is hand SQL.
- **Partners can link too, but never their own code and never a mutual pair** (partner-code-for-partners,
  2026-10-27, [`../specs/partner-code-for-partners.md`](../specs/partner-code-for-partners.md)). A wallet that is
  some partner's `PayoutWallet` may link another partner's code. The writer itself refuses (so `?ref=` bets and
  Orbit sessions obey it too, silently) the partner's **own** code and a code whose partner wallet already carries
  the caller's code ("That partner already uses your partner code, so you can't use theirs."). Only direct pairs;
  longer chains are allowed. Partner-wallet links take `pg_advisory_xact_lock(PlayerReferralWriter.AdvisoryLockKey)`
  so two partners cannot cross-link at the same instant. ⚠️ Before this, a partner betting through their own
  YOUR LINK got their own code stamped (it never earned) and lost the button for good; migration
  `ClearPartnerSelfReferrals` cleared those rows. Mutual pairs linked before 2026-10-27 were left as they are.
- **Only bets after linking earn.** Nothing new in the earnings code: `RegisterBetHandler` stamps
  `Bet.ReferralCode` from the player row at placement (re-resolved through the directory, so an inactive
  partner stamps NULL). Earlier bets keep `ReferralCode = NULL`. No backfill, no re-stamping.
- ⚠️ **The bet path no longer rewrites the player row wholesale.** `RegisterBetHandler.UpsertPlayerAsync`
  used to save a NoTracking-loaded row with `Players.Update`, writing a stale `ReferralCode = NULL` over
  a code linked a moment earlier. It now writes only `LastSeenAt`/`DisplayName`, links through the
  writer, then re-reads `ReferralCode`/`ReferredAt` before stamping the bet.
  `CreateOrbitSessionHandler.EnsurePlayerExists` also goes through the writer.
- **`POST /players/partner-code`** `{ wallet, code, nonce, signature }` → `{ partnerCode, partnerLinkedAt }`.
  The nonce comes from `GET /players/nonce?purpose=partner-code`. That purpose is never shared with
  `preferences` or `referral-*`. The signed message is five lines joined with `\n`: `Space Stroker partner code` /
  `Wallet: {wallet}` / `Code: {normalized code}` / `I understand this partner code is permanent.` /
  `Nonce: {nonce}` (Base64 ed25519 signature). Rate limit `post:/players/partner-code` 10/min/IP.
- **Check order:** shape (`WalletAddress.IsValid`, `ReferralDirectory.Normalize` — trim, lowercase,
  `[a-z0-9_-]`, 1–32) → wallet proof → house wallet → active streamer wallet → code exists → active →
  not the caller's own `PayoutWallet` → atomic write. Every refusal up to and including the house and
  streamer checks is the **vague** `SelfExclusionRequestInvalid`. After a valid proof (nonce consumed)
  the player may be told: "That code doesn't exist." / "That code is no longer active." / "You can't use
  your own partner code." / "You already have a partner code." A scoped §6 exception like #195's: the
  caller has just proven the wallet, and codes are public in every `?ref=` link anyway.
- The stored value is the partner's **canonical** `Code`, never the user's spelling, because earnings
  match `ReferralCode == partner.Code` exactly.
- **The player-level code is public.** `GET /players/profile` returns `partnerCode` + `partnerLinkedAt`,
  stamped live from the row (no cache invalidation needed after a link), returned as stored even if the
  partner is later deactivated. `Unavailable` and the "no row" body carry nulls. ⚠️ Owner decision,
  2026-10-26: this is the one deliberate exception to #175's "nothing leaks". Bet DTOs, SignalR,
  history, stats, leaderboards and verification stay code-free.
- **Front:** the button and badge render only on the owner's own, non-embedded profile
  (`isOwner && !isEmbedded`); the button is also hidden for streamer wallets (`orbitIsStreamer`).
  Visitors see neither. The dialog (`ArcadeModal` compact) pre-fills the unexpired `?ref=` stash
  (editable), shows backend errors verbatim inline, and treats a rejected signature as a silent no-op
  that leaves the dialog open. Success clears the stash, drops the cached profile, reloads and toasts
  "Partner code linked."
- **Copy says "partner", never "streamer"** (owner rule).
- Self-excluded players are not refused by the backend (linking moves no money), but their profile
  reads Unavailable, so the UI is unreachable. Deeplink phone wallets cannot sign in-page ("This wallet
  cannot sign messages."), same as EDIT NAME.

## Key code touchpoints

**Backend (`monkify-backend`)**

- `SpaceStroker.Api/Controllers/PlayersController.cs` — `POST /players/partner-code` (+ OPTIONS)
- `SpaceStroker.Infrastructure/Handlers/Players/LinkPartnerCode/LinkPartnerCodeHandler.cs` + `LinkPartnerCodeRequest.cs`
- `SpaceStroker.Infrastructure/Services/Referrals/IPlayerReferralWriter.cs` + `PlayerReferralWriter.cs` — the only writer of `Player.ReferralCode`/`ReferredAt`
- `SpaceStroker.Infrastructure/Services/Referrals/ReferralMessages.cs` — `BuildPartnerCodeMessage`
- `SpaceStroker.Infrastructure/Handlers/Players/GetWalletNonce/GetWalletNonceRequest.cs` — `WalletNoncePurpose.PartnerCode`
- `SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` — `UpsertPlayerAsync` (race fix)
- `SpaceStroker.Infrastructure/Handlers/Orbit/CreateOrbitSession/CreateOrbitSessionHandler.cs` — `EnsurePlayerExists`
- `SpaceStroker.Infrastructure/Handlers/Players/GetPlayerProfile/GetPlayerProfileHandler.cs` + `Contracts/Players/PlayerProfileDto.cs`, `PlayerPartnerCodeDto.cs`
- `SpaceStroker.Common/Resources/ErrorMessages.resx` — `PartnerCodeNotFound`, `PartnerCodeInactive`, `PartnerCodeOwnCode`, `PartnerCodeAlreadyLinked`
- `SpaceStroker.Common/spacestrokersettings.json` — the rate-limit rule

**Frontend (`monkify-front`)**

- `src/pages/PlayerProfilePage.vue` — `showPartnerArea`, `canLinkPartner`, button + badge
- `src/components/player/PartnerCodeModal.vue` — the dialog
- `src/hooks/usePartnerCodeLink.ts` — `buildPartnerCodeMessage`, nonce → sign → POST
- `src/services/mainService.ts` (`linkPartnerCode`, `'partner-code'` nonce purpose), `src/types/index.ts`

**Tests** — unit: `LinkPartnerCodeHandlerTests` (16 cases), `RegisterBetHandlerTests.ABetWithoutReferral_DoesNotClearALinkedCode`,
`GetPlayerProfileHandlerTests`, `GetWalletNonceHandlerTests`, `RateLimitSettingsTests`, with
`Shared/InMemoryPlayerReferralWriter.cs` standing in because EF InMemory cannot `ExecuteUpdate`.
Integration: `PartnerToPartnerLinkFlowTests` (partner links another partner's code, reverse refused, own `?ref=`
does not stick, concurrent cross-links → one wins), `PartnerCodeLinkFlowTests` (happy path where only bets after linking earn, second link
refused, unknown code after a valid proof, wrong-purpose nonce is vague) and
`PlayerReferralWriterSqlTests` (the real conditional SQL: set once, create-when-missing, concurrent links).
