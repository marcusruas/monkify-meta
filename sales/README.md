# Monkify — Sales / B2B document set

> **Status:** Draft — not legal advice. · **Owner:** [OWNER] · **Entity:** [ENTITY]
> Source spec: [`specs/sales-technical-documents.md`](../specs/sales-technical-documents.md) · Trello
> [#27](https://trello.com/c/NFNDRlBk).

This folder holds the **technical / commercial** documents used to sell Monkify as a **SaaS /
managed game license** to casinos. It is the sibling of the **legal** document set in
[`../legal/`](../legal/) and of the pitch-**strategy** card
[#1 (ZowgQHZg)](https://trello.com/c/ZowgQHZg).

## Index

Docs 01 and 02 come in **two per-model versions** — send the one that matches the buyer's track
(Model A = regulated, Model B = crypto). Doc 03 is single and model-agnostic.

| # | Document | Model A — Regulated | Model B — Crypto | Audience | Confidentiality |
| --- | --- | --- | --- | --- | --- |
| 01 | Pitch | [EN](model-a/01-pitch.md) · [PT-BR](model-a/01-pitch.pt-BR.md) | [EN](model-b/01-pitch.md) · [PT-BR](model-b/01-pitch.pt-BR.md) | Casino commercial / BD | Cold-sendable |
| 02 | Integration overview | [EN](model-a/02-integration-overview.md) · [PT-BR](model-a/02-integration-overview.pt-BR.md) | [EN](model-b/02-integration-overview.md) · [PT-BR](model-b/02-integration-overview.pt-BR.md) | Casino CTO / integration engineer | **Confidential — under NDA** |
| 03 | [Anti-clone playbook](03-anti-clone-playbook.md) · [PT-BR](03-anti-clone-playbook.pt-BR.md) | — single, model-agnostic — | | Internal sales / BD | Internal |

Every document is built on [`_TEMPLATE.md`](_TEMPLATE.md).

## Canonical track definitions (single source of truth)

The **Model A / Model B** block below is the one authoritative definition of the two integration
tracks. **Documents 01–03 reuse it verbatim** — do not fork the wording. When the product changes,
edit it here first, then re-paste into the three documents.

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

<!-- CANONICAL-TRACKS:PT-BR START -->
### As duas trilhas de integração

O Monkify integra com um cassino por uma de duas trilhas:

| | **Modelo A — Regulado** | **Modelo B — Cripto-nativo** |
| --- | --- | --- |
| **Exemplo de comprador** | Operadores licenciados, estilo BetMGM | Cassinos cripto, estilo BetFury |
| **Pagamentos** | Carteira do operador; o Monkify **não** retém saldo do jogador | Hospedado pelo Monkify, on-chain (SOL / USDT / tokens SPL) |
| **Identidade do jogador** | `ExternalPlayerId` do operador, entregue na claim `sub` do launch token | Chave pública da carteira cripto do jogador |
| **Liquidação** | O Monkify chama a API de carteira do operador — **débito** da aposta, **crédito** dos prêmios, **estorno** de cancelamentos (valores em USD) | Transferências de tokens SPL on-chain na Solana |
| **Jogo responsável** | Do operador; hook opcional de verificação pré-aposta do Monkify (permitir / negar / fail-closed) | Do operador; hook pré-aposta opcional |
| **Superfície de integração** | Launch token assinado **+** API de carteira do operador **+** hook pré-aposta opcional | Launch token assinado **+** conexão de carteira do jogador |

Em **ambas** as trilhas o cassino assina um **launch token** de curta duração (JWT RS256) que informa
ao Monkify qual operador abriu o jogo e — no Modelo A — para qual jogador ele age. O Monkify **não**
cobra comissão: os vencedores recebem o pote inteiro.
<!-- CANONICAL-TRACKS:PT-BR END -->

## Single-model profiles (reused verbatim by the per-model docs)

Docs 01 and 02 come in a Model-A and a Model-B version. Each version shows the buyer **only their
track** by pasting the matching profile block below **verbatim**. Edit here first, then re-paste.

<!-- MODEL-A-PROFILE:EN START -->
### Model A — Regulated (single-model profile)

- **Who it's for:** licensed / regulated operators (BetMGM-style).
- **Payments:** your own wallet — Monkify holds **no** player float.
- **Player identity:** your `ExternalPlayerId`, delivered as the launch-token `sub` claim.
- **Settlement:** Monkify calls your wallet API — **debit** the stake, **credit** winnings, **refund** cancellations (amounts in USD).
- **Responsible gaming:** yours; optionally enforced via Monkify's pre-bet hook (allow / deny / fail-closed).
- **Integration surface:** signed launch token **+** operator wallet API **+** optional RG pre-bet hook.

**You keep** the player account, KYC/AML, funds/float, and RG controls. **Monkify runs** the game
logic, RNG/fairness, live streaming, hosting, bug fixes, and support. Monkify takes **no commission**
— winners receive the full pot.
<!-- MODEL-A-PROFILE:EN END -->

<!-- MODEL-B-PROFILE:EN START -->
### Model B — Crypto-native (single-model profile)

- **Who it's for:** crypto-native casinos (BetFury-style).
- **Payments:** Monkify-hosted, on-chain (SOL / USDT / SPL tokens).
- **Player identity:** the player's crypto wallet public key.
- **Settlement:** on-chain Solana SPL-token transfers (reward and refund).
- **Responsible gaming:** yours; Monkify's pre-bet hook is optional.
- **Integration surface:** signed launch token **+** player wallet connect.

**You keep** KYC/AML per your licence and RG controls. **Monkify runs** the game logic, RNG/fairness,
live streaming, on-chain payments, hosting, bug fixes, and support. Monkify takes **no commission** —
winners receive the full pot.
<!-- MODEL-B-PROFILE:EN END -->

<!-- MODEL-A-PROFILE:PT-BR START -->
### Modelo A — Regulado (perfil de modelo único)

- **Para quem é:** operadores licenciados / regulados (estilo BetMGM).
- **Pagamentos:** sua própria carteira — o Monkify **não** retém saldo do jogador.
- **Identidade do jogador:** o seu `ExternalPlayerId`, entregue na claim `sub` do launch token.
- **Liquidação:** o Monkify chama a sua API de carteira — **débito** da aposta, **crédito** dos prêmios, **estorno** de cancelamentos (valores em USD).
- **Jogo responsável:** seu; opcionalmente aplicado via hook pré-aposta do Monkify (permitir / negar / fail-closed).
- **Superfície de integração:** launch token assinado **+** API de carteira do operador **+** hook pré-aposta opcional.

**Você mantém** a conta do jogador, KYC/AML, fundos/saldo e controles de RG. **O Monkify opera** a
lógica do jogo, RNG/fairness, transmissão ao vivo, hospedagem, correções e suporte. O Monkify **não**
cobra comissão — os vencedores recebem o pote inteiro.
<!-- MODEL-A-PROFILE:PT-BR END -->

<!-- MODEL-B-PROFILE:PT-BR START -->
### Modelo B — Cripto-nativo (perfil de modelo único)

- **Para quem é:** cassinos cripto-nativos (estilo BetFury).
- **Pagamentos:** hospedados pelo Monkify, on-chain (SOL / USDT / tokens SPL).
- **Identidade do jogador:** a chave pública da carteira cripto do jogador.
- **Liquidação:** transferências de tokens SPL on-chain na Solana (prêmio e estorno).
- **Jogo responsável:** seu; o hook pré-aposta do Monkify é opcional.
- **Superfície de integração:** launch token assinado **+** conexão de carteira do jogador.

**Você mantém** KYC/AML conforme a sua licença e os controles de RG. **O Monkify opera** a lógica do
jogo, RNG/fairness, transmissão ao vivo, pagamentos on-chain, hospedagem, correções e suporte. O
Monkify **não** cobra comissão — os vencedores recebem o pote inteiro.
<!-- MODEL-B-PROFILE:PT-BR END -->

## Discipline (applies to every document here)

- **No real PII / contacts / secrets.** Use placeholders (`[OWNER]`, `[ENTITY]`, `[CONTACT_EMAIL]`).
  Any example token/config value is an obvious mock (`MOCK_…`).
- **Operator names are illustrative only** (BetMGM, BetFury, Hub88, SOFTSWISS, …) — **never** claimed
  as Monkify clients.
- **Paid steps are DEFERRED**, never presented as done: lab RNG certification (GLI/BMM), lawyer
  review, trademark registration.
- **Pricing:** setup fee **+** monthly platform fee (figures editable); **no revenue share / NGR**.
- **PDF / slide-deck export is deferred** — Markdown here is the deliverable.
