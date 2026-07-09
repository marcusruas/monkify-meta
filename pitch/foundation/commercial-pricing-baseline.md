# Monkify — Commercial Pricing Baseline (INTERNAL)

> **Internal only — never sent to a supplier.** Customer-facing proposals show **only the recommended
> offer, with fixed values**. This file holds the **negotiation room** behind that offer: the ranges
> Marcus can move within, grounded in real B2B iGaming comparables (research 2026-07-09; sources at the
> end). Rev-share is on **GGR** (industry-standard base; NGR only if a counterparty insists, at a
> higher %).

## The public offer (what's in every proposal)

A single **licensed, fully-operated** model — no menu of options in the docs:

| Component | Public (fixed) value |
| --- | --- |
| One-time setup | **$25,000** — earmarked to **fund independent certification** |
| Monthly (fixed) | **$6,000 / month** — 24×7 support **+ all hosting/infra**, never variable |
| Revenue share | **15% of GGR** |
| SLAs | bug (1h/24h critical; 1bd/5bd standard), operator integration (10bd), features (5bd assess; quarterly releases) |

Everything is stated as **"open to discussion"** — so the negotiation happens verbally, against the
parameters below, and only the agreed number lands in a signed contract.

## Negotiation room (internal — do NOT print)

| Lever | Floor | Target (public) | Ceiling / stretch | Notes |
| --- | --- | --- | --- | --- |
| **Setup fee** | $15,000 | **$25,000** | $50,000 | Framed as cert funding. Can flex down for a big network, up if we certify extra markets. Market band for a single integration is $5–25k. |
| **Monthly (fixed)** | $3,000 | **$6,000** | $10,000 | Covers support + infra. Real infra cost is low-four-figures/mo even at ~10k concurrent users, so $6k is comfortably profitable. Market fixed platform fees run $3–15k/mo. |
| **Rev-share (GGR)** | 10% | **15%** | 20% | Studio content share is 7–20% of GGR (~15% common midpoint). Go toward the ceiling only for exclusivity (below). |
| **Exclusivity (if raised)** | — | not offered by default | +3–5 pts GGR **and** a monthly minimum guarantee | Only if a supplier asks. Category-exclusive on a format with no substitute justifies top-of-band + a floor. |
| **Full-code / IP sale (if raised)** | $1.5M | — | $5M+ (+3–6% GGR residual) | Only if a supplier asks to buy outright. Anchor to iGaming content-IP multiples (~1.5–3× revenue / ~12–20× EBITDA at maturity); NetEnt sold for ~$2.1bn / a 43% premium as the ceiling comp. Structure as upfront + earn-out. |

**Rule:** the alternative models (exclusivity, full-code sale) are **reactive** — surface them only if
the supplier brings them up. The proposal documents present the SaaS licence only.

## Certification stance (transparent — this wording is public)

- Monkify is **provably fair by design**: deterministic, seed-based outcomes each player contributes to
  before the round, with a **published replay/verification method** — any round can be independently
  recomputed and audited. *(True per `Paperwork/Legal documents/04 Fairness & game rules.pdf`.)*
- Independent **lab RNG certification (GLI/BMM) is deferred / not yet in place** — the fairness doc lists
  it as "DEFERRED (pilot-milestone or operator-funded)." **Do not claim "cert-ready."**
- The **setup fee funds certification.** This is the honest, differentiating framing: a cost the buyer
  carries for any new content becomes a funded path to certified go-live.

## SLA definitions (public, in every proposal)

| Area | Commitment |
| --- | --- |
| Critical / game-blocking bug | Acknowledge ≤1h; workaround/fix ≤24h (24×7) |
| Standard bug | Acknowledge ≤1 business day; resolve next release or ≤5 business days |
| New operator integration | Live ≤10 business days from receiving credentials/config |
| New features | Assess ≤5 business days; scheduled releases ≥ quarterly |

These are deliberately achievable commitments; tighten only if a specific supplier requires it (and
price it in).

## Infrastructure reality (why the fixed monthly works)

Real hosting for a real-time multiplayer backend: ~$100–300/mo at 100 concurrent users, ~$500–1,500/mo
at 1k, ~$3k–8k/mo at 10k. The **$6k fixed monthly covers infra + support at meaningful scale** and
stays profitable — which is why we can quote a fixed number instead of a variable pass-through.

## Sources

- Studio/aggregator rev-share bands: Gamingsoft — https://www.gamingsoft.com/blog/2026/06/how-to-negotiate-contracts-with-casino-game-providers/ ; Track360 — https://track360.io/blog/online-casino-business-model-economics-ggr-ngr-2026
- Certification cost/timeline: SDLC Corp — https://sdlccorp.com/post/rng-certification-for-casino-games/ ; gaming-slots.org — https://gaming-slots.org/rng-certification-guide/ ; iTech Labs — https://itechlabs.com/our-services/
- SaaS/integration/support fees: MyGamingLicense — https://www.mygaminglicense.com/blog/igaming-software-cost-key-factors-that-influence-your-platform-investment ; igaming-solution.com — https://igaming-solution.com/guides/igaming-software-cost/
- Exclusivity: iGamingBusiness (Mega Casino / Bragg exclusive) — https://igamingbusiness.com/company-news/mega-casino-launches-exclusive-slot-from-bragg-gaming/
- M&A multiples / NetEnt comp: IB Interview Questions — https://ibinterviewquestions.com/guides/valuation-investment-banking/current-ma-multiples-across-sectors-2025-2026 ; Equidam — https://www.equidam.com/gaming-studio-valuation-complete-guide-2025-game-development-investment/ ; Casino.org (Evolution/NetEnt) — https://www.casino.org/news/evolution-gaming-to-acquire-netent-for-2b-igaming-rivals-partnering/
- Infrastructure cost: OneUptime — https://oneuptime.com/blog/post/2026-02-12-build-a-multi-player-game-backend-on-aws/view ; AWS GameLift — https://aws.amazon.com/gamelift/servers/pricing/
