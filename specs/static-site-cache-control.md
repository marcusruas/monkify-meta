# SpaceStroker — "Set Cache-Control on static-site uploads" — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Set Cache-Control on static-site uploads: stale index.html points at chunks rsync -d already deleted](https://trello.com/c/bSSvCfKF)
> (2026-07-27).
> Repos affected: **`monkify-meta` only** — `deploy/tools/SpaceStroker.Deploy`. No backend, no
> frontend, no Terraform, no database.
>
> **Purpose:** every object in both static-site buckets is uploaded with no cache metadata, so GCS
> serves all of them with its default `public, max-age=3600`. That caches `index.html` — which must
> never be cached — for an hour, while caching immutable hashed chunks for only an hour. Combined
> with `rsync -d`, which deletes the previous build's chunks, a returning player boots a stale HTML
> that names files no longer in the bucket. This spec makes the deploy tool set a correct
> `Cache-Control` per file class, and stop deleting the assets a still-running session may ask for.

---

## 1. Summary

`Deployer.UploadAndInvalidate` (`deploy/tools/SpaceStroker.Deploy/Deployer.cs:591-599`) is today one
line of upload:

```csharp
ProcessRunner.RunOrThrow("gsutil", $"-m rsync -r -d \"{distDir}\" gs://{bucket}");
```

No `-h`, no `setmeta`, so **every** object inherits the GCS bucket default `public, max-age=3600` —
verified live on 2026-07-27 against both `https://spacestroker.com/` and
`https://app.spacestroker.com/`.

This spec replaces that single call with an **ordered plan of gsutil invocations**, derived from the
built `dist` tree by a new pure class, plus a `setmeta` convergence pass that repairs the objects
already sitting in the buckets.

| | Today | After |
| --- | --- | --- |
| `index.html`, `404.html`, `verify.html`, `embed-example.html`, `cookies/index.html` | `public, max-age=3600` | `no-cache` |
| Hashed assets — front `js/ css/ img/ media/`, lp `_astro/` | `public, max-age=3600` | `public, max-age=31536000, immutable` |
| Unhashed rest — `fonts/`, favicons, `og-image.png`, `robots.txt`, `sitemap*.xml` | `public, max-age=3600` (implicit) | `public, max-age=3600` (**explicit**) |
| Previous build's hashed chunks | deleted by `rsync -d` | **retained** |
| `*.js.map` sourcemaps | uploaded, publicly readable | **not uploaded** |
| CDN invalidation | `--path="/*"`, best-effort | unchanged |

### 1.1 Why `Cache-Control` alone does not fix the bug

Setting `no-cache` on the HTML fixes the *returning visitor*. It does nothing for a player who is
**already running** the old build when a deploy lands. `monkify-front` has no `import()` in `src/`
and its routes use eager `require()`, but `GameFairPanel.vue:228` does:

```ts
worker.value = new Worker(new URL('@/workers/verifyRound.worker.ts', import.meta.url));
```

That is a content-hashed chunk fetched **only when the player clicks RECOMPUTE OUTCOME in the FAIR
modal** — potentially hours into a session. The last local build also emitted four numbered async
chunks from lazily-loading dependencies. Under `rsync -d` those files are gone the moment a deploy
finishes, and since card #102 shipped, a missing chunk now returns a clean 404 instead of the SPA
shell — so it fails visibly. Retaining the hashed assets is therefore part of the fix, not a
nice-to-have.

### 1.2 Why upload-time `-h` alone does not fix the buckets

`gsutil rsync` decides what to copy by size / mtime / checksum — **not by metadata**. An object it
skips as unchanged keeps whatever metadata it already has, forever. The front's `img/` (7.5 MB) and
`media/` (11 MB) are GIFs and audio that essentially never change, so an upload-header-only fix would
leave the majority of production bytes on `max-age=3600` indefinitely. Hence the `setmeta`
convergence passes: they are what actually repairs what is live today.

### 1.3 Ordering is part of the correctness

Two ordering rules, both load-bearing:

1. **Assets upload before the HTML that names them.** If the new `index.html` landed first, a visitor
   in that window would request chunks not yet uploaded — the same 404, just inverted. Asset passes
   run first; the root pass (which carries the HTML) runs last.
2. **Upload conservatively, converge with `setmeta`.** The root pass stamps `no-cache` on everything
   it uploads, including files whose final tier is 1h; the `setmeta` passes then relax those. Every
   intermediate state is therefore *less* cached than the final one. A window between passes can
   **under-cache but never over-cache** — the only failure direction that is safe when the whole
   point of the card is that over-caching strands players.

### 1.4 Why the plan is derived from the `dist` tree

