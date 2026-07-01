# Monkify — Pitch de duas trilhas

> **Versão:** 1 · **Data:** 2026-07-01
> **Público:** Times comerciais / de desenvolvimento de negócios de cassinos
> **Confidencialidade:** Pode ser enviado a frio — não contém código-fonte, credenciais nem
> arquitetura interna.
> **Status:** Rascunho — não constitui aconselhamento jurídico.

## Objetivo

Um one-pager comercial autossuficiente que explica **o que é o Monkify**, as **duas formas de um
cassino integrá-lo** (regulado vs cripto-nativo) e o **modelo comercial SaaS**. Foi desenhado para ser
enviado sozinho e abrir uma conversa; o detalhe técnico mais profundo está no
[Panorama de integração](02-integration-overview.pt-BR.md), compartilhado apenas sob NDA. Para a
estratégia completa de prospecção e revelação em etapas, veja o card de pitch
[#1 (ZowgQHZg)](https://trello.com/c/ZowgQHZg) — este documento o referencia, mas não depende dele.

## O que é o Monkify

O Monkify é um **jogo de cassino não tradicional, pronto para integrar**: os jogadores apostam na
**primeira palavra que um macaco simulado digita**. Um motor comprovadamente justo digita caracteres
aleatórios em tempo real e os transmite ao vivo para todos os jogadores; quando a sequência escolhida
por um jogador aparece, as apostas correspondentes vencem e recebem o pote inteiro. O produto está
**totalmente construído** — backend, web, web-mobile e ferramentas de operação — e o Monkify o
**licencia e opera para você** sob um SLA.

**Por que é diferente:** não é mais um clone de caça-níqueis ou jogo de mesa. É um jogo ao vivo,
compartilhado, um espetáculo com mascote (Edson) e um gancho simples e viral ("aposte na palavra"), já
implementado e em funcionamento.

## Como você integra — duas trilhas

> A diretriz de mensagem em toda conversa: **"você integra o Monkify com a *sua* carteira e as *suas*
> contas de jogador."** Não estamos dizendo "não fazemos pagamentos" — nos adaptamos à forma como
> *você* liquida.

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

## O que o Monkify assume vs o que você assume

| Responsabilidade | Modelo A — Regulado | Modelo B — Cripto-nativo |
| --- | --- | --- |
| Conta e identidade do jogador | **Você** (referenciamos o seu `ExternalPlayerId`) | Carteira do jogador (autocustódia) |
| KYC / AML | **Você** | **Você** (conforme a sua licença exigir) |
| Fundos / saldo do jogador | **Você** (sua carteira) | Saldo on-chain do jogador |
| Controles de jogo responsável | **Você** (opcionalmente aplicados via nosso hook pré-aposta) | **Você** |
| Lógica do jogo, RNG, fairness, transmissão ao vivo | **Monkify** | **Monkify** |
| Hospedagem, monitoramento, correções, suporte ao cliente (do jogo) | **Monkify** | **Monkify** |

O ponto: o Monkify se conecta à sua carteira e ao seu sistema de jogadores existentes. Você mantém a
superfície regulada; nós operamos o jogo.

## Modelo comercial (licença gerenciada SaaS)

- **Taxa de setup** — integração e onboarding, cobrança única. *(valor editável; ex.: `[SETUP_FEE]`.)*
- **Mensalidade da plataforma** — cobre **hospedagem + correções de bugs + suporte ao cliente** do
  jogo. *(valor editável; ex.: `[MONTHLY_FEE]`.)*
- **Desenvolvimento de novas funcionalidades é orçado e cobrado à parte** — por projeto ou por hora;
  não incluído na mensalidade.
- **Prazo mínimo:** 12 meses (editável).
- **Exclusividade** (por território, vertical ou grupo de operadores) é um **adicional pago** opcional
  — nunca gratuito.

> Não há **revenue share / % de NGR** neste modelo — o headline é apenas setup + mensalidade.

Frase de fechamento para o deck: *"Lógica detalhada do jogo, especificações de integração e materiais
de diligência estão disponíveis sob NDA / não-uso para contrapartes sérias."*

## Próximo passo

1. Agende uma call curta de avaliação (~25 min) — veja a sequência de prospecção no card #1.
2. Assine um **NDA mútuo + não-uso** (template em [`../legal/`](../legal/)).
3. Compartilhe o [Panorama de integração](02-integration-overview.pt-BR.md) e faça o aprofundamento
   técnico.

## Controle do documento

- **Fonte de verdade Modelo A/B:** [`README.md`](README.md) (bloco reutilizado na íntegra).
- **Fundamentado em (somente leitura):** n/a — comercial. Preços conforme card #1 (linha de revenue
  share omitida).
- **Etapas pagas adiadas:** certificação de RNG em laboratório (GLI/BMM) — **ADIADA**; apresentada
  apenas como item de roadmap, nunca como concluída.
