# Monkify — Integration overview (launch → bet → settle)

> **Version:** 1 · **Date:** 2026-07-01
> **Audience:** Casino CTO / integration engineer
> **Confidentiality:** **Confidential — shared under NDA.** Contains integration-level technical
> detail. It contains **no** source code, real keys, credentials, or infrastructure topology; every
> example value below is an obvious mock (`MOCK_…`).
> **Status:** Draft — not legal advice.

## Purpose

The technical deep-dive a casino engineering team reads before (or during) the integration call. It
describes the end-to-end **launch → game load → bet → settle** flow at the endpoint level, the two
settlement paths (regulated wallet vs on-chain crypto), and exactly what the operator implements. It
is grounded in Monkify's shipped backend. For fairness/RNG detail see the legal **Fairness & Game
Rules** document; for authorship/ownership see **Proof of Ownership** (both in
[`../legal/`](../legal/)).

## The two integration tracks

<!-- CANONICAL-TRACKS:EN START -->
### The two integration tracks

Monkify integrates with a casino on one of two tracks:

| | **Model A — Regulated** | **Model B — Crypto-native** |
| --- | --- | --- |
| **Buyer example** | BetMGM-style / licensed operators | BetFury-style crypto casinos |
| **Payments** | Operator-owned wallet; Monkify holds **no** player float | Monkify-hosted, on-chain (SOL / USDT / SPL tokens) |
| **Player identity** | Operator's `ExternalPlayerId`, delivered as the launch-token `sub` claim | Player's crypto wallet public key |
| **Settlement** | Monkify calls the operator wallet API — **debit** stake, **credit** winnings, **refund** cancellations (amounts in USD) | On-chain Solana SPL-token transfers |
| **Responsible gaming** | Operator-owned; optional Monkify pre-bet check hook (allow / deny / fail-closed) | Operator-owned; pre-bet hook optional |
| **Integration surface** | Signed launch token **+** operator wallet API **+** optional RG pre-bet hook | Signed launch token **+** player wallet connect |

In **both** tracks the casino signs a short-lived **launch token** (RS256 JWT) that tells Monkify
which operator launched the game and — for Model A — which player it acts for. Monkify takes **no
commission**: winners receive the full pot.
<!-- CANONICAL-TRACKS:EN END -->

## 1. Launch — signed launch token

The casino launches the game by redirecting the player to:

```
https://<monkify-host>/monkify/game/sessions#launch_token=<jwt>
```

- The token is an **RS256 JWT** the casino signs with **its private key**. Monkify validates it
  against the operator's stored **public key** (PEM), with optional key-id for rotation.
- **Claims:** `iss` = your operator id, `sub` = `ExternalPlayerId` (empty for Model B / crypto),
  plus `exp`, `iat`, and `jti` (audit only). `displayName` / `currency` are **not** claims.
- Validation (stateless middleware) checks signature, `exp`/`iat` with **±2 min** clock skew, and —
  when the path carries an operator id — that it matches `iss`. Replay protection is **expiry-only**.
  The operator must exist, be contract-active, and have a stored public key.
- **Every operator-scoped REST call carries `X-Launch-Token`.** The SPA captures the token from the
  URL fragment, strips it from the URL, and sends it on every request.
- **Failures are vague by design.** Any failure — missing, expired, bad signature, unknown operator,
  `iss` mismatch — returns a single generic **HTTP 401**; the real reason and a correlation id are
  logged **server-side only**. The game never reveals contract status (e.g. "expired"). A token that
  expires mid-play takes the same path (re-launch).

Example token payload (**mock**):

```json
{ "iss": "MOCK_OPERATOR_GUID", "sub": "MOCK_EXTERNAL_PLAYER_ID", "iat": 0, "exp": 0, "jti": "MOCK_JTI" }
```

## 2. Game load — read endpoints

All calls send `X-Launch-Token`. Responses are wrapped as `{ data, messages }`.

| Endpoint | Purpose |
| --- | --- |
| `GET /operators/{id}` | Branding + **`paymentModel`** (drives whether the UI does wallet-connect). |
| `GET /sessions/active-types` | The operator's active game types (session parameters). |
| `GET /sessions/{sessionTypeId}` | The active session for a game type (to join and bet). |
| `PATCH /players/preferences` | Upserts the player's display name + preferred currency (render-only). |

