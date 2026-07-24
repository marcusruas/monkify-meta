# Space Stroker — "Background-Only Page" — Implementation Spec

> **Status: Ready for implementation** — user request (2026-07-24), not from a Trello card.
> Repos affected: `monkify-front` and this doc in `monkify-meta`. **No backend change.**
>
> **Purpose:** Add a chrome-free URL, `/space-stroker/background`, that renders **only** the animated
> space backdrop — nothing in front of it. A reusable visual asset: embed it in an `<iframe>`,
> screen-record it, or point a capture tool at it.

---

## 1. Summary

The animated backdrop is **not** per-page. `App.vue` renders it globally, behind every route:

| Layer | Component | Where |
| --- | --- | --- |
| Secondary starfield | `<game-background id="secondary-bg">` | `App.vue` |
| Main starfield | `<game-background id="main-bg">` | `App.vue` |
| Per-bet torpedoes | `<torpedo-spawner v-if="!lightMode">` | `App.vue` |
| Seam vignette | `.section-vignette` (`arcade.css:227`) | `App.vue` |

So this feature does not **build** a background — it **removes everything in front of one**. The page
component renders nothing and the route suppresses the header; the global backdrop shows through.

**Level: Minor change** (frontend-only, additive, no game-flow change) → no mandatory integration
test and no dedicated `features/background-page.md`.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md`:

- **Worktree-per-repo:** work lives in `.worktrees/background-page/monkify-front`; the shared
  checkout stays on `main` (hub). Branch `feature/background-page` is the merge/PR ref, pushed to
  `origin`.
- **No backend integration test required:** per `constraints.md` §3, mandatory only for **new
  features** or game-flow changes. This adds no backend surface and does not touch the game
  lifecycle. `monkify-front` has no test suite → verification is manual (§7).
- **CRLF:** new/edited files keep CRLF line endings or `yarn lint` fails on `Insert ␍`.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Reuse the global backdrop; do not duplicate it.** The page renders nothing and lets `App.vue`'s existing `GameBackground` instances show through. A self-contained page that re-declares the layers was rejected: it would silently drift from the real in-game look the first time the App-level backdrop is tuned. |
| 2 | **Layers = both starfields + the vignette** — i.e. exactly the in-game backdrop. |
| 3 | **Torpedoes need no code.** `TorpedoSpawner` only spawns on incoming bets (it watches `gameInstance.bets.length`); none arrive on this route, so it mounts and stays inert. Left in place rather than excluded. |
| 4 | **Path `/space-stroker/background`** — sibling of `/space-stroker/wallet-bridge`, deliberately **outside** the operator-scoped `/space-stroker/game/*` tree. |
| 5 | **Registered in production.** It is an asset URL, not a dev tool. |
| 6 | **Chrome suppression is declarative:** a `meta: { chromeless: true }` route flag replaces `App.vue`'s hard-coded `route.name !== 'WalletBridge'` header check. `WalletBridge` gets the same flag — identical behaviour, but the condition stops growing a name per chromeless route. |
| 7 | **`routes.ts` is typed `RouteRecordRaw[]`, not `RouteRecord[]`** (found during implementation, §5.2). |

---

## 4. Backend behaviour

None. No endpoint, entity, migration, or config change.

---

## 5. Frontend behaviour

### 5.1 New page — `src/pages/BackgroundPage.vue`

A single empty root element with a comment block explaining why it is empty. The empty root (rather
than a comment-only template) exists to absorb the `<router-view class="mx-auto h-5/6 block">`
attribute fallthrough — a rootless component would log an "Extraneous non-props attributes" warning,
and this page's whole point is a clean capture.

```vue
<template>
  <div aria-hidden="true"></div>
</template>
```

### 5.2 New route — `src/routes.ts`

```ts
{
  name: 'Background',
  path: '/space-stroker/background',
  component: require('@/pages/BackgroundPage.vue').default,
  meta: { chromeless: true },
},
```

`WalletBridge` gains the same `meta: { chromeless: true }`.

**Required type fix.** Adding `meta` breaks the file's existing `as RouteRecord[]` cast with TS2352
(`'component' does not exist in type 'RouteRecordNormalized'. Did you mean 'components'?`).
`RouteRecord` is vue-router's **normalized** record — what the router produces internally, whose
`components` is plural. The route **definitions** in this file are `RouteRecordRaw`. The wrong type
only ever compiled because the literals overlapped it loosely enough to satisfy the cast's
"sufficiently overlaps" test; `meta` is a real `RouteRecord` property, so adding it tightened the
comparison and surfaced the mismatch. Fix is the correct type, not a looser cast:

```diff
- import { RouteRecord } from 'vue-router';
+ import { RouteRecordRaw } from 'vue-router';
...
- ] as RouteRecord[];
+ ] as RouteRecordRaw[];
```

No behaviour change — the only consumer is `createRouter({ routes })` in `main.ts`, which expects
`RouteRecordRaw[]` anyway.

### 5.3 Header suppression — `src/App.vue`

```diff
- <game-header v-if="route.name !== 'WalletBridge'" class="relative z-40" />
+ <game-header v-if="!route.meta?.chromeless" class="relative z-40" />
```

### 5.4 What needs no change (and why)

- **Operator boot.** `App.vue`'s `onMounted` calls `loadOperator()` only for
  `GameLobby | Game | GameHistory | GameStats`. `Background` is not in that set, so the route makes
  **no API call**, needs **no launch token**, and can never redirect to `/error`. The URL works cold.
- **`EmbedRelaunching`.** `relaunching` starts `false` and is only flipped by the 401/expired paths
  in `mainService.ts` / `useOperator.ts`, which this route never reaches — so the overlay cannot
  appear even when the page is iframed.
- **Perf mode.** The `light-graphics` class still applies, so a weak renderer degrades the backdrop
  here exactly as it does in-game.
- **Scrolling.** `<main>` is empty, so the `min-h-screen` root produces no scrollbar.

---

## 6. Out of scope

- Query/hash parameters to tune the backdrop (density, speed, layer toggles). `speed-multiplier`
  stays bound to `typingSpeed`, which sits at its idle default here.
- A screenshot/export button, or any recording affordance.
- Making the backdrop itself configurable, or extracting it out of `App.vue`.
- Linking to the page from anywhere in the UI — it is reached by typing the URL.

---

## 7. File checklist

### 7.1 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/pages/BackgroundPage.vue` | **New.** Empty page (§5.1) |
| 2 | `src/routes.ts` | **New** `Background` route + `meta.chromeless`; add `meta.chromeless` to `WalletBridge`; retype `RouteRecord[]` → `RouteRecordRaw[]` (§5.2) |
| 3 | `src/App.vue` | Header `v-if` keys off `route.meta.chromeless` instead of the `WalletBridge` name (§5.3) |

### 7.2 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/background-page.md` | This spec (new) |
| 2 | `INDEX.md` | Spec row + "Feature currently in flight" |

---

## 8. Tests

No automated tests: `monkify-front` has no test suite and no backend surface changed.

**Manual QA:**

1. `yarn serve`, open `/space-stroker/background` → a moving starfield fills the viewport, with **no
   header, no content, no scrollbar**, and **0 console errors/warnings**.
2. The page loads with **no launch token and no operator** in the URL — confirm no `/error` redirect
   and no operator/preferences requests in the network tab.
3. **Regression:** `/space-stroker/wallet-bridge` still renders without the game header after the
   `meta` refactor.
4. **Regression:** `/space-stroker/game` still shows the header normally.

---

## 9. Acceptance criteria

1. Given a cold browser with no launch token, when `/space-stroker/background` is loaded, then the
   animated backdrop renders and no header, page content, or error redirect appears.
2. Given that page, when it is inspected, then the console shows no errors or Vue warnings and no
   operator/preferences API requests were made.
3. Given the page is loaded inside an `<iframe>`, then the "relaunching" overlay does not appear.
4. Given `/space-stroker/wallet-bridge`, when it is loaded after this change, then it still renders
   without the game header.
5. Given any other route, when it is loaded, then the game header still renders as before.

---

## 10. Revision log

| Date | Change |
| --- | --- |
| 2026-07-24 | Initial spec from a direct user request (frontend-only, Minor change). |
| 2026-07-24 | Implementation note: adding `meta` required retyping `routes.ts` to `RouteRecordRaw[]` (§5.2). Manual QA + both regressions verified against the local stack. |
