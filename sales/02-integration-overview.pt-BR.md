# Monkify — Panorama de integração (launch → aposta → liquidação)

> **Versão:** 1 · **Data:** 2026-07-01
> **Público:** CTO / engenheiro de integração do cassino
> **Confidencialidade:** **Confidencial — compartilhado sob NDA.** Contém detalhe técnico em nível de
> integração. **Não** contém código-fonte, chaves reais, credenciais nem topologia de infraestrutura;
> todo valor de exemplo abaixo é um mock evidente (`MOCK_…`).
> **Status:** Rascunho — não constitui aconselhamento jurídico.

## Objetivo

O aprofundamento técnico que o time de engenharia do cassino lê antes (ou durante) a call de
integração. Descreve o fluxo ponta a ponta **launch → carregamento do jogo → aposta → liquidação** em
nível de endpoint, as duas trilhas de liquidação (carteira regulada vs cripto on-chain) e exatamente o
que o operador implementa. É fundamentado no backend em produção do Monkify. Para detalhe de
fairness/RNG veja o documento legal **Regras do Jogo & Fairness**; para autoria/propriedade veja
**Prova de Propriedade** (ambos em [`../legal/`](../legal/)).

## As duas trilhas de integração

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

## 1. Launch — launch token assinado

O cassino abre o jogo redirecionando o jogador para:

```
https://<monkify-host>/monkify/game/sessions#launch_token=<jwt>
```

- O token é um **JWT RS256** que o cassino assina com **sua chave privada**. O Monkify o valida com a
  **chave pública** armazenada do operador (PEM), com key-id opcional para rotação.
- **Claims:** `iss` = id do seu operador, `sub` = `ExternalPlayerId` (vazio no Modelo B / cripto),
  mais `exp`, `iat` e `jti` (apenas auditoria). `displayName` / `currency` **não** são claims.
- A validação (middleware stateless) checa assinatura, `exp`/`iat` com **±2 min** de tolerância de
  relógio e — quando o caminho carrega um id de operador — que ele bate com `iss`. Proteção de replay
  é **apenas por expiração**. O operador deve existir, ter contrato ativo e uma chave pública.
- **Toda chamada REST com escopo de operador carrega `X-Launch-Token`.** A SPA captura o token do
  fragmento da URL, o remove da URL e o envia em cada requisição.
- **Falhas são vagas por design.** Qualquer falha — ausente, expirado, assinatura inválida, operador
  desconhecido, `iss` divergente — retorna um único **HTTP 401** genérico; o motivo real e um
  correlation id são registrados **apenas no servidor**. O jogo nunca revela o status do contrato (ex.:
  "expirado"). Um token que expira durante o jogo segue o mesmo caminho (re-launch).

Exemplo de payload do token (**mock**):

```json
{ "iss": "MOCK_OPERATOR_GUID", "sub": "MOCK_EXTERNAL_PLAYER_ID", "iat": 0, "exp": 0, "jti": "MOCK_JTI" }
```

## 2. Carregamento do jogo — endpoints de leitura

Todas as chamadas enviam `X-Launch-Token`. As respostas vêm no formato `{ data, messages }`.

| Endpoint | Objetivo |
| --- | --- |
| `GET /operators/{id}` | Branding + **`paymentModel`** (define se a UI faz wallet-connect). |
| `GET /sessions/active-types` | Os tipos de jogo ativos do operador (parâmetros de sessão). |
| `GET /sessions/{sessionTypeId}` | A sessão ativa de um tipo de jogo (para entrar e apostar). |
| `PATCH /players/preferences` | Faz upsert do nome de exibição + moeda preferida (apenas exibição). |

O frontend condiciona a UX de carteira ao `paymentModel`: **Cripto** conecta carteira e paga on-chain;
**Regulado** não mostra carteira e liquida off-chain.

## 3. Aposta — `POST /sessions/{id}/bets`

Corpo da requisição (campos variam por modelo):

```json
{ "amount": 0, "choice": "banana", "seed": "MOCK_SEED", "wallet": "MOCK_WALLET_OU_VAZIO", "paymentSignature": "MOCK_SIG_OU_VAZIO" }
```

- **Modelo A (Regulado):** sem `wallet`, sem `paymentSignature`; o jogador é o `sub` do token.
- **Modelo B (Cripto):** apostas pagas carregam o `paymentSignature` on-chain (após transferência de
  carteira).

O handler de aposta executa, nesta **ordem exata**:

