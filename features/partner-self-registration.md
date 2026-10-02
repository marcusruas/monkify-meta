# Feature — Partner self-registration

> Status: **shipped.** Authoritative spec: [`../specs/partner-self-registration.md`](../specs/partner-self-registration.md)
> (merged via `feature/partner-self-registration` on 2026-09-29, in `monkify-backend`, `monkify-front`,
> `monkify-lp` and `monkify-meta`). Trello [#195](https://trello.com/c/fuLdOsMW/195-public-referral-registering-page).
>
> Extends [`referral-cash-out.md`](referral-cash-out.md) (#177) and [`referral-program.md`](referral-program.md)
> (#175). Attribution, the earnings predicate, cash-out and the player roster are **unchanged** — a
> self-registered partner is an ordinary `ReferralPartners` row.

## What it does

Anyone can become a referral partner without the team. On the unlisted `/partner` page a visitor reads
the pitch (**HOW IT WORKS** and **THE RULES**), connects the wallet they want paid to, picks a code, ticks
the partner terms and signs one message. A `ReferralPartner` row is written — live immediately — and they
land signed in on their statement, which always shows **YOUR LINK** (`spacestroker.com/?ref=<code>`) with a
COPY LINK button. Two things on the home page open `/partner?register=1`: the cold hero's
**BECOME A PARTNER** button and, since #199, the **partner card** every visitor sees.

Bespoke partners are still created by hand-run SQL; self-registration is an additional door, not a
replacement.

## Core rules

### Who gets what

- A self-registered partner is inserted with **`SharePercentage = 5`** — 5% **of the house cut**, not of the
  stake — **`Active = true`**, `PayoutWallet` = the registering wallet, `DisplayName` = the code and
  `TermsAcceptedAt` = now. The rate is a code constant (`ReferralSelfRegistration.SharePercentage`), not
  config.
- At the committed 2% house cut that is **0.1% of every paid stake**: a 1 SOL bet earns the partner
  0.001 SOL. The same uniform basis applies to every game type, Hyper Roll included
  ([`partner-player-roster.md`](partner-player-roster.md)).
- ⚠️ There is **no approval step**. Self-referral through alt wallets is possible (only the payout wallet's
  own bets are excluded) and is accepted at ~0.1% of turnover; the terms make it a ground for deactivation,
  which is the existing SQL `Active = false` — history and the earned balance stay untouched.
- **Codes are permanent** and one wallet holds one code. No rename, no second code, no payout-wallet change.

### Code rules (registration only)

- 3–20 characters of `[a-z0-9_-]`, trimmed and **stored lowercased** (`Code` = `NormalizedCode`).
- A committed **reserved list** (`spacestroker`, `chimpout`, `monkify`, `edson`, `admin`, `official`,
  `support`, `house`, `team`, `staff`, `mod`, `partner`, `referral`, `treasury`, `orbit`, …) answers
  "taken", indistinguishable from a real collision.
- ⚠️ `ReferralDirectory.Normalize` (1–32 chars) is **unchanged**, so SQL-created partners outside 3–20
  keep resolving. The stricter bounds live only in `ReferralSelfRegistration.NormalizeForRegistration`.

### The proof and the order of checks

- `POST /referrals/registrations` `{ wallet, code, acceptTerms, nonce, signature }`, public; the signature
  is the authorization. Nonce from the existing `GET /players/nonce` with the new purpose
  **`referral-register`** (never shared with `referral-session`).
- The signed message binds the **normalized code** and the **terms acceptance**:
  `Space Stroker partner registration` / `Wallet: …` / `Code: …` /
  `I accept the Space Stroker partner program terms.` / `Nonce: …` — byte-identical in
  `ReferralMessages.BuildRegistrationMessage` and `usePartnerEarnings.buildPartnerRegistrationMessage`.
- Order: **terms → code shape** (both explicit and unsigned — the rules are public) → **wallet proof**
  (vague on failure; the nonce is consumed only on success) → **house wallet** (treasury, Orbit, house seed,
  referral payout → vague) → **already a partner** → **reserved / taken** → insert.
- ⚠️ "Already a partner" and "taken" ignore `Active`: a deactivated partner's wallet and code stay taken,
  so a deactivated farmer cannot re-register under a fresh active code.
- ⚠️ **The UNIQUE indexes are the guard**, not the directory check (a ≤60 s-stale snapshot that skips
  malformed rows). A `23505` is mapped by constraint name: `IX_ReferralPartners_NormalizedCode` → taken,
  `IX_ReferralPartners_PayoutWallet` → already a partner. Two wallets racing for one code yield one row.
- On success the handler calls `IReferralDirectoryProvider.Invalidate()` — the link attributes on the very
  next bet — and returns the same `ReferralSessionDto` a sign-in does. No `Player` row is created.

### What a caller may learn — the scoped §6 exception

- The register endpoint says **"That code is taken."** and **"This wallet is already a partner. Sign in
  instead."** — only **after** a valid wallet proof, and revealing existence only (codes are public in every
  `?ref=` link), never a name, wallet or share. See [`constraints.md`](../constraints.md) §6.
- It also says the two public rules plainly, before the proof: terms required, and the code shape.
- **Everything else stays vague** (`ReferralRequestInvalid`): a bad signature, an unknown or replayed nonce,
  a house wallet. Sign-in, statement and cash-out refusals are unchanged, and an unknown `?ref=` still
  resolves exactly like no code.

### The figures on screen

- `GET /referrals/program` (public, anonymous, config reads only) returns the self-serve share, the house
  cut, **`earningPerOneSol` computed by `ReferralEarningsService.ApplyShare` itself**, the cash-out minimum
  and cooldown, and the code bounds. The explainer prints only these — no hand-typed number — so it is
  right at whatever house cut the deployment runs.
- `GET /referrals/me` now carries the partner's own **`code`** (read off the directory row, not the cached
  aggregate), for YOUR LINK. It is behind the partner's own session; nothing reaches a player-facing
  contract.

### Limits

- `post:/referrals/registrations` — **5/min per client IP** (committed `IpRateLimiting`, pinned in
  `RateLimitSettingsTests`; POST only so the preflight does not eat the budget). `GET /referrals/program`
  has no rule, like every other cheap public read.

### Frontend and terms

- `/partner` stays unlisted and embed-redirected. Register mode comes from `?register=1` or the page's own
  BECOME A PARTNER button; it shows the pitch **before** a wallet is connected, the form after. Refusals
  are shown as the server's own sentence (`apiErrorMessage`); the client never invents a reason. The
  sign-in path's vague copy is untouched.
- **The register view is a PITCH since #199** (Trello [#199](https://trello.com/c/kGhJBd3t), layout only —
  no request, refusal or figure changed). It was one six-bullet box in a narrow grey frame; it is now a
  frameless title (`BRING PLAYERS. / GET PAID IN SOL.`) over the home page's `HomeSection` panels at the
  home width:
  - the **two headline figures** — the share, and what a 1 SOL bet earns — in an amber frame. ⚠️ Still
    only `GET /referrals/program`'s numbers: when that read fails the panel is **not rendered** and the
    steps fall back to numberless sentences. Never hand-type a figure here.
  - **HOW IT WORKS** (four numbered steps — a sequence, so they keep their numerals) and **THE RULES**.
    Step 01 says **"One code per wallet, and it is yours for good"** — true since #195, never said on the
    page before.
  - **CLAIM YOUR CODE**, the cyan panel beside them (below them under `lg`): the code field with its rule
    **always visible** (it turns red once what was typed breaks it), the link preview and the payout
    wallet as **text**, the terms box and REGISTER. ⚠️ **"Commissions are paid to `<wallet>` (the
    connected wallet)"** is new copy; it is true by construction, because registration stores the signing
    wallet as `PayoutWallet`. With no wallet the same panel holds the connect button, so the pitch never
    dead-ends.
  - ⚠️ A read-only value is **never** put in an input-looking box on this page (owner's rule from #197):
    the link preview here and YOUR LINK on the statement are plain text.
- The home hero's button exists only in the **cold** (wallet-not-connected) hero, so before #199 a
  connected player — every returning player — had no in-app way to the programme. `HomePartnerCard`
  closes that: one panel after the Orbit card, **identical for every visitor** (the page cannot know
  whether a wallet is a partner without a signed session, and must not hint at it — §6), with **static
  copy and no figures**, so `/` makes no new request and the cold home still fetches nothing. Neither
  entry needs an embed gate: `Home` is already in `EMBED_REDIRECT_ROUTES`. Both fire `home_link_click`
  with `link: 'partner'` — ⚠️ the two entries are **not distinguishable** in GA.
- `monkify-lp` `/terms` has a **PARTNER PROGRAM** section (`#partner-program`, the checkbox's link target).
  ⚠️ Unlike the numberless REFERRALS clause it carries figures (5% of the house cut, 0.1 SOL, 60 minutes),
  all from `PARTNER_PROGRAM` in `monkify-lp/src/constants.ts` — a **hand-mirror** of the backend that must
  be kept in step. It does not restate the house-cut percentage.

## Key code touchpoints

**Backend (`monkify-backend`)**

| File | Role |
| --- | --- |
| `SpaceStroker.Domain/Referrals/ReferralSelfRegistration.cs` | Share 5, bounds 3/20, reserved list, `NormalizeForRegistration` |
| `SpaceStroker.Domain/Referrals/Entities/ReferralPartner.cs` | + `TermsAcceptedAt` (nullable) |
| `Migrations/…_AddReferralPartnerTermsAcceptedAt.cs` | One nullable column, no backfill |
| `SpaceStroker.Infrastructure/Handlers/Referrals/RegisterReferralPartner/` | The handler (order of checks, 23505 mapping, invalidate, session) |
| `SpaceStroker.Infrastructure/Handlers/Referrals/GetReferralProgram/` | The explainer's figures via `ApplyShare` |
| `SpaceStroker.Infrastructure/Handlers/Referrals/GetReferralEarnings/` | + `Code` on the statement |
| `SpaceStroker.Infrastructure/Handlers/Players/GetWalletNonce/GetWalletNonceRequest.cs` | + `ReferralRegister` purpose |
| `SpaceStroker.Infrastructure/Services/Referrals/ReferralMessages.cs` | + `BuildRegistrationMessage` |
| `SpaceStroker.Api/Controllers/ReferralsController.cs` | `POST registrations`, `GET program` |
| `SpaceStroker.Common/Resources/ErrorMessages.resx` | `ReferralTermsRequired`, `ReferralCodeInvalid`, `ReferralWalletAlreadyPartner`, `ReferralCodeTaken` |
| `SpaceStroker.Common/spacestrokersettings.json` | The rate-limit rule |

**Frontend (`monkify-front`)** — `src/hooks/usePartnerRegistration.ts` (program load, code validation,
sign + register, `partnerLink`), `src/components/partner/PartnerRegisterPanel.vue` (the pitch + form),
`src/components/home/HomePartnerCard.vue` (the home card, #199),
`src/hooks/usePartnerEarnings.ts` (`buildPartnerRegistrationMessage`, `adoptSession`, exported `toBase64`),
`src/pages/PartnerPage.vue` (register mode, YOUR LINK + COPY), `src/components/home/HomeHero.vue` +
`src/pages/HomePage.vue` (the hero button), `src/services/partnerService.ts`, `src/types/partner.ts`,
`src/hooks/useAnalytics.ts`.

**Landing page (`monkify-lp`)** — `src/pages/terms.astro` (PARTNER PROGRAM), `src/constants.ts`
(`PARTNER_PROGRAM`).

## Tests

- `SpaceStroker.Tests.Integration/ReferralSelfRegistrationFlowTests.cs` — the mandatory happy path
  (register → two referred paid bets stamped with the code **immediately** → round pays → statement shows
  `code`, share 5 and `stake × cut × 5%`), a taken code in another case, **two wallets racing for one code**,
  the same wallet twice, the live 5/min limit, and the program figures. ⚠️ Every test registers through its
  own client IP — the class registers more than five times, and one shared client would throttle itself.
- Unit: `ReferralSelfRegistrationTests`, `RegisterReferralPartnerHandlerTests` (every refusal branch,
  nonce-not-spent on the unsigned refusals, house wallets, deactivated wallet/code), `GetReferralProgramHandlerTests`
  (the example at 2% and 5%), plus `GetReferralEarningsHandlerTests` (`Code`) and `RateLimitSettingsTests`.
- ⚠️ The 23505 mapping has no unit test — the in-memory provider enforces no unique index; the integration
  race test is its only proof. `monkify-front` has no test runner, so the registration message's
  byte-identity is pinned by the integration test rebuilding it.

## Known gaps

- No admin view of self-registered partners — ops reads `ReferralPartners` (a row with `TermsAcceptedAt`
  set is self-registered).
- No profanity filter and no terms versioning; `TermsAcceptedAt` records one acceptance.
- The LP's `PARTNER_PROGRAM` figures can drift from the backend silently; the app's explainer cannot.
