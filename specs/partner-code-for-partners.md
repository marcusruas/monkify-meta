# Space Stroker — "Partners can link a partner code" — Implementation Spec

> **Status: Implemented** on `feature/partner-code-for-partners` (`monkify-backend` + this doc in `monkify-meta`).
> `monkify-front` and `monkify-lp` NOT affected. Requested directly by the owner on 2026-10-27 (no Trello card).
> Extends [`partner-code-on-profile.md`](partner-code-on-profile.md) (#213). Level: **`Feature`**.
>
> **Purpose:** a partner (a wallet that is some `ReferralPartners.PayoutWallet`) can link **another**
> partner's code from their profile, like any other player. Two partners can never link each other: if
> partner Y carries X's code, X cannot take Y's.

## 1. Summary

The profile link never refused partners, but the `?ref=` paths (`POST /games/{id}/bets`,
`POST /orbit/session`) had no self check: a partner who bet through their own YOUR LINK got **their own code**
written to their wallet. Because `Player.ReferralCode` is write-once, that hid **+ PARTNER CODE** for good, and it
never earned anything (`EarningBets` excludes the PayoutWallet's own bets). This card:

1. Moves both partner rules into `IPlayerReferralWriter` (the single writer), so **every** path enforces them.
2. Clears the existing self-links once with a migration.
3. Adds the mutual-pair refusal, with a specific message on the profile and a silent no-link on `?ref=`.

## 2. Constraints

- Write-once stays. Nothing changes a code that is already linked, including existing mutual pairs from before this
  card (they stay; only new ones are refused).
- `Bet.ReferralCode` history is never rewritten.
- §6 vagueness is unchanged: the new message is answered only after a valid partner-code wallet proof, like the
  four #213 messages.

## 3. Resolved product decisions (owner, 2026-10-27)

| Question | Decision |
|---|---|
| What does "partners can't add a code" change? | Partners can link another partner's code; no reciprocal pairs. Not an override of `?ref=` attribution. |
| Partners already stuck with their own code? | Cleared by an EF migration. |
| Does the mutual rule cover `?ref=` too? | Yes, every path. `?ref=` refuses silently (same as an unknown code). |

Only **direct** pairs are refused. Longer chains (X→Y→Z→X) are allowed: commission is single-level, so a chain
pays nobody twice.

## 4. Backend behaviour

### 4.1 `IPlayerReferralWriter.TryLinkAsync` → `ReferralLinkResult`

Returns `Linked | AlreadyLinked | OwnCode | MutualPartners` (was `bool`).

1. Look up the caller's own partner code (`ReferralPartners.PayoutWallet == wallet`; unique, so at most one).
2. Not a partner → the unchanged write-once update/insert (no lock).
3. Own code == the code → `OwnCode`.
4. Otherwise, inside a transaction holding `pg_advisory_xact_lock(PlayerReferralWriter.AdvisoryLockKey)` (5s
   `lock_timeout`): if the code's partner wallet's `Player.ReferralCode` == the caller's own code → `MutualPartners`;
   else the write-once update/insert.
5. ⚠️ The lock is what makes the mutual rule hold under concurrency: two partners cross-linking at the same instant
   would each miss the other's uncommitted row. Only partner wallets take it, so the bet path for ordinary players
   is untouched.
6. The insert path uses a savepoint when a transaction is current, so its 23505 fallback can still run the
   conditional update.

### 4.2 Callers

- `LinkPartnerCodeHandler`: `OwnCode` → `PartnerCodeOwnCode` (its earlier directory check stays), `MutualPartners` →
  new `PartnerCodeMutual` "That partner already uses your partner code, so you can't use theirs.",
  `AlreadyLinked` → `PartnerCodeAlreadyLinked`.
- `RegisterBetHandler`, `CreateOrbitSessionHandler`: result ignored (silent), as before.

### 4.3 Migration `ClearPartnerSelfReferrals`

```sql
UPDATE "Players" p SET "ReferralCode" = NULL, "ReferredAt" = NULL
  FROM "ReferralPartners" rp
 WHERE rp."PayoutWallet" = p."ExternalPlayerId" AND rp."Code" = p."ReferralCode";
```

One-way (Down is a no-op). No money moves: those links never earned.

## 5. Frontend

None. The button already shows whenever `partnerCode` is null, and backend errors render verbatim in the dialog.

## 6. Tests

- Unit (`LinkPartnerCodeHandlerTests`): `Partner_CanLinkAnotherPartnersCode`, `MutualPartners_SaysTheyUseYourCode`.
  `InMemoryPlayerReferralWriter` mirrors both rules.
- Integration (`PartnerToPartnerLinkFlowTests`, new, mandatory): Y links X then X→Y is refused through the API; a
  partner's own `?ref=` on a real bet does not stick and another code links after; concurrent cross-links → exactly
  one wins. `PlayerReferralWriterSqlTests` adapted to the enum.

## 7. Acceptance criteria

- [x] A partner wallet can link another active partner's code from the profile.
- [x] A partner's own code is never linked to their wallet, on any path.
- [x] If Y carries X's code, X cannot link Y's (profile: specific message; `?ref=`: silent).
- [x] Existing self-links are cleared on deploy; nothing else changes.