The tier of an object is decided by walking the built output, not by a hardcoded file list. Astro and
Vue CLI both emit files nobody enumerated in advance (`sitemap-0.xml` appeared with the sitemap
integration; `cookies/index.html` appeared with analytics #101). A hardcoded list silently misfiles
each new one. Deriving from `dist` means an unrecognised file lands in the **1h tier by default** —
the conservative direction — and an unrecognised *directory* is never treated as immutable.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** work lives in `.worktrees/static-site-cache-control/monkify-meta` on
  `feature/static-site-cache-control`. The `monkify-meta` hub stays on `main` — never `git checkout -b`
  there.
- **Secrets/config:** mock-only values in committed files. This spec adds no secret, no env var and
  no `secrets.<env>.json` key.
- **Production deploys are operator steps** (§5) — no agent runs `dotnet run -- deploy` or
  `terraform apply`. Every live acceptance criterion in §12 is therefore explicitly deferred.
- **Level: Minor change** (deploy/infra bug fix) ⇒ **no `SpaceStroker.Tests.Integration` test**
  (`constraints.md` §3 scopes that mandate to new features). There is no backend code in this card at
  all. Unit tests in `SpaceStroker.Deploy.Tests` are still required (§11).
- **No `monkify-backend` change** ⇒ `SpaceStroker.Tests` is untouched and there is nothing to report
  under the "report which unit tests changed" rule beyond §11's new file.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Scope = headers + asset retention.** `-d` is dropped for the hashed asset prefixes only; the rest of the tree keeps normal `-d` deletion semantics. Rationale in §1.1. |
| 2 | **Both upload-time `-h` and a `setmeta` convergence pass.** Neither alone is sufficient (§1.2). |
| 3 | **Three tiers:** HTML → `no-cache`; hashed assets → `public, max-age=31536000, immutable`; everything else → explicit `public, max-age=3600`. |
| 4 | **Fonts stay in the 1h tier.** `fonts/*.woff2` are **not** content-hashed, so an immutable TTL would strand a font swap for a year with no clean remedy. The whole unhashed group is a few hundred KB behind a CDN; the win did not justify the risk. Promoting it later is a one-constant change. |
| 5 | **REVISED during implementation — retained assets are pruned automatically after 24h (§6.6).** The refined card rejected automated pruning because rsync never re-uploads an unchanged file, so an age-based rule would delete an asset **still referenced by the live build**. That is right but too broad: it rules out deleting on age *alone*, not on *"absent from the build just published **and** retired longer than the window"* — an object no retained build names is by definition unreferenced. Implemented with per-deploy **build manifests**, not a GCS lifecycle rule, because the quantity that matters is **retirement** time and GCS records no such field. |
| 6 | **Pass order: assets → root → setmeta (broad to specific).** See §1.3. |
| 7 | **`no-cache`, not `no-store`.** The HTML is still stored and revalidated, so returning visitors get cheap ETag 304s rather than a full re-download. Cloud CDN's `CACHE_ALL_STATIC` declines to edge-cache `no-cache` either way, so the browser and the edge are both fixed by the one directive. |
| 8 | **`*.map` excluded from upload.** Decision 1 is what changes the calculus: retained builds make sourcemap accumulation permanent instead of self-overwriting. The `//# sourceMappingURL=` comment stays in the shipped JS, so devtools will request a map and get a clean 404 — accepted. |
| 9 | **The `/*` CDN invalidation is kept verbatim** (`Deployer.cs:597`, best-effort `Run`). It is mostly redundant afterwards, but it is the only thing that flushes the edge copies cached under the *old* headers on the transition deploy. Narrowing it to a path list was rejected — it would drift and would miss exactly those objects. |
| 10 | **Stay on `gsutil`.** It is what `deploy/README.md` lists as a prerequisite, what `ProcessRunner.ResolveExecutable` already handles on Windows, and what every other storage call uses. Migrating to `gcloud storage` is a separate concern with its own blast radius. |
| 11 | **The tier + plan logic is a pure class** (`StaticSiteCache.cs`) unit-tested in the existing `SpaceStroker.Deploy.Tests`, following the `TeardownDecisions` precedent from #98. `Deployer` keeps every process call. |
| 12 | **Split from card #102 deliberately** — different fix, different file, different deploy surface. #102 makes this failure *legible*; this card makes it *stop happening*. |

---

## 4. Data model

**Not applicable.** No entity, no EF mapping, no migration, no database contact of any kind.

---

## 5. Configuration

**Not applicable.** No new `GeneralSettings` key, no `secrets.<env>.json` entry, no `VUE_APP_*`
variable, no Terraform variable. The cache tiers are compile-time constants in `StaticSiteCache`
(decision 4 notes that promoting fonts later is a one-constant change, deliberately not a config
knob — an untested TTL flip on a live bucket is not something to expose).

---

## 6. Tool behaviour (`SpaceStroker.Deploy`)

### 6.1 New file — `deploy/tools/SpaceStroker.Deploy/StaticSiteCache.cs`

A pure decisions class in the shape of `TeardownDecisions.cs`: it takes an already-read directory
listing and returns values. No I/O, which is what makes it testable.

```csharp
namespace SpaceStroker.Deploy;

/// <summary>Which static site is being published — they have different build layouts.</summary>
public enum StaticSite { Front, Lp }

/// <summary>What a step does, so tests can assert on shape rather than parse strings.</summary>
public enum UploadStepKind { AssetSync, RootSync, SetMeta }

/// <summary>One fully-formed gsutil invocation. Deployer executes; this file decides.</summary>
public sealed record UploadStep(UploadStepKind Kind, string Arguments);

/// <summary>
/// The pure decisions behind publishing a static site: which Cache-Control each file class gets,
/// which prefixes are content-hashed (and therefore safe to retain and cache forever), and the
/// ordered list of gsutil invocations that realises both. No I/O — Deployer keeps every process
/// call, which is what makes this testable at all.
/// </summary>
public static class StaticSiteCache
{
    /// <summary>Entry points that name the current hashed chunks. Must never be cached.</summary>
    public const string HtmlCacheControl = "no-cache";

    /// <summary>Content-hashed by construction: a new build produces a new filename.</summary>
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    /// <summary>
    /// Everything unhashed and non-HTML. This is exactly the GCS bucket default we inherit today —
    /// made explicit so the buckets end up fully declared rather than relying on a provider default.
    /// </summary>
    public const string DefaultCacheControl = "public, max-age=3600";

    /// <summary>
    /// Sourcemaps: never uploaded. Excluded from every pass (a no-op for the lp, which emits none)
    /// so the rule stays uniform across both sites.
    /// </summary>
    public const string ExcludedPathRegex = @".*\.map$";

    /// <summary>
    /// Top-level directories whose contents carry a content hash in the filename. Vue CLI emits
    /// js/css/img/media; Astro emits everything hashed into _astro. A directory NOT listed here is
    /// treated as unhashed (1h) — the conservative default for build output nobody enumerated.
    /// </summary>
    public static IReadOnlyList<string> HashedPrefixes(StaticSite site) => site switch
    {
        StaticSite.Front => ["js", "css", "img", "media"],
        StaticSite.Lp => ["_astro"],
        _ => throw new DeployException($"Unknown static site '{site}'."),
    };

    /// <summary>True for a path that must never be uploaded.</summary>
    public static bool IsExcluded(string relativePath);

    /// <summary>
    /// The tier for one dist-relative path (forward slashes). ORDER MATTERS: the .html test runs
    /// first and at ANY depth, because Astro emits directory-style pages — the lp's /cookies page
    /// builds to cookies/index.html, which a first-segment rule would misfile as unhashed 1h and
    /// re-create this very bug one directory down.
    /// </summary>
    public static string ForPath(StaticSite site, string relativePath);

    /// <summary>
    /// The single -x regex for the root pass: every hashed prefix present, plus the sourcemap rule.
    /// gsutil rsync honours only ONE -x, so the patterns are composed with alternation rather than
    /// repeated flags. -x excludes from BOTH copy and delete, which is what stops this pass's -d
    /// from reaping the retained assets.
    /// </summary>
    public static string RootExclusionRegex(StaticSite site, IReadOnlyList<string> rootDirectories);

    /// <summary>
    /// The ordered gsutil invocations for one publish. rootFiles/rootDirectories are the entries at
    /// the top of distDir; htmlPaths is every *.html in the tree at ANY depth (the lp's /cookies page
    /// is not at the root). All three are already read by the caller — this class does no I/O.
    /// </summary>
    public static IReadOnlyList<UploadStep> Plan(
        StaticSite site,
        string distDir,
        string bucket,
        IReadOnlyList<string> rootFiles,
        IReadOnlyList<string> rootDirectories,
        IReadOnlyList<string> htmlPaths);
}
```

#### 6.1.1 `ForPath` — tier resolution

Normalise `\` to `/` and strip a leading `./`, then, **in this order**:

| # | Test | Result |
| --- | --- | --- |
| 1 | path ends with `.html` (ordinal, case-insensitive), **at any depth** | `HtmlCacheControl` |
| 2 | first path segment is in `HashedPrefixes(site)` | `ImmutableCacheControl` |
| 3 | anything else | `DefaultCacheControl` |

Rule 1 must precede rule 2 — see the `cookies/index.html` note in the summary comment above. Rule 3
is the fail-safe: an unknown file or directory under-caches rather than over-caches.

#### 6.1.2 `Plan` — the ordered invocations

Given the front (`rootDirectories = [js, css, img, media]`,
`rootFiles = [index.html, verify.html, embed-example.html, favicon.ico]`) and bucket `B`:

| Order | Kind | gsutil arguments |
| --- | --- | --- |
| 1..n | `AssetSync` | `-h "Cache-Control:public, max-age=31536000, immutable" -m rsync -r -x ".*\.map$" "<dist>/js" gs://B/js` — one per hashed directory **present in `rootDirectories`**, **no `-d`** |
| n+1 | `RootSync` | `-h "Cache-Control:no-cache" -m rsync -r -d -x "^(js\|css\|img\|media)/.*$\|.*\.map$" "<dist>" gs://B` |
| n+2 | `SetMeta` | `-m setmeta -r -h "Cache-Control:public, max-age=31536000, immutable" gs://B/js gs://B/css gs://B/img gs://B/media` |
| n+3 | `SetMeta` | `-m setmeta -h "Cache-Control:no-cache" gs://B/index.html gs://B/verify.html gs://B/embed-example.html` |
| n+4 | `SetMeta` | `-m setmeta -h "Cache-Control:public, max-age=3600" gs://B/favicon.ico` |

For the lp (`rootDirectories = [_astro, fonts]`, `rootFiles = [index.html, 404.html, favicon.ico,
favicon.png, favicon-96.png, og-image.png, robots.txt, sitemap-0.xml, sitemap-index.xml]`, plus the
`cookies/` page directory):

| Order | Kind | gsutil arguments |
| --- | --- | --- |
| 1 | `AssetSync` | `-h "Cache-Control:…immutable" -m rsync -r -x ".*\.map$" "<dist>/_astro" gs://B/_astro` |
| 2 | `RootSync` | `-h "Cache-Control:no-cache" -m rsync -r -d -x "^(_astro)/.*$\|.*\.map$" "<dist>" gs://B` |
| 3 | `SetMeta` | `-m setmeta -r -h "Cache-Control:…immutable" gs://B/_astro` |
| 4 | `SetMeta` | `-m setmeta -h "Cache-Control:no-cache" gs://B/index.html gs://B/404.html` |
| 5 | `SetMeta` | `-m setmeta -h "Cache-Control:public, max-age=3600" gs://B/favicon.ico gs://B/favicon.png gs://B/favicon-96.png gs://B/og-image.png gs://B/robots.txt gs://B/sitemap-0.xml gs://B/sitemap-index.xml` and `-r gs://B/fonts` |

Construction rules:

- **Asset steps** are emitted only for hashed prefixes **actually present** in `rootDirectories`, so
  a build that stops emitting `media/` does not produce a step against a path that does not exist
  (gsutil would fail the run on a missing source).
- **Non-hashed root directories** (the lp's `fonts/`, and the `cookies/` page directory) are carried
  by the `RootSync` step — they are not excluded from it. Their objects are then corrected by the
  `SetMeta` steps: `cookies/index.html` by the HTML step (its path is enumerated from the dist walk,
  not just the root listing — see below), `fonts/**` by a recursive 1h step.
- **A non-hashed root directory that CONTAINS HTML is skipped by the 1h `SetMeta` step** (the lp's
  `cookies/`). Recursing the 1h header over it would stamp `cookies/index.html` at `max-age=3600`
  until the HTML step ran — a brief over-cache, and §1.3 rule 2 says intermediate states may only
  under-cache. Its HTML is stamped by the HTML step; any non-HTML sibling keeps the root pass's
  `no-cache`, which is safe. (Added during implementation — the original §6.1.2 did not address the
  recursive pass colliding with a page directory.)
- **The HTML `SetMeta` step enumerates every `.html` in the dist tree**, at any depth — not only
  root-level ones. `Plan` therefore needs the full relative-path list for HTML, which the caller
  supplies (§6.2). Root-level *non*-HTML files and non-hashed root *directories* are enough for the
  1h step, because a nested non-HTML file inherits the recursive directory step.
- **A `SetMeta` step is emitted only when its target list is non-empty.** An empty `gsutil setmeta`
  invocation is an error, and a site with no unhashed root file must not fail the deploy.
- Paths are quoted; the `-x` regex is a single alternation because gsutil honours only one `-x`.

### 6.2 `UploadAndInvalidate` — rewritten

`Deployer.cs:591-599` becomes:

```csharp
private void UploadAndInvalidate(StaticSite site, string bucket, string distDir)
{
    if (!Directory.Exists(distDir))
        throw new DeployException($"Build output not found: {distDir}. Did the build succeed?");

    // The plan is derived from what the build actually emitted, so a file class nobody enumerated
    // (a new sitemap, a new Astro page) lands in the conservative 1h tier instead of being missed.
    var rootFiles = Directory.GetFiles(distDir).Select(Path.GetFileName).OfType<string>().Order().ToArray();
    var rootDirs = Directory.GetDirectories(distDir).Select(Path.GetFileName).OfType<string>().Order().ToArray();
    var htmlPaths = Directory
        .EnumerateFiles(distDir, "*.html", SearchOption.AllDirectories)
        .Select(p => Path.GetRelativePath(distDir, p).Replace('\\', '/'))
        .Order()
        .ToArray();

    foreach (var step in StaticSiteCache.Plan(site, distDir, bucket, rootFiles, rootDirs, htmlPaths))
        ProcessRunner.RunOrThrow("gsutil", step.Arguments);

    // CDN cache invalidation (best-effort; url-map name follows the module convention). Kept at /*:
    // it is the only thing that flushes edge copies cached under the OLD headers on the deploy that
    // first carries this change.
    ProcessRunner.Run("gcloud",
        $"compute url-maps invalidate-cdn-cache {_naming.UrlMap} --project={_s.Gcp.ProjectId} --path=\"/*\" --async");
}
```

Every step uses `RunOrThrow`: a failed upload or a failed `setmeta` must fail the deploy loudly. A
half-applied plan is safe to re-run — the passes are idempotent — but it must not be mistaken for a
successful publish.

### 6.3 `DeployComponent` — pass the site

`Deployer.cs:49-56`:

```csharp
case "front":
    BuildStaticSite(_paths.FrontRepo, isYarn: true);
    UploadAndInvalidate(StaticSite.Front, _naming.FrontBucket, Path.Combine(_paths.FrontRepo, "dist"));
    break;
case "lp":
    BuildStaticSite(_paths.LpRepo, isYarn: false);
    UploadAndInvalidate(StaticSite.Lp, _naming.LpBucket, Path.Combine(_paths.LpRepo, "dist"));
    break;
```

### 6.4 Unchanged behaviour (explicit)

- `BuildStaticSite` / `WriteFrontEnv` — untouched. The `frontBuild` env block still bakes
  `VUE_APP_*` / `PUBLIC_*` into both builds.
- The CDN invalidation call — byte-for-byte unchanged, still best-effort `Run` (not `RunOrThrow`), so
  an invalidation quota error still does not fail a deploy.
- `DeployAll` ordering (consumers → api → front → lp) — untouched.
- `EnvNaming.FrontBucket` / `LpBucket` / `UrlMap` — untouched.
- Terraform — **nothing** in `deploy/terraform/` changes. The `static_site` module's
  `not_found_page` / `main_page_suffix` and the `edge` module's `custom_error_response_policy` are
  card #102's surface and stay exactly as merged.

### 6.5 What actually accumulates

Measured against the real build output, not the 26 MB the refined card implied:

| | Size | Churns per deploy? |
| --- | --- | --- |
| front `js/` real code | 1.5 MB | yes, on any code change |
| front `js/` sourcemaps | 6.2 MB | **no longer uploaded at all** |
| front `css/` | 96 KB | yes |
| front `img/` | 7.5 MB | only when the art changes |
| front `media/` | 11 MB | audio — effectively never |
| lp `_astro` js/css | 223 KB | yes |
| lp `_astro` images | 2.2 MB | only when the art changes |

`rsync` skips unchanged files and a hashed name only changes with its content, so a code-only deploy
leaves roughly **1.6 MB** (front) and **0.2 MB** (lp) behind — not a whole build. Dropping the 6.2 MB
of sourcemaps more than pays for the first several deploys. The cost is negligible; the reason to
prune is that unbounded growth is not a policy.

### 6.6 Pruning retained assets (automatic, 24h)

Runs inside `UploadAndInvalidate` after the upload plan, before the CDN invalidation.

**Why manifests and not a lifecycle rule.** The quantity that matters is how long an asset has been
**retired**, and GCS records no such field. Object creation time is the wrong proxy: an image
unchanged for eight months has an ancient creation date, so any age-based rule would delete it the
moment it dropped out of a build — precisely the assets retention exists to protect. So each deploy
writes down what it published, and an asset is prunable once no manifest inside the window lists it.

**The retention window: 24 hours.** It is not derived from `Cache-Control` — HTML is `no-cache`, so it
revalidates on every load and can never name a stale chunk. The binding constraint is an
**already-open tab**, whose document lives in memory and can lazily fetch a chunk (the provably-fair
verify Worker) at any moment. `useIdleDisconnect` (#96) abandons idle sessions after 30-60 minutes, so
24h is ~20× that ceiling and still covers a laptop closed overnight. One constant:
`BuildManifest.Retention`.

**Flow.**

1. Write `gs://<bucket>/_builds/<yyyyMMddTHHmmssZ>.txt` — every asset path this build published.
2. `gsutil ls` the manifests; retain those inside the window (an **unparseable** name is retained, never
   assumed expired).
3. `gsutil cat` the retained manifests in batches → the union of protected paths.
4. `gsutil ls -r` each hashed prefix → what is actually in the bucket.
5. Delete `(in bucket) − (protected)`, plus the expired manifests, in batched `gsutil -m rm` calls.

**Fail-closed at every step.** A non-zero `ls` or `cat` prints why and prunes nothing;
`PruneTargets` returns empty for an empty keep-set, so a failed read can never be mistaken for
"nothing is protected". Deleting the live build because a listing failed would be far worse than the
bug being fixed.

**`_builds/` is shielded** from the root pass's `-d` in `RootExclusionRegex` — it is bucket-only and
absent from every dist tree, so without the shield the deploy would delete the manifest it just wrote.

**Batching** keeps every command line under the Windows 8191-character cap; the lp alone publishes
~80 `_astro` objects and a delete list can span several builds.

---

## 7. Frontend behaviour

**No change in `monkify-front` or `monkify-lp`.** Both already emit content-hashed filenames; this
card only changes how the built output is uploaded and served.

One observable consequence: `*.js.map` requests (issued by devtools when it opens a bundle) will
return 404 instead of the map. No user-facing surface, no console error in normal browsing.

---

## 8. Flow diagram

```mermaid
sequenceDiagram
    participant D as Deployer
    participant P as StaticSiteCache.Plan
    participant G as gsutil
    participant B as GCS bucket
    participant C as Cloud CDN

    D->>D: read dist root files + dirs + all *.html
    D->>P: Plan(site, dist, bucket, rootFiles, rootDirs, htmlPaths)
    P-->>D: ordered UploadStep[]

    Note over D,B: 1. assets FIRST — the HTML must never name a chunk that is not there yet
    D->>G: rsync -r (no -d) -h immutable  dist/js → gs://B/js
    G->>B: new chunks added, previous build's chunks RETAINED

    Note over D,B: 2. root pass — -d deletes, but -x shields the asset prefixes
    D->>G: rsync -r -d -x "^(js|css|img|media)/.*$|.*\.map$" -h no-cache  dist → gs://B
    G->>B: index.html + unhashed files replaced; stale top-level objects deleted

    Note over D,B: 3. convergence — repairs objects rsync skipped as unchanged
    D->>G: setmeta -r immutable  gs://B/js gs://B/css …
    D->>G: setmeta no-cache      gs://B/index.html gs://B/cookies/index.html …
    D->>G: setmeta 1h            gs://B/favicon.ico … ; setmeta -r gs://B/fonts
    G->>B: every object now carries an explicit Cache-Control

    D->>C: invalidate-cdn-cache --path="/*" (best-effort)
    Note over C: flushes edge copies still held under the OLD headers
```

---

## 9. Out of scope

- **The HTTP status-code bug** (SPA deep links returning 404, the lp's `404.html` unreachable) —
  companion card #102 ([mkogi4tR](https://trello.com/c/mkogi4tR)), already merged, **not yet
  applied**. No Terraform is touched here.
- Any change to the SPA, the lp, or the Terraform LB topology.
- GCS object-lifecycle rules. Pruning itself is now **in scope** and automatic (§6.6, decision 5
  revised) — but it is done tool-side with build manifests, not with a lifecycle rule, and adds no
  Terraform.
- Migrating the deploy tool from `gsutil` to `gcloud storage` (decision 10).
- Disabling sourcemap **generation** in `monkify-front/vue.config.js` — this card only stops
  **uploading** them.
- Running a real deploy or `terraform apply` — operator steps (`constraints.md` §5).
- A frontend "new version available, reload" handler. Retention (decision 1) addresses the same
  failure without touching app code; a reload prompt is a separate product decision.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None.** This card does not touch the backend repo.

### 10.2 Frontend (`monkify-front` / `monkify-lp`)

**None.** Neither repo is modified.

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `deploy/tools/SpaceStroker.Deploy/StaticSiteCache.cs` | **New.** Pure tier + plan decisions: the 3 `Cache-Control` constants, `HashedPrefixes`, `IsExcluded`, `ForPath`, `RootExclusionRegex` (which also shields `_builds/`), `Plan`. |
| 2 | `deploy/tools/SpaceStroker.Deploy/BuildManifest.cs` | **New.** Pure prune decisions: `Retention` (24h), `NameFor`/`TimestampOf`, `RetainedManifests`/`ExpiredManifests`, `PruneTargets` (fail-closed), `ParseListing`, `Render`/`ParseManifestBodies`, `Batch`. |
| 3 | `deploy/tools/SpaceStroker.Deploy/Deployer.cs` | `UploadAndInvalidate` takes a `StaticSite`, reads the dist listing, executes `StaticSiteCache.Plan` step by step via `RunOrThrow`, then calls the new private `PruneRetainedAssets`; CDN invalidation unchanged. `DeployComponent`'s `front`/`lp` cases pass the site. |
| 4 | `deploy/tools/SpaceStroker.Deploy.Tests/StaticSiteCacheTests.cs` | **New.** 43 tests over the tiers and the plan. |
| 5 | `deploy/tools/SpaceStroker.Deploy.Tests/BuildManifestTests.cs` | **New.** 18 tests over the prune decisions. |
| 6 | `deploy/README.md` | Document the three cache tiers, asset retention, and the automatic 24h prune under **Deploy** (§6.5-6.6). |
| 7 | `specs/static-site-cache-control.md` | **New.** This spec. |
| 8 | `INDEX.md` | Spec row + "Feature currently in flight". |

No `.csproj` change is needed — `SpaceStroker.Deploy.Tests` already references `SpaceStroker.Deploy`
and carries xUnit 2.4.2 + Shouldly 4.2.1 on net8.0.

---

## 11. Unit tests

Project: `deploy/tools/SpaceStroker.Deploy.Tests` (xUnit + Shouldly, the suite added by #98).
Run with `dotnet test` from `deploy/tools/`.

| Test file | Test case | Expected |
| --- | --- | --- |
| `StaticSiteCacheTests.cs` | `ForPath_RootHtml_IsNoCache` — `index.html` on both sites; `404.html`, `verify.html`, `embed-example.html` | `HtmlCacheControl` (AC 1, 4) |
| | `ForPath_NestedHtml_IsNoCache` — `cookies/index.html` on the lp | `HtmlCacheControl` — the `.html` test beats the directory test (AC 4) |
| | `ForPath_HashedPrefixes_AreImmutable` — `js/app.abc.js`, `css/app.abc.css`, `img/bg.abc.png`, `media/x.abc.wav` (front); `_astro/x.abc.js` (lp) | `ImmutableCacheControl` (AC 2) |
| | `ForPath_UnhashedNonHtml_IsDefault` — `favicon.ico`, `og-image.png`, `robots.txt`, `sitemap-0.xml`, `fonts/vt323-latin.woff2` | `DefaultCacheControl` (AC 3) |
| | `ForPath_UnknownDirectory_IsDefaultNotImmutable` — `something-new/file.bin` | `DefaultCacheControl` — fail-safe direction (AC 3) |
| | `ForPath_FrontPrefixes_DoNotApplyToLp` — `js/app.abc.js` resolved for `StaticSite.Lp` | `DefaultCacheControl`, **not** immutable (AC 8) |
| | `IsExcluded_Sourcemaps` — `js/app.abc.js.map` excluded; `js/app.abc.js` not | true / false (AC 5) |
| | `Plan_AssetSteps_HaveNoDeleteFlag` — every `AssetSync` step | arguments contain no ` -d ` token (AC 6) |
| | `Plan_RootStep_DeletesAndExcludesHashedPrefixes` — the single `RootSync` step | contains `-d`, and an `-x` whose regex matches `js/app.abc.js` but not `index.html` (AC 7) |
| | `Plan_RootStep_ExclusionIsOneAlternationRegex` — count of `-x` occurrences in the root step | exactly 1 (gsutil honours only one) |
| | `Plan_AssetStepsPrecedeRootStep` — index of last `AssetSync` vs the `RootSync` | asset index < root index (AC 9, §1.3 rule 1) |
| | `Plan_UploadHeadersAreNeverWeakerThanFinalSetMeta` — for each tier, the upload-time `-h` vs the tier's `setmeta` value | upload header is `no-cache` or equal to the final value; never a longer TTL (AC 9) |
| | `Plan_EmitsSetMetaForEveryTierPresent` — front and lp plans | one immutable step, one HTML step, one 1h step (AC 10) |
| | `Plan_OmitsSetMetaStepWithNoTargets` — a dist with no unhashed root file | no empty-target `SetMeta` step is emitted |
| | `Plan_AssetStepsOnlyForPrefixesPresent` — front dist listing without `media` | 3 `AssetSync` steps, none referencing `media` |
| | `Plan_HtmlSetMetaIncludesNestedPages` — lp with `cookies/index.html` in `htmlPaths` | the HTML `SetMeta` step names `gs://B/cookies/index.html` (AC 4) |
| | `Plan_LpUsesAstroPrefixOnly` — lp plan | one `AssetSync` for `_astro`; no `js`/`css`/`img`/`media` anywhere in the plan (AC 8) |
| | `Plan_SourcemapsExcludedFromEveryStep` — front plan | every rsync step's `-x` regex matches `app.abc.js.map` (AC 5) |

`BuildManifestTests.cs` (new, 18 tests) covers §6.6: timestamp round-trip, undateable manifests being
**retained** rather than assumed expired, the retention edge exactly at 24h, `PruneTargets` deleting
only what no retained build names, the **fail-closed** empty-keep-set case, `gsutil ls` parsing
(directory headers, blanks, other buckets), manifest-body union/dedup, and `Batch` respecting the
command-line cap including an oversized single target.

`TeardownDecisionsTests.cs` is **not** modified — nothing in this card touches teardown. The whole
suite must still be green (AC 12): **76 tests**, 0 failed.

---

## 12. Acceptance criteria

Copied 1:1 from the Trello **Acceptance Criteria** checklist. They are split on purpose: `[merge]`
items are verifiable from the diff and `dotnet test`; `[deferred]` items can only be checked against
live headers **after an operator runs a deploy**, exactly as card #102's are. A green `[merge]` group
must never be read as "confirmed in production".

**Verifiable at merge**

1. Given the cache-control policy class, when asked for `index.html` on either site, then it returns `no-cache`.
2. Given the policy class, when asked for a path under the front's `js/ css/ img/ media/` or the lp's `_astro/`, then it returns `public, max-age=31536000, immutable`.
3. Given the policy class, when asked for `fonts/*.woff2`, a favicon, `og-image.png`, `robots.txt` or `sitemap*.xml`, then it returns an explicit `public, max-age=3600`.
4. Given the policy class, when asked for the front's `verify.html`, `embed-example.html` or the lp's `404.html`, then it returns `no-cache` (same tier as `index.html`).
5. Given the front's upload plan, when the `js/` pass is built, then `*.map` is excluded so no sourcemap is uploaded.
6. Given the upload plan for either site, when the hashed-asset passes are built, then none of them carries `-d`, so a previous build's chunks are never deleted.
7. Given the upload plan for either site, when the everything-else pass is built, then it carries `-d` and excludes every hashed-asset prefix, so deletion still applies to the top level but never reaches retained assets.
8. Given the lp upload plan, when its prefixes are resolved, then `_astro/` is its only hashed prefix and none of the front's `js/css/img/media` prefixes are used.
9. Given a site upload, when the passes are ordered, then every upload-time `-h` is at least as conservative as the final `setmeta` value, so the intermediate window can only under-cache, never over-cache.
10. Given a site upload, when the passes are built, then a `setmeta` convergence pass exists for every tier, so objects rsync skipped as unchanged are still re-stamped.
11. Given `Deployer.cs` after the change, when the upload method is reviewed, then the `invalidate-cdn-cache --path="/*"` call is unchanged and still best-effort (`Run`, not `RunOrThrow`).
12. Given `dotnet test` on `SpaceStroker.Deploy.Tests`, when the suite runs, then it is green — the new policy tests and the existing `TeardownDecisionsTests` both pass.
13. Given `deploy/README.md`, when the deploy flow is read, then it documents the three cache tiers, the retained-asset behaviour, and the manual pruning command.

**Deferred — needs an operator deploy**

14. Given a completed `deploy front`, when `https://app.spacestroker.com/` is requested, then the response carries `Cache-Control: no-cache`.
15. Given a completed `deploy front`, when a hashed asset such as `/js/app.<hash>.js` is requested, then it carries `Cache-Control: public, max-age=31536000, immutable`.
16. Given a completed `deploy lp`, when `https://spacestroker.com/` and `/404.html` are requested, then both carry `Cache-Control: no-cache`.
17. Given a completed `deploy lp`, when `/robots.txt`, `/og-image.png` and `/fonts/vt323-latin.woff2` are requested, then each carries `Cache-Control: public, max-age=3600` — proving `setmeta` repaired pre-existing objects rsync skipped.
18. Given two consecutive front deploys, when the FIRST build's hashed chunk URL is requested after the second deploy, then it still returns 200 — assets are retained, not reaped.
19. Given a completed `deploy front`, when `/js/app.<hash>.js.map` is requested, then it returns 404 — sourcemaps are no longer uploaded.

---

## 13. Manual QA

No local GCS is available, so verification before the operator deploy is the test suite plus a
**dry read of the plan**:

1. `cd deploy/tools && dotnet test` → green (AC 12).
2. Build a real front dist (`cd monkify-front && yarn build`) so the plan runs against a genuine
   listing, then temporarily print the plan instead of executing it (a scratch `Console.WriteLine`
   over `StaticSiteCache.Plan`, reverted before commit) and confirm by eye:
   - asset steps come first and carry no `-d`;
   - the root step's `-x` regex covers `js|css|img|media` and `.map`;
   - the HTML `setmeta` step names `index.html`, `verify.html`, `embed-example.html`.
3. Repeat for the lp (`cd monkify-lp && npm run build`) and confirm `_astro` is the only asset step,
   `fonts` gets a recursive 1h step, and `cookies/index.html` appears in the HTML step. **Pull
   `monkify-lp` first** — the `cookies` page only exists on `origin/master` (see §15).

**After the operator deploys** (their step, not an agent's), AC 14-19 are checked with
`curl -I <url>` against both hosts.

---

## 14. Trello / pipeline

- **Source card:** [#103 — Set Cache-Control on static-site uploads](https://trello.com/c/bSSvCfKF) —
  list **Specs created**; the **Implementation** section on the card carries branch, worktree and
  spec path.
- **Companion:** [#102 — static-site HTTP status codes](https://trello.com/c/mkogi4tR), merged
  2026-07-27, **not yet applied**. Both changes land on the same next deploy and must be verified
  independently.
- **Next:** `/monkify-implement-feature 103`, then `/monkify-ship-feature 103`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-27 | Initial spec from refined card #103 (12 decisions, 19 acceptance criteria). Code exploration added three findings the card did not have: (a) the lp's `/cookies` page exists on `origin/master` and Astro builds it to **`dist/cookies/index.html`**, so the tier rule must test `.html` at **any depth** — a first-path-segment rule would have re-created this bug one directory down; (b) `gsutil rsync` honours only **one** `-x`, so the exclusions must be composed into a single alternation regex; (c) a `setmeta` step with an empty target list is an error, so tier steps must be omitted when nothing matches. |
| 2026-07-27 | **Decision 5 reversed during implementation.** The user challenged indefinite retention ("aren't we gonna pile trash old builds forever this way?"). Measurement showed the real churn is ~1.6 MB per code-only front deploy, not 26 MB — but unbounded growth still is not a policy. The original rejection of automated pruning was too broad: it correctly rules out deleting on **age alone**, not on *"absent from the build just published **and** retired longer than the window"*. Added §6.5 (what actually accumulates) and §6.6 (automatic 24h prune via per-deploy build manifests, fail-closed, `_builds/` shielded from the root `-d`). Window derived from the **open-tab** horizon, not from `Cache-Control` — HTML is `no-cache` and revalidates every load, so the app's own 30-60 min `useIdleDisconnect` ceiling is the real constraint. Retirement time, not object age, is what the manifests make measurable; a GCS lifecycle rule still cannot express it. |