The frontend gates wallet UX on `paymentModel`: **Crypto** connects a wallet and pays on-chain;
**Regulated** shows no wallet and settles off-chain.

## 3. Bet — `POST /sessions/{id}/bets`

Request body (fields vary by model):

```json
{ "amount": 0, "choice": "banana", "seed": "MOCK_SEED", "wallet": "MOCK_WALLET_OR_EMPTY", "paymentSignature": "MOCK_SIG_OR_EMPTY" }
```

- **Model A (Regulated):** no `wallet`, no `paymentSignature`; the player is the token `sub`.
- **Model B (Crypto):** paid bets carry the on-chain `paymentSignature` (after a wallet transfer).

The bet handler runs, in this **exact order**:

1. **Resolve the operator** (cached, contract-active) and its payment model.
2. **Shape checks** — free-bet vs paid, malformed (e.g. a crypto paid bet missing `paymentSignature`).
3. **Resolve player identity** — Model B: `wallet` from body; Model A: token `sub`. Upsert the
   `OperatorPlayer`.
4. **Signature validation** (Model B only) — reject a reused `paymentSignature`; validate the
   on-chain payment.
5. **Bet-parameter validation** — duplicate bet (same player + choice + session), session is
   accepting bets, operator still contract-active, choice valid for the session.
6. **Responsible-gaming pre-bet check** (opt-in; see §5) — **after** local validation, **before** the
   debit.
7. **Stake settlement** — Model A: **debit** the operator wallet (see §4), the **last** step before
   persisting; Model B: the validated on-chain payment already moved funds.
8. **Persist** the bet and publish `BetPlacedEvent` (non-lottery).

Because the debit / on-chain check is **last**, an invalid, duplicate, or RG-denied bet is **never
debited or persisted**.

## 4. Settlement — operator wallet API (Model A)

For regulated operators, Monkify holds no float and calls **your** wallet API. You implement three
operations (amounts are **USD decimals**, `currency: "USD"`):

| Operation | When | Called from |
| --- | --- | --- |
| **Debit(bet, amount)** | At bet time — the last step before the bet is accepted | `Monkify.Api` (synchronous) |
| **Credit(bet, amount)** | When a session is rewarded and the player won | Background worker |
| **Refund(bet, amount, reason)** | Post-acceptance cancellation (lottery under-subscription, abrupt end) | Background worker |

- **Idempotency:** the key is the **bet id** (+ operation). A timeout is not retried synchronously;
  the bet-id key guards against a double-debit on any later retry.
- **Debit result → player message.** A failed debit returns a **specific** player-facing message
  mapped from a canonical error code (this is *not* the vague operator-status rule — a player's own
  wallet error is specific):

  | Canonical error | Player sees |
  | --- | --- |
  | `InsufficientFunds` | insufficient funds |
  | `PlayerBlocked` | player blocked |
  | `Duplicate` | duplicate bet |
  | `Timeout` / `Unavailable` | "temporarily unavailable, please try again" |

- **Every successful operation writes a `TransactionLog` row** carrying your returned
  `ExternalTransactionReference`.
- **Crypto (Model B)** is unchanged: reward = on-chain SPL transfer; refund = on-chain refund.
- **Free bets** (amount 0) make **no** wallet calls under any model.

## 5. Responsible-gaming pre-bet hook (optional, Model A or B)

If you want Monkify to enforce your responsible-gaming controls before a bet, you implement a
**pre-bet check** adapter. It is **opt-in per operator** — unconfigured operators skip the check
entirely.

- Monkify calls your check with `{ operatorId, externalPlayerId, amount, currency, sessionId,
  gameType, betId }` **after** local validation and **before** the debit.
