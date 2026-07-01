# Monkify — Pitch (Modelo A — Regulado)

> **Versão:** 1 · **Data:** 2026-07-01
> **Público:** Times comerciais / de desenvolvimento de negócios (operadores regulados / licenciados)
> **Confidencialidade:** Pode ser enviado a frio — não contém código-fonte, credenciais nem
> arquitetura interna.
> **Status:** Rascunho — não constitui aconselhamento jurídico.

## Objetivo

Um one-pager comercial autossuficiente para **operadores regulados / licenciados**. Explica **o que é
o Monkify**, como ele integra na trilha **regulada (Modelo A)** — conectando-se à *sua* carteira e às
*suas* contas de jogador — e o **modelo comercial SaaS**. Foi desenhado para ser enviado sozinho; o
detalhe técnico mais profundo está no [Panorama de integração](02-integration-overview.pt-BR.md),
compartilhado apenas sob NDA. Para a estratégia completa de prospecção, veja o card de pitch
[#1 (ZowgQHZg)](https://trello.com/c/ZowgQHZg).

> Operador cripto-nativo? Veja a [versão Modelo B](../model-b/01-pitch.pt-BR.md).

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

## Como você integra — a trilha regulada

> A diretriz de mensagem: **"você integra o Monkify com a *sua* carteira e as *suas* contas de
> jogador."** Não estamos dizendo "não fazemos pagamentos" — nós liquidamos pela *sua* carteira
> regulada.

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

## Modelo comercial (licença gerenciada SaaS)

- **Taxa de setup** — integração e onboarding, cobrança única. *(valor editável; ex.: `[SETUP_FEE]`.)*
- **Mensalidade da plataforma** — cobre **hospedagem + correções de bugs + suporte ao cliente** do
  jogo. *(valor editável; ex.: `[MONTHLY_FEE]`.)*
- **Desenvolvimento de novas funcionalidades é orçado e cobrado à parte** — por projeto ou por hora.
- **Prazo mínimo:** 12 meses (editável).
- **Exclusividade** (por território, vertical ou grupo de operadores) é um **adicional pago** opcional
  — nunca gratuito.

> Não há **revenue share / % de NGR** neste modelo — o headline é apenas setup + mensalidade.

Frase de fechamento para o deck: *"Lógica detalhada do jogo, especificações de integração e materiais
de diligência estão disponíveis sob NDA / não-uso para contrapartes sérias."*

## Próximo passo

1. Agende uma call curta de avaliação (~25 min) — veja a sequência de prospecção no card #1.
2. Assine um **NDA mútuo + não-uso** (template em [`../../legal/`](../../legal/)).
3. Compartilhe o [Panorama de integração (Modelo A)](02-integration-overview.pt-BR.md) e faça o
   aprofundamento técnico.

## Controle do documento

- **Fonte de verdade Modelo A/B:** [`../README.md`](../README.md) (bloco de perfil reutilizado na
  íntegra).
- **Fundamentado em (somente leitura):** n/a — comercial. Preços conforme card #1 (linha de revenue
  share omitida).
- **Etapas pagas adiadas:** certificação de RNG em laboratório (GLI/BMM) — **ADIADA**; apresentada
  apenas como item de roadmap, nunca como concluída.
