/* Nightfall dashboard.
 *
 * Selection, hover quoting and the confirmation live here; every money rule lives on the host. In
 * particular the SIMULATE/LIVE check is server-side — this file could be tampered with in devtools
 * and still not make a simulating host broadcast anything.
 */

(() => {
  "use strict";

  const $ = (id) => document.getElementById(id);

  const el = {
    grid: $("grid"),
    empty: $("empty"),
    meta: $("meta"),
    mode: $("mode"),
    stamp: $("stamp"),
    hover: $("hover"),
    percent: $("percent"),
    count: $("count"),
    sell: $("sell"),
    veil: $("veil"),
    dialogBody: $("dialog-body"),
    confirm: $("confirm"),
    cancel: $("cancel"),
    marquee: $("marquee"),
  };

  /** Server state plus purely local selection. */
  let state = null;
  const selected = new Set();
  let lastClicked = null;
  let pendingSell = null;

  // ---------------------------------------------------------------- formatting

  const nf = new Intl.NumberFormat("en-US");

  const fmtTokens = (value) =>
    nf.format(Number(value) || 0);

  const fmtSol = (value, places = 4) =>
    (Number(value) || 0).toLocaleString("en-US", { minimumFractionDigits: 0, maximumFractionDigits: places });

  const fmtPct = (value) => `${(Number(value) || 0).toFixed(4)}%`;

  const shortSig = (sig) => (sig ? `${sig.slice(0, 4)}…${sig.slice(-4)}` : "");

  // ---------------------------------------------------------------- api

  async function api(path, options) {
    const response = await fetch(path, options);
    const body = await response.json().catch(() => null);

    if (!response.ok) throw new Error((body && body.error) || `HTTP ${response.status}`);

    return body;
  }

  const post = (path, payload) =>
    api(path, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(payload || {}),
    });

  // ---------------------------------------------------------------- sell %

  /** null = blank = 100%. Returns undefined when the field holds something unusable. */
  function readPercent() {
    const raw = el.percent.value.trim();
    if (raw === "") return null;

    const value = Number(raw);
    if (!Number.isFinite(value) || value <= 0 || value > 100) return undefined;

    return value;
  }

  function percentLabel() {
    const percent = readPercent();
    return percent === null || percent === undefined ? "100" : String(percent);
  }

  // ---------------------------------------------------------------- render

  function render() {
    if (!state) return;

    document.body.classList.toggle("live", state.live);

    for (const button of el.mode.querySelectorAll(".mode-opt"))
      button.classList.toggle("on", String(state.live) === button.dataset.live);

    el.meta.innerHTML =
      `<span>mint <b>${state.tokenMint.slice(0, 4)}…${state.tokenMint.slice(-4)}</b></span>` +
      `<span>pool <b>${state.wallets.length}</b></span>` +
      `<span>slippage <b>${state.slippagePercent}%</b></span>` +
      `<span>max impact <b>${state.maxPriceImpactPercent}%</b></span>` +
      `<span>concurrency <b>${state.maxConcurrentSells}</b></span>`;

    el.stamp.textContent = state.refreshedUtc
      ? `updated ${new Date(state.refreshedUtc).toLocaleTimeString()}`
      : "";

    el.empty.hidden = state.wallets.length > 0;

    // Drop selections that are no longer sellable, so the count can never promise a wallet the
    // batch would silently skip.
    for (const key of [...selected]) {
      const wallet = state.wallets.find((w) => w.publicKey === key);
      if (!wallet || !wallet.selectable) selected.delete(key);
    }

    el.grid.innerHTML = "";
    for (const wallet of state.wallets) el.grid.appendChild(card(wallet));

    const count = selected.size;
    el.count.textContent = `${count} SELECTED`;
    el.sell.disabled = count === 0 || readPercent() === undefined;
  }

  function card(wallet) {
    const node = document.createElement("article");
    node.className = "w";
    node.dataset.key = wallet.publicKey;

    const selling = wallet.status === "Selling" || wallet.status === "Sent";
    if (selected.has(wallet.publicKey)) node.classList.add("sel");
    if (selling) node.classList.add("busy");
    if (!wallet.selectable) node.classList.add("blocked");

    const tag = tagFor(wallet, selling);

    node.innerHTML =
      (tag ? `<span class="w-tag ${tag.kind}">${tag.text}</span>` : "") +
      `<div class="w-top">
         <span class="w-alias">${escape(wallet.alias)}</span>
         <span class="w-share">${fmtPct(wallet.supplyPercent)}</span>
       </div>
       <div class="w-line"><span>SOL</span><b>${fmtSol(wallet.sol, 4)}</b></div>
       <div class="w-line"><span>TOKENS</span><b>${fmtTokens(wallet.tokenAmount)}</b></div>
       <div class="w-est">
         <span>${wallet.realizedSol != null ? "GOT" : "EST"}</span>
         <b>${fmtSol(wallet.realizedSol != null ? wallet.realizedSol : wallet.estimatedSol)} SOL</b>
       </div>` +
      (wallet.reason ? `<div class="w-note">${escape(wallet.reason)}</div>` : "") +
      (wallet.signature ? `<div class="w-sig">${escape(shortSig(wallet.signature))}</div>` : "");

    return node;
  }

  function tagFor(wallet, selling) {
    if (selling) return { kind: "", text: "SELLING" };
    if (wallet.status === "Sold") return { kind: "good", text: "SOLD" };
    if (wallet.status === "Failed") return { kind: "err", text: "FAILED" };
    if (wallet.status === "Unconfirmed") return { kind: "", text: "UNCONFIRMED" };
    if (wallet.status === "Simulated") return { kind: "good", text: "SIMULATED" };
    if (!wallet.selectable) return { kind: "bad", text: "BLOCKED" };
    return null;
  }

  const escape = (value) =>
    String(value).replace(/[&<>"']/g, (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

  // ---------------------------------------------------------------- selection

  function toggle(key, additive, range) {
    const wallet = state.wallets.find((w) => w.publicKey === key);
    if (!wallet || !wallet.selectable) return;

    if (range && lastClicked) {
      const keys = state.wallets.filter((w) => w.selectable).map((w) => w.publicKey);
      const from = keys.indexOf(lastClicked);
      const to = keys.indexOf(key);

      if (from !== -1 && to !== -1)
        for (const k of keys.slice(Math.min(from, to), Math.max(from, to) + 1)) selected.add(k);
    } else if (additive) {
      selected.has(key) ? selected.delete(key) : selected.add(key);
    } else {
      // A plain click on an already-solo selection clears it; otherwise it becomes the selection.
      const solo = selected.size === 1 && selected.has(key);
      selected.clear();
      if (!solo) selected.add(key);
    }

    lastClicked = key;
    render();
  }

  // A click SELECTS. It never opens a sell dialog — that is what SELL SELECTED is for, and it is
  // why a stray click can never start a sale.
  el.grid.addEventListener("click", (event) => {
    const node = event.target.closest(".w");
    if (!node) return;

    toggle(node.dataset.key, event.ctrlKey || event.metaKey, event.shiftKey);
  });

  $("select-all").addEventListener("click", () => {
    for (const wallet of state.wallets) if (wallet.selectable) selected.add(wallet.publicKey);
    render();
  });

  $("clear").addEventListener("click", () => {
    selected.clear();
    render();
  });

  $("refresh").addEventListener("click", async () => {
    state = await post("/api/refresh");
    render();
  });

  // ---------------------------------------------------------------- marquee

  let drag = null;

  el.grid.addEventListener("mousedown", (event) => {
    if (event.button !== 0 || event.target.closest(".w")) return;

    drag = { x: event.pageX, y: event.pageY, additive: event.ctrlKey || event.metaKey };
    if (!drag.additive) selected.clear();
  });

  window.addEventListener("mousemove", (event) => {
    if (!drag) return;

    const box = {
      left: Math.min(drag.x, event.pageX),
      top: Math.min(drag.y, event.pageY),
      width: Math.abs(event.pageX - drag.x),
      height: Math.abs(event.pageY - drag.y),
    };

    Object.assign(el.marquee.style, {
      left: `${box.left - window.scrollX}px`,
      top: `${box.top - window.scrollY}px`,
      width: `${box.width}px`,
      height: `${box.height}px`,
    });
    el.marquee.hidden = false;

    for (const node of el.grid.children) {
      const rect = node.getBoundingClientRect();
      const hit =
        rect.left + window.scrollX < box.left + box.width &&
        rect.right + window.scrollX > box.left &&
        rect.top + window.scrollY < box.top + box.height &&
        rect.bottom + window.scrollY > box.top;

      const wallet = state.wallets.find((w) => w.publicKey === node.dataset.key);

      if (hit && wallet && wallet.selectable) selected.add(node.dataset.key);
    }

    render();
  });

  window.addEventListener("mouseup", () => {
    if (!drag) return;

    drag = null;
    el.marquee.hidden = true;
  });

  // ---------------------------------------------------------------- hover quote

  let hoverKey = null;
  let hoverTimer = null;

  el.grid.addEventListener("mouseover", (event) => {
    const node = event.target.closest(".w");
    if (!node || node.dataset.key === hoverKey) return;

    hoverKey = node.dataset.key;
    clearTimeout(hoverTimer);

    // 300 ms debounce: hover fires on every mouse move, and a quote is a rate-limited network call.
    // Sweeping across the pool must not spend the whole Jupiter quota.
    hoverTimer = setTimeout(() => showQuote(node), 300);
  });

  el.grid.addEventListener("mouseout", (event) => {
    if (event.relatedTarget && event.relatedTarget.closest(".w")) return;

    hoverKey = null;
    clearTimeout(hoverTimer);
    el.hover.hidden = true;
  });

  el.grid.addEventListener("mousemove", (event) => {
    if (el.hover.hidden) return;
    place(event);
  });

  function place(event) {
    const pad = 14;
    const width = el.hover.offsetWidth || 300;
    const left = Math.min(event.clientX + pad, window.innerWidth - width - pad);
    const top = Math.min(event.clientY + pad, window.innerHeight - el.hover.offsetHeight - pad);

    el.hover.style.left = `${Math.max(pad, left)}px`;
    el.hover.style.top = `${Math.max(pad, top)}px`;
  }

  async function showQuote(node) {
    const key = node.dataset.key;
    const wallet = state.wallets.find((w) => w.publicKey === key);
    if (!wallet) return;

    const head = `<span class="hd">▸ ${escape(wallet.alias)} · ${wallet.realizedSol != null ? "SOLD" : `QUOTE · ${percentLabel()}%`}</span>`;

    // A settled wallet shows what it ACTUALLY got, not a fresh quote — the realized figure and the
    // signature are what the operator needs after the fact.
    if (wallet.realizedSol != null) {
      show(
        head +
          `REALIZED <b>${fmtSol(wallet.realizedSol)} SOL</b><br />` +
          (wallet.quotedSol != null ? `QUOTED <b>${fmtSol(wallet.quotedSol)} SOL</b><br />` : "") +
          `<span style="word-break:break-all">${escape(wallet.signature || "")}</span>`,
      );
      return;
    }

    if (!wallet.selectable) {
      show(head + `<span class="warn">${escape(wallet.reason || "Not sellable.")}</span>`);
      return;
    }

    show(head + "quoting…");

    try {
      const percent = readPercent();
      const query = percent === null || percent === undefined ? "" : `&percent=${percent}`;
      const quote = await api(`/api/quote?wallet=${encodeURIComponent(key)}${query}`);

      if (hoverKey !== key) return; // the pointer moved on while we were waiting

      const impact = quote.priceImpactPercent;
      const hot = impact > state.maxPriceImpactPercent;

      show(
        head +
          `SELLS <b>${fmtTokens(quote.tokenAmount)}</b> → <b>${fmtSol(quote.outSol)} SOL</b><br />` +
          `PRICE IMPACT <b class="${hot ? "warn" : ""}">${impact.toFixed(2)}%</b> · MAX <b>${state.maxPriceImpactPercent}%</b>` +
          (hot ? `<br /><span class="warn">above the cap — this wallet would abort</span>` : ""),
      );
    } catch (error) {
      if (hoverKey === key) show(head + `<span class="warn">${escape(error.message)}</span>`);
    }
  }

  function show(html) {
    el.hover.innerHTML = html;
    el.hover.hidden = false;
  }

  // ---------------------------------------------------------------- sell

  el.percent.addEventListener("input", () => {
    el.percent.classList.toggle("bad", readPercent() === undefined);
    render();
  });

  el.sell.addEventListener("click", async () => {
    const percent = readPercent();
    if (percent === undefined || selected.size === 0) return;

    const wallets = state.wallets.filter((w) => selected.has(w.publicKey));
    const blocked = state.wallets.filter((w) => !w.selectable && w.tokenAmount > 0);

    // Quote every selected wallet so the dialog shows a real total, not an estimate. Failures
    // degrade to "—" rather than blocking the confirmation.
    el.sell.disabled = true;
    const quotes = await Promise.all(
      wallets.map((w) =>
        api(
          `/api/quote?wallet=${encodeURIComponent(w.publicKey)}` +
            (percent === null ? "" : `&percent=${percent}`),
        ).catch(() => null),
      ),
    );
    el.sell.disabled = false;

    const total = quotes.reduce((sum, q) => sum + (q ? q.outSol : 0), 0);

    el.dialogBody.innerHTML =
      wallets
        .map((w, i) => {
          const q = quotes[i];
          return `<div class="d-row"><span>${escape(w.alias)}</span><b>${
            q ? `${fmtTokens(q.tokenAmount)} → ${fmtSol(q.outSol)} SOL` : "quote unavailable"
          }</b></div>`;
        })
        .join("") +
      `<div class="d-total"><span>${wallets.length} WALLET${wallets.length === 1 ? "" : "S"} · ${percentLabel()}%</span><b>~${fmtSol(total)} SOL</b></div>` +
      (blocked.length
        ? `<div class="d-skip">Skipping ${blocked.length} wallet${blocked.length === 1 ? "" : "s"}: ${blocked
            .map((w) => escape(w.alias))
            .join(", ")}</div>`
        : "") +
      (state.live
        ? `<div class="d-warn">LIVE — this signs and broadcasts real transactions. A sale cannot be undone.</div>`
        : `<div class="d-sim">SIMULATE — quotes and transactions are built, but nothing is broadcast.</div>`);

    pendingSell = { wallets: wallets.map((w) => w.publicKey), percent };
    el.veil.hidden = false;
  });

  el.cancel.addEventListener("click", () => {
    el.veil.hidden = true;
    pendingSell = null;
  });

  el.confirm.addEventListener("click", async () => {
    if (!pendingSell) return;

    el.veil.hidden = true;
    const request = pendingSell;
    pendingSell = null;

    try {
      await post("/api/sell", { wallets: request.wallets, percent: request.percent });
      selected.clear();
      render();
    } catch (error) {
      alert(error.message);
    }
  });

  // ---------------------------------------------------------------- mode

  el.mode.addEventListener("click", async (event) => {
    const button = event.target.closest(".mode-opt");
    if (!button) return;

    const live = button.dataset.live === "true";
    if (live === state.live) return;

    // One deliberate confirmation before arming. The host still enforces the mode server-side.
    if (live && !confirm("Switch to LIVE?\n\nSells will be signed and broadcast for real, and a sale cannot be undone.")) {
      return;
    }

    await post("/api/mode", { live });
    state.live = live;
    render();
  });

  // ---------------------------------------------------------------- live events

  function listen() {
    const source = new EventSource("/api/events");

    source.addEventListener("sell", async () => {
      state = await api("/api/state");
      render();
    });

    source.addEventListener("mode", async () => {
      state = await api("/api/state");
      render();
    });

    // The browser reconnects an EventSource on its own; nothing to do but let it.
  }

  // Keep the picture current even between SSE events (balances move without a sell).
  setInterval(async () => {
    try {
      state = await api("/api/state");
      render();
    } catch {
      /* the host is down or restarting — the next tick picks it up */
    }
  }, 5000);

  (async () => {
    state = await api("/api/state");
    render();
    listen();
  })();
})();