- Your response is one of:
  - **Allowed** → the bet proceeds.
  - **Denied(reason)** → the bet is rejected with a **specific** player message. Canonical reasons:
    `SelfExcluded`, `LimitReached`, `CoolingOff`, `KycRequired`, `Blocked`, `Other`.
  - **Unavailable** (timeout / error) → **fail-closed**: the bet is rejected ("temporarily
    unavailable, please try again"). Any transport exception is treated as Unavailable.
- A denied or failed check means the bet is **never debited or persisted**. Audit is **log-only** — no
  Monkify DB table, no player PII stored.

## 6. Run & stream — session lifecycle

Once a bet is placed, the session advances through a message-driven state machine (RabbitMQ):

```
BetPlaced → RunSession → RewardSession        (RefundInvalidBet on failure)
```

- The **typer** runs the game in the background worker and streams over **SignalR**
  (`/Hubs/Sessions`, `/Hubs/RecentBets`): batched characters, current speed, and status transitions
  (winners are announced on `Ended`). SignalR is session-id scoped — no launch token on the socket.
- On `RewardSession`, winners are paid (**Model A** wallet `Credit` / **Model B** on-chain transfer);
  cancellations refund (**Model A** wallet `Refund` / **Model B** on-chain). Every movement is a
  `TransactionLog` row (`Credit` / `Debit` / `Refund`). **No commission** — the full pot goes to
  winners.

## 7. End-to-end sequence (both models)

```mermaid
sequenceDiagram
  participant Casino as Operator/Casino
  participant SPA as Monkify SPA
  participant API as Monkify.Api
  participant RG as RG pre-bet adapter (opt-in)
  participant Wallet as Operator Wallet API (Model A)
  participant Chain as Solana (Model B)
  participant Cons as Monkify background worker

  Casino->>SPA: redirect …#launch_token=<jwt> (RS256, iss/sub)
  SPA->>API: GET /operators/{id} (X-Launch-Token)
  API-->>SPA: branding + paymentModel  (bad/expired token ⇒ 401 "unavailable")
  SPA->>API: GET /sessions/active-types → GET /sessions/{typeId}
  SPA->>API: POST /sessions/{id}/bets (X-Launch-Token)
  API->>RG: pre-bet check (if configured) — allow/deny/fail-closed
  alt Model A (Regulated)
    API->>Wallet: Debit(bet, amount, USD)  — last step, fail-closed
    Wallet-->>API: ok / insufficient-funds / blocked / timeout
  else Model B (Crypto)
    API->>Chain: validate paymentSignature
  end
  API-->>SPA: bet accepted (TransactionLog: Debit)
  API->>Cons: BetPlaced → RunSession → RewardSession (RabbitMQ)
  alt winner — Model A
    Cons->>Wallet: Credit(bet, winnings, USD)  (TransactionLog: Credit)
  else winner — Model B
    Cons->>Chain: SPL token transfer
  end
  opt cancellation / invalid
    Cons->>Wallet: Refund(bet, reason) (Model A) / on-chain refund (Model B)  (TransactionLog: Refund)
  end
```

## 8. What you implement (Model A checklist)

- [ ] **Sign launch tokens** (RS256) with claims `iss` / `sub` / `exp` / `iat` / `jti`; give Monkify
      your **public key**.
- [ ] **Wallet API** exposing **Debit / Credit / Refund** (USD; idempotent on bet id; returns an
      external reference and a canonical error code on failure).
- [ ] *(optional)* **RG pre-bet check** returning Allowed / Denied(reason) / Unavailable.
- [ ] Provide the **`ExternalPlayerId`** you want Monkify to reference per player.

For **Model B** you only sign launch tokens (`sub` empty) and let players connect their wallet.

## Out of scope for this document

- Full OpenAPI for every endpoint (shared separately if needed).
- Real infrastructure topology, credentials, and source.
- Certification promises — see the legal Fairness document; lab certification is **DEFERRED**.

## Document control

- **Model A/B source of truth:** [`README.md`](README.md) (block reused verbatim).
- **Grounded in (read-only):** launch-token validation, operator wallet API (Debit/Credit/Refund,
  unified `TransactionLog`, no commission), the RG pre-bet hook (allow/deny/fail-closed), and the
  session state machine (`BetPlaced → RunSession → RewardSession`, `RefundInvalidBet`).
- **Cross-references:** [`../legal/`](../legal/) — Fairness & Game Rules, Proof of Ownership.
- **Deferred (paid) steps:** lab RNG certification (GLI/BMM) — **DEFERRED**.
