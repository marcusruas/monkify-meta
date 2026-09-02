// What to screenshot out of the real app, for the leaderboard launch video.
//
//   cd monkify-front && yarn serve --port 8080     (devnet, local API on :6001)
//   node tools/capture-ui.mjs leaderboard-launch
//
// ⚠️ THE BOARD DATA HERE IS MOCKED, on the owner's explicit call. The local dev database holds two
// test accounts from an old week, so the real page renders half-empty behind a "no rounds this
// week" banner. These shots exist to show the INTERFACE — the plinths, the type, the colour — not
// to report standings. The names are invented and the figures are stand-ins.
//
// Everything else on screen is the genuine app: the components, the fonts, the starfield, the
// header. That is the point of capturing rather than redrawing.

export const base = 'http://localhost:8080';

/* A believable week. Cosmic Sprint is 0.02 SOL a round and Space Jackpot is 0.1, so a leader on
   4.86 SOL has played a few hundred rounds — heavy, but not absurd. Figures descend smoothly
   because a real board does; a top score ten times the runner-up would read as fabricated. */
const now = new Date('2026-09-02T18:00:00Z');
const weekAgo = new Date(now.getTime() - 7 * 24 * 3600 * 1000);

const row = (rank, displayName, score) => ({
  rank,
  displayName,
  wallet: null,
  score,
  isYou: false,
  won: null,
  achievedAt: null,
  gameTypeName: null,
});

const gambleRace = {
  board: 'gambleRace',
  // isFallback FALSE is the whole reason for mocking: a true fallback paints the "no rounds this
  // week, showing the most recent week that had results" banner across the top of the shot.
  window: { kind: 'week', from: weekAgo.toISOString(), to: now.toISOString(), isFallback: false },
  scoreIsMultiplier: false,
  rows: [
    row(1, 'bananahands', 4.86),
    row(2, 'v0idmonk', 3.94),
    row(3, 'strokelord', 3.12),
    row(4, 'apeirogon', 2.48),
    row(5, 'kbd_smasher', 2.06),
    row(6, 'nebulanaut', 1.72),
    row(7, 'chimpwave', 1.34),
    row(8, 'orbital_ted', 1.08),
  ],
  you: null,
};

export const shots = [
  {
    // The hero: the board itself, cropped to the panel rather than the whole viewport, so it
    // composites into a 16:9 board without a lot of dead starfield around it.
    id: 'leaderboard-panel',
    path: '/leaderboard',
    mock: { '**/leaderboards*': { gambleRace, weeklyPnl: null, hallOfFame: null } },
    selector: '.arcade-panel, main section, main > div',
    width: 1440,
    height: 1000,
    scale: 2,
    settle: 1800,
  },
  {
    // The same page whole, for a scene that wants the app's chrome (header, wallet button) in
    // frame so it is unmistakably the product and not a graphic.
    id: 'leaderboard-page',
    path: '/leaderboard',
    mock: { '**/leaderboards*': { gambleRace, weeklyPnl: null, hallOfFame: null } },
    width: 1440,
    height: 900,
    scale: 2,
    settle: 1800,
  },
];
