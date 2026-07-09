# Monkify — Operator Integration Guide

> **For the operator's engineering / integration team.** This guide explains exactly what your team
> builds, what you send us to go live, and how to use our game endpoints. It is the working handbook
> behind a Monkify integration.
>
> **Confidential — shared under NDA.** Base URLs, credentials, and your operator id are provisioned at
> onboarding.

## 1. How the integration works

Monkify is delivered as a **multi-tenant service**: one integration serves all of your brands, each
scoped by a signed token you issue. There are only **two directions** of work:

| Direction | Who builds it | What it is |
| --- | --- | --- |
| **You → Monkify** | **You** | A **Wallet API** we call to move money in your ledger (**Debit / Credit / Refund**), and an optional **Responsible-Gaming pre-bet check** endpoint. For crypto sessions you build nothing — you register a token. |
| **Monkify → You** | Monkify | A signed launch flow, a small game **REST API + real-time streams** your client consumes, and an **iframe embed** for your casino. |

Design principle: for standard (fiat) settlement, **Monkify holds no player funds and no player
personal data** — money stays in your wallet and player identity stays in your token. Your regulatory
and financial surface stays on your side.

**The happy path (fiat settlement):**

1. **Launch** — your casino opens the game with a signed launch token.
2. **Bet** — a player stakes → we validate → (optional) call your **RG check** → call your **Wallet
   `Debit`** → accept the bet. A denied/failed debit rejects the bet; nothing is taken.
3. **Play** — the round runs live; the winning sequence appears.
4. **Settle** — on a win we call your **Wallet `Credit`** for each winner (full pot; no commission).
5. **Refund** — a voided round (e.g. an under-subscribed draw) returns stakes via your **Wallet
   `Refund`**.

Every money call is **idempotent on the bet id**, uses **USD** amounts, and expects a transaction
reference back for your reconciliation.

---

## 2. Authentication — the signed launch token

Authentication uses a short-lived **JSON Web Token (JWT), signed with RS256** (asymmetric). **You
sign** it with your private key; **we validate** it with the public key you register with us. No
shared secrets; we never hold your signing key. This token is the security boundary of the whole
integration.

### One-time setup
1. Generate an **RSA key pair** (2048-bit or stronger).
2. Send us the **public key** (PEM). Keep the private key in your secrets manager.
3. Optionally send a **key id** so you can rotate keys without downtime.
4. We return your **operator id** (a GUID).

### Token claims

| Claim | Meaning |
| --- | --- |
| `iss` | Your **operator id** (GUID). Required. |
| `sub` | Your **player id** (your stable reference for the player). |
| `iat` | Issued-at (Unix seconds). Required. |
| `exp` | Expiry (Unix seconds). Keep short — 5–15 min. Required. |
| `jti` | Optional token id (for your audit). |

Example payload:

```json
{ "iss": "8f2b0c9e-1a4d-4b7a-9e3f-6c5d4e3b2a10", "sub": "player-000123",
  "iat": 1782600000, "exp": 1782600900 }
```

### Launching the game
Open (or set the iframe `src` to) the game URL with the token in the URL fragment:

```
https://<game-host>/monkify/game/sessions#launch_token=<JWT>
```

The fragment is never sent to servers or logged. The game captures the token, removes it from the URL,
and keeps it for the session.

### Every game request carries the token
Your client sends the header on every game API call:

```
X-Launch-Token: <JWT>
```

We validate the signature, the `exp`/`iat` (±2 min clock skew), and that the operator is active. **Any
failure returns a generic `HTTP 401`** — never a reason (contract status is never revealed). When
embedded, a 401 triggers a silent re-launch instead of an error page (see §7).

---

## 3. The Wallet API (you build this) — Standard / fiat settlement

For **standard** (fiat, off-chain, USD) sessions, you expose three HTTPS endpoints we call. Money lives
in your ledger; balances, limits, and player data stay entirely with you.

