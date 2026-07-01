# Monkify — Sales / B2B document set

> **Status:** Draft — not legal advice. · **Owner:** [OWNER] · **Entity:** [ENTITY]
> Source spec: [`specs/sales-technical-documents.md`](../specs/sales-technical-documents.md) · Trello
> [#27](https://trello.com/c/NFNDRlBk).

This folder holds the **technical / commercial** documents used to sell Monkify as a **SaaS /
managed game license** to casinos. It is the sibling of the **legal** document set in
[`../legal/`](../legal/) and of the pitch-**strategy** card
[#1 (ZowgQHZg)](https://trello.com/c/ZowgQHZg).

## Index

| # | Document | Audience | Confidentiality |
| --- | --- | --- | --- |
| 01 | [Dual-track pitch](01-dual-track-pitch.md) · [PT-BR](01-dual-track-pitch.pt-BR.md) | Casino commercial / BD | Cold-sendable |
| 02 | [Integration overview](02-integration-overview.md) · [PT-BR](02-integration-overview.pt-BR.md) | Casino CTO / integration engineer | **Confidential — shared under NDA** |
| 03 | [Anti-clone playbook](03-anti-clone-playbook.md) · [PT-BR](03-anti-clone-playbook.pt-BR.md) | Internal sales / BD | Internal |

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

## Discipline (applies to every document here)

- **No real PII / contacts / secrets.** Use placeholders (`[OWNER]`, `[ENTITY]`, `[CONTACT_EMAIL]`).
  Any example token/config value is an obvious mock (`MOCK_…`).
- **Operator names are illustrative only** (BetMGM, BetFury, Hub88, SOFTSWISS, …) — **never** claimed
  as Monkify clients.
- **Paid steps are DEFERRED**, never presented as done: lab RNG certification (GLI/BMM), lawyer
  review, trademark registration.
- **Pricing:** setup fee **+** monthly platform fee (figures editable); **no revenue share / NGR**.
- **PDF / slide-deck export is deferred** — Markdown here is the deliverable.