1. **Resolve o operador** (em cache, contrato ativo) e seu modelo de pagamento.
2. **Checagens de formato** — free-bet vs paga, malformada (ex.: aposta cripto paga sem
   `paymentSignature`).
3. **Resolve a identidade do jogador** — Modelo B: `wallet` do corpo; Modelo A: `sub` do token. Faz
   upsert do `OperatorPlayer`.
4. **Validação de assinatura** (só Modelo B) — rejeita `paymentSignature` reutilizado; valida o
   pagamento on-chain.
5. **Validação de parâmetros da aposta** — aposta duplicada (mesmo jogador + escolha + sessão), sessão
   aceitando apostas, operador ainda com contrato ativo, escolha válida para a sessão.
6. **Verificação pré-aposta de jogo responsável** (opt-in; ver §5) — **após** a validação local,
   **antes** do débito.
7. **Liquidação da aposta** — Modelo A: **débito** na carteira do operador (ver §4), o **último** passo
   antes de persistir; Modelo B: o pagamento on-chain validado já movimentou os fundos.
8. **Persiste** a aposta e publica `BetPlacedEvent` (não-loteria).

Como o débito / checagem on-chain é o **último** passo, uma aposta inválida, duplicada ou negada pelo
RG **nunca** é debitada ou persistida.

## 4. Liquidação — API de carteira do operador (Modelo A)

Para operadores regulados, o Monkify não retém saldo e chama a **sua** API de carteira. Você implementa
três operações (valores são **decimais em USD**, `currency: "USD"`):

| Operação | Quando | Chamada de |
| --- | --- | --- |
| **Debit(bet, amount)** | No momento da aposta — o último passo antes de aceitá-la | `Monkify.Api` (síncrono) |
| **Credit(bet, amount)** | Quando a sessão é premiada e o jogador venceu | Worker de background |
| **Refund(bet, amount, reason)** | Cancelamento pós-aceite (loteria sem quórum, fim abrupto) | Worker de background |

- **Idempotência:** a chave é o **id da aposta** (+ operação). Um timeout não é reexecutado de forma
  síncrona; a chave por id de aposta impede débito duplicado em qualquer retentativa posterior.
- **Resultado do débito → mensagem ao jogador.** Um débito falho retorna uma mensagem **específica** ao
  jogador, mapeada de um código de erro canônico (isto *não* é a regra do status vago do operador — o
  erro da própria carteira do jogador é específico):

  | Erro canônico | O jogador vê |
  | --- | --- |
  | `InsufficientFunds` | fundos insuficientes |
  | `PlayerBlocked` | jogador bloqueado |
  | `Duplicate` | aposta duplicada |
  | `Timeout` / `Unavailable` | "temporariamente indisponível, tente novamente" |

- **Toda operação bem-sucedida grava uma linha em `TransactionLog`** com o seu
  `ExternalTransactionReference` retornado.
- **Cripto (Modelo B)** não muda: prêmio = transferência SPL on-chain; estorno = estorno on-chain.
- **Free bets** (valor 0) **não** fazem chamadas de carteira em nenhum modelo.

## 5. Hook pré-aposta de jogo responsável (opcional, Modelo A ou B)

Se você quer que o Monkify aplique seus controles de jogo responsável antes de uma aposta, você
implementa um adaptador de **verificação pré-aposta**. É **opt-in por operador** — operadores não
configurados pulam a verificação por completo.

- O Monkify chama a sua verificação com `{ operatorId, externalPlayerId, amount, currency, sessionId,
  gameType, betId }` **após** a validação local e **antes** do débito.
