# Monkify — Commercial Pricing Baseline (INTERNAL)

> **Internal only — never sent to a supplier.** This is the reasoning and the negotiation room behind
> the customer-facing proposal. Read the reality check first — it's why the public offer is
> deliberately humble.

## Reality check (why the offer is pilot-first)

Monkify is **pre-traction**: no live operators, **no KPIs** (GGR, retention, concurrency), **not
lab-certified**, an unproven single novel format, and **no patent**. In iGaming content licensing,
price is set almost entirely by **proven performance** — so with none of that, we have **very little
leverage** and sit at the bottom of every market band until we can show numbers. The benchmark ranges
below describe **established** studios; they are the ceiling we grow into, not the opening ask.

**Consequence:** lead with the lowest-risk deal that still covers our costs — a **pilot + revenue
share + an infra-covering minimum** — and treat higher rates, exclusivity, and any sale as
**post-traction** conversations.

## The public offer (what's in every proposal)

| Component | Public value | Why |
| --- | --- | --- |
| Start | **Pilot on 1–2 operators** | Removes the "prove it first" objection; generates the KPIs we lack. |
| Revenue share | **12% of GGR** (open, scales) | Modest — a new, uncertified single game realistically lands 8–15%. |
| Monthly minimum | **$2,000/mo**, **credited against rev-share** | This is how we "get money for infrastructure" without a backwards standalone fee. Guarantees infra + support cost is covered in a slow month; a performing partner just pays the share. |
| Setup | **Waived** | New entrant — keep the barrier near zero. |
| Certification | Shared milestone (no cold-pitch fee) | We can't credibly charge a big cert fee cold; fund it from proven revenue / jointly once the pilot works. |
| Support | We host + support everything, 24×7 + SLAs | Our main value prop; also the justification for the minimum. |

**Calibrate the $2,000 minimum to the real GCP bill** once known (Cloud Run × api/consumers + Cloud
SQL Postgres + Redis + RabbitMQ + static front). At pilot/low-concurrency scale real infra is likely a
few hundred $/mo; the minimum should cover infra **+ some support time**, and stay low enough not to be
a dealbreaker.

## Negotiation room (internal — do NOT print)

| Lever | Floor | Public start | Stretch (post-traction) | Notes |
| --- | --- | --- | --- | --- |
| **Rev-share (GGR)** | 8% (or lower in a pilot) | **12%** | 15–20% | Only move up once KPIs prove the game earns; an aggregator may also take a slice of our share. |
| **Monthly minimum** | $0 (waive in pilot) | **$2,000** | up to real infra + support cost | It's cost recovery, not profit — don't oversell it. Can be waived for the pilot to win the deal, then switched on. |
| **Setup** | $0 | **$0** | small integration fee only if a partner expects one | Generally waive. |
| **Certification** | — | shared milestone | co-fund / studio-funded from revenue | Cert is often the studio's cost of entry to regulated listings; be ready to fund it ourselves once revenue supports it. |

## Post-traction / aspirational (NOT for cold pitches)

These need real KPIs first. Kept in `commercial-alternatives-internal.md`:
- **Higher rev-share / exclusivity** — only credible once the game demonstrably earns; exclusivity can
  then command top-of-band + a real minimum guarantee.
- **Full-code / IP sale** — **not** a cold-pitch item. Pre-revenue it's a low-to-mid six-figure deal or
  an acqui-hire, **not** the $1.5M–5M revenue-multiple range (those need revenue/EBITDA we don't have).

## Model direction (get this right per channel)

- **Aggregator (SOFTSWISS, Hub88, Playtech/POP):** money flows operator → aggregator → us; we earn our
  **% of GGR**. Aggregators don't normally pay a studio a standalone monthly — hence the minimum is
  framed as **credited against rev-share** (a floor, not a tax), justified by us hosting + supporting
  everything.
- **Direct operator (a single casino — BC.Game, Roobet, etc.):** a managed-service arrangement is more
  natural; the same rev-share + infra-minimum works and a modest standalone monthly is easier to justify.

## Market comparables (context for the above)

- Studio content rev-share **7–20% of GGR** (~15% common for *established* content); aggregators add a
  distribution layer and often take part of the studio's share. — Gamingsoft; Track360.
- RNG/PF certification **~$15k–$50k per market**, ~2–5 months. — SDLC Corp; gaming-slots.org; iTech Labs.
- SaaS/integration fees ($5–25k setup, $3–15k/mo) exist but fit the **direct-operator / platform** model,
  not content-in-an-aggregator. — MyGamingLicense; igaming-solution.com.
- M&A multiples (1.5–3× revenue / 12–20× EBITDA; NetEnt ≈ $2.1bn) apply to companies **with revenue** —
  irrelevant to a pre-revenue studio's sale price. — IB Interview Questions; Equidam; Casino.org.
- Infra for a real-time multiplayer backend: ~$100–300/mo @100 CCU, ~$500–1,500/mo @1k, ~$3–8k/mo @10k. —
  OneUptime; AWS GameLift.

## Sources
- https://www.gamingsoft.com/blog/2026/06/how-to-negotiate-contracts-with-casino-game-providers/ ; https://track360.io/blog/online-casino-business-model-economics-ggr-ngr-2026
- https://sdlccorp.com/post/rng-certification-for-casino-games/ ; https://gaming-slots.org/rng-certification-guide/ ; https://itechlabs.com/our-services/
- https://www.mygaminglicense.com/blog/igaming-software-cost-key-factors-that-influence-your-platform-investment ; https://igaming-solution.com/guides/igaming-software-cost/
- https://ibinterviewquestions.com/guides/valuation-investment-banking/current-ma-multiples-across-sectors-2025-2026 ; https://www.equidam.com/gaming-studio-valuation-complete-guide-2025-game-development-investment/ ; https://www.casino.org/news/evolution-gaming-to-acquire-netent-for-2b-igaming-rivals-partnering/
- https://oneuptime.com/blog/post/2026-02-12-build-a-multi-player-game-backend-on-aws/view ; https://aws.amazon.com/gamelift/servers/pricing/