**Ground rules**
- Amounts are **USD decimals** with `"currency": "USD"`.
- **Idempotency key = the bet id** (a GUID) + the operation. A repeated call must return the original
  result and move no additional money.
- Return an **`externalTransactionReference`** on every success (we store it for reconciliation).
- **No commission** — pay the exact amounts we send; the full pot goes to winners.

We authenticate to your endpoints with the scheme agreed at onboarding (typically a bearer API key).

### 3.1 `POST {your-base-url}/debit` — take the stake (at bet time)
Called synchronously when a player places a bet, as the last step before we accept it. A rejected debit
means the bet is **not** placed.

Request:
```json
{ "operatorId": "…", "externalPlayerId": "player-000123", "betId": "…",
  "sessionId": "…", "gameType": "6L Daily Dispatch Draw", "amount": 1.00, "currency": "USD" }
```
Success (HTTP 200): `{ "status": "ok", "externalTransactionReference": "WLT-DEB-99123" }`

Rejection — return a code so we show the player the right message:
```json
{ "status": "error", "error": "InsufficientFunds" }
```

| `error` | Player sees |
| --- | --- |
| `InsufficientFunds` | "Insufficient funds" |
| `PlayerBlocked` | "You can't place this bet right now" |
| `Duplicate` | "Duplicate bet" (you already processed this `betId`) |
| `Timeout` *(or no response in time)* | "Please try again" (we don't auto-retry; the `betId` keeps a later retry safe) |
| `Unavailable` | "Temporarily unavailable, please try again" |

### 3.2 `POST {your-base-url}/credit` — pay a winner (at reward time)
Called once per winning bet for the winner's share of the pot.
```json
{ "operatorId": "…", "externalPlayerId": "player-000123", "betId": "…",
  "sessionId": "…", "amount": 7.00, "currency": "USD", "reason": "SessionReward" }
```
Success: `{ "status": "ok", "externalTransactionReference": "WLT-CRE-99441" }`. Never double-credit the
same `betId`.

### 3.3 `POST {your-base-url}/refund` — return a voided stake
Called for post-acceptance voids (e.g. an under-subscribed lottery). A bet rejected at debit time was
never taken, so it is never refunded.
```json
{ "operatorId": "…", "externalPlayerId": "player-000123", "betId": "…",
  "sessionId": "…", "amount": 1.00, "currency": "USD", "reason": "LotteryUndersubscribed" }
```
Success: `{ "status": "ok", "externalTransactionReference": "WLT-REF-99502" }`. Never double-refund the
same `betId`.

> **Free bets** (amount 0, dev/QA only) make no wallet calls.

---

## 4. Responsible-Gaming pre-bet check (optional — you build this)

An **opt-in** endpoint we call just before a stake is taken, so you can allow or deny a bet on RG
grounds (self-exclusion, limits, cooling-off, KYC, blocked account). We hold no RG state and store no
player data — we ask you and honour the answer. If you don't configure it, it simply doesn't run.

`POST {your-rg-base-url}/pre-bet-check`
```json
{ "operatorId": "…", "externalPlayerId": "player-000123", "betId": "…",
  "sessionId": "…", "gameType": "6L Daily Dispatch Draw", "amount": 1.00, "currency": "USD" }
```
Allow: `{ "decision": "allow" }`
Deny (rejected with a specific message): `{ "decision": "deny", "reason": "SelfExcluded" }`

| `reason` | Player sees |
| --- | --- |
| `SelfExcluded` | "You are self-excluded…" |
| `LimitReached` | "You've reached a limit…" |
| `CoolingOff` | "You're in a cooling-off period…" |
| `KycRequired` | "Please complete verification…" |
| `Blocked` | "This account can't place bets…" |
| *(any other)* | "You can't place this bet right now" |

If your endpoint is unavailable or times out, we **fail closed** — the bet is rejected with a "try
again" message; no debit, no bet. The check runs **before** settlement, so a denied bet is never
charged.

---

## 5. Payment methods

Settlement is chosen **per game type**; the same operator can run both.

| | **Standard (fiat)** | **Crypto** |
| --- | --- | --- |
| Rail | Off-chain, via **your Wallet API** (§3) | On-chain |
| Currency | **USD** | The token's own units |
| Player identity | Launch-token `sub` (no wallet) | Connected wallet |
| What you build | Wallet API (+ optional RG) | Nothing — you register a token |

**Crypto today runs on Solana** (you register a token — its mint, decimals, and payout authority; the
private key is encrypted at rest and never exposed). **We're open to adding more chains/tokens** as you
need them. For crypto sessions the player pays on-chain at bet time and we transfer payouts; there is
no wallet-API call.

---

## 6. Using the game API (your client / lobby)

All calls carry `X-Launch-Token`. Base URL and the real-time host are provided at onboarding.

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/operators/{operatorId}` | Your branding payload (to skin the game) |
| `GET` | `/sessions/active` | Joinable sessions (id, name, method, required stake, choice length, …) |
| `POST` | `/sessions/{sessionId}/bets` | Place a bet — `{ amount, choice, seed }` (fiat); crypto adds wallet + on-chain signature |
| `GET` / `PATCH` | `/players/preferences` | Read/update display name + preferred display currency (display-only; settlement stays USD) |
| `GET` | `/fx/rates` | USD-base FX table for display conversion |

**Real-time streams** (WebSockets, per session id): live typed characters, session status transitions,
the live bet feed, and per-session chat. Your client subscribes per session to render the round and
accumulate the pot.

Errors use a standard response envelope with a `messages` array — player wallet/RG messages are
specific; anything touching operator/contract status is deliberately generic.

---

## 7. Embedding in your casino (iframe)

Run the full game inside your page and react to lifecycle events:

```html
<iframe src="https://<game-host>/monkify/game/sessions#launch_token=<JWT>"
        style="width:100%;height:100%;border:0"></iframe>
```

Your host serving the game must allow framing (no `X-Frame-Options: DENY|SAMEORIGIN`; a permissive
`frame-ancestors`). Listen for messages of the form `{ source: 'monkify', type, payload }`:

| Event | Use |
| --- | --- |
| `ready` | Frame is up. |
| `session-started` | A round began. |
| `bet-placed` | This player staked → refresh their balance (USD). |
| `bet-settled` | This player's result (`won`, amount). |
| `session-ended` | Round finished. |
| `token-expired` | Re-launch: re-set the iframe `src` with a fresh token (the player never dead-ends). |
| `resize` | One-time size hint. |

Payloads never contain the token or any secret. Fiat sessions run fully in-frame; crypto sessions use
a wallet-connect step for the on-chain payment.

---

## 8. What to send us to go live (onboarding checklist)

| # | Item | Notes |
| --- | --- | --- |
| 1 | **Launch public key** (PEM) + optional key id | You sign launch tokens with the private key. |
| 2 | **Wallet API details** — base URL, API key, timeout | For Standard/fiat settlement (Debit/Credit/Refund). |
| 3 | **RG check details** (optional) — base URL, API key, timeout | Only if you want the pre-bet check. |
| 4 | **Crypto token details** (if offering crypto) — mint, decimals, payout account/authority | You register the Solana token; the private key is encrypted at rest. |
| 5 | **Branding assets** | Name/theme/logo to skin the game per brand. |
| 6 | **Test player ids** | For sandbox validation. |

## 9. Testing before production

We provide a **sandbox** and a **mock wallet** (always-allow, echoes references) so your team can drive
the full **bet → reward → refund** loop end to end before wiring your real wallet. Integrate against
the sandbox first, validate the money and RG paths, then switch to your production endpoints.

---

*Questions during integration: Marcus Ruas · marcusruas.andrade@gmail.com*