- Sua resposta é uma de:
  - **Allowed** → a aposta prossegue.
  - **Denied(reason)** → a aposta é rejeitada com mensagem **específica** ao jogador. Razões canônicas:
    `SelfExcluded`, `LimitReached`, `CoolingOff`, `KycRequired`, `Blocked`, `Other`.
  - **Unavailable** (timeout / erro) → **fail-closed**: a aposta é rejeitada ("temporariamente
    indisponível, tente novamente"). Qualquer exceção de transporte é tratada como Unavailable.
- Uma verificação negada ou falha significa que a aposta **nunca** é debitada ou persistida. A auditoria
  é **apenas em log** — sem tabela no banco do Monkify, sem armazenar PII do jogador.

## 6. Execução & streaming — ciclo de vida da sessão

Após a aposta ser registrada, a sessão avança por uma máquina de estados dirigida por mensagens
(RabbitMQ):

```
BetPlaced → RunSession → RewardSession        (RefundInvalidBet em caso de falha)
```

- O **typer** roda o jogo no worker de background e transmite via **SignalR** (`/Hubs/Sessions`,
  `/Hubs/RecentBets`): caracteres em lote, velocidade atual e transições de status (vencedores são
  anunciados em `Ended`). O SignalR tem escopo por id de sessão — sem launch token no socket.
- Em `RewardSession`, os vencedores são pagos (**Modelo A** `Credit` na carteira / **Modelo B**
  transferência on-chain); cancelamentos estornam (**Modelo A** `Refund` na carteira / **Modelo B**
  on-chain). Cada movimento é uma linha em `TransactionLog` (`Credit` / `Debit` / `Refund`). **Sem
  comissão** — o pote inteiro vai para os vencedores.

## 7. Sequência ponta a ponta (ambos os modelos)

```mermaid
sequenceDiagram
  participant Casino as Operador/Cassino
  participant SPA as Monkify SPA
  participant API as Monkify.Api
  participant RG as Adaptador RG pré-aposta (opt-in)
  participant Wallet as API de Carteira do Operador (Modelo A)
  participant Chain as Solana (Modelo B)
  participant Cons as Worker de background Monkify

  Casino->>SPA: redirect …#launch_token=<jwt> (RS256, iss/sub)
  SPA->>API: GET /operators/{id} (X-Launch-Token)
  API-->>SPA: branding + paymentModel  (token inválido/expirado ⇒ 401 "indisponível")
  SPA->>API: GET /sessions/active-types → GET /sessions/{typeId}
  SPA->>API: POST /sessions/{id}/bets (X-Launch-Token)
  API->>RG: verificação pré-aposta (se configurada) — allow/deny/fail-closed
  alt Modelo A (Regulado)
    API->>Wallet: Debit(bet, amount, USD)  — último passo, fail-closed
    Wallet-->>API: ok / fundos-insuficientes / bloqueado / timeout
  else Modelo B (Cripto)
    API->>Chain: valida paymentSignature
  end
  API-->>SPA: aposta aceita (TransactionLog: Debit)
  API->>Cons: BetPlaced → RunSession → RewardSession (RabbitMQ)
  alt vencedor — Modelo A
    Cons->>Wallet: Credit(bet, prêmio, USD)  (TransactionLog: Credit)
  else vencedor — Modelo B
    Cons->>Chain: transferência de token SPL
  end
  opt cancelamento / inválida
    Cons->>Wallet: Refund(bet, reason) (Modelo A) / estorno on-chain (Modelo B)  (TransactionLog: Refund)
  end
```

## 8. O que você implementa (checklist Modelo A)

- [ ] **Assinar launch tokens** (RS256) com claims `iss` / `sub` / `exp` / `iat` / `jti`; fornecer ao
      Monkify a sua **chave pública**.
- [ ] **API de carteira** expondo **Debit / Credit / Refund** (USD; idempotente pelo id da aposta;
      retorna uma referência externa e um código de erro canônico em falha).
- [ ] *(opcional)* **Verificação pré-aposta de RG** retornando Allowed / Denied(reason) / Unavailable.
- [ ] Fornecer o **`ExternalPlayerId`** que você quer que o Monkify referencie por jogador.

No **Modelo B** você só assina launch tokens (`sub` vazio) e deixa os jogadores conectarem a carteira.

## Fora do escopo deste documento

- OpenAPI completo de cada endpoint (compartilhado à parte, se necessário).
- Topologia real de infraestrutura, credenciais e código-fonte.
- Promessas de certificação — ver o documento legal de Fairness; a certificação em laboratório é
  **ADIADA**.

## Controle do documento

- **Fonte de verdade Modelo A/B:** [`README.md`](README.md) (bloco reutilizado na íntegra).
- **Fundamentado em (somente leitura):** validação de launch token, API de carteira do operador
  (Debit/Credit/Refund, `TransactionLog` unificado, sem comissão), o hook pré-aposta de RG
  (allow/deny/fail-closed) e a máquina de estados da sessão (`BetPlaced → RunSession → RewardSession`,
  `RefundInvalidBet`).
- **Referências cruzadas:** [`../legal/`](../legal/) — Regras do Jogo & Fairness, Prova de Propriedade.
- **Etapas pagas adiadas:** certificação de RNG em laboratório (GLI/BMM) — **ADIADA**.
