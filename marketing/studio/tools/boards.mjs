// The boards (nominal publish sizes), in ONE place, because two programs must agree about them:
// `build.mjs` draws a piece at the size declared here, and `export-png.mjs` checks the PNG against
// the same number. If each kept its own copy, the check would be the program asking itself whether
// it got it right, which is a question that always answers yes.
//
// That is not hypothetical. In the campaign this system is adapted from, the first version of the
// check measured the element and compared it against the same element. It passed on a deliberately
// wrong piece, because 1016x1806 is always equal to 1016x1806 — and six slides were published at
// 94% of their nominal size before ffprobe caught it. The number has to come from OUTSIDE the thing
// being measured.
//
// `w`/`h` are the nominal publish size. A PNG that does not match is a stop-everything (see
// references/platforms.md §"Measure the piece, not the preview").
// `where` is the human label, used in LEGENDS.md and on each piece's toolbar.

export const BOARDS = {
  /** The Instagram default. Takes more screen than the square. */
  feed: { w: 1080, h: 1350, where: 'IG feed 1080x1350' },

  /** Story, Reels cover, TikTok. Also the correct cover for a YouTube SHORT. */
  story: { w: 1080, h: 1920, where: 'story / reels 1080x1920' },

  /** Square: carousels, LinkedIn, a tidy grid. */
  square: { w: 1080, h: 1080, where: 'square 1080x1080' },

  /**
   * X timeline. ⚠️ NOT the feed piece re-cropped.
   * The X timeline crops a tall image hard — a 1080x1920 is reduced to roughly 16:9, keeping
   * something like a third to two thirds of the height, which on a piece made of type eats the
   * logo and half the headline. A piece for X is BORN 16:9.
   * Two things fall out of drawing it properly rather than cropping down: most source art is
   * landscape to begin with and finally fits, and the text axis moves — on a landscape board the
   * copy sits in the LEFT HALF and the veil comes in from the left, not from the bottom.
   */
  x16: { w: 1600, h: 900, where: 'X timeline 1600x900' },

  /**
   * ⚠️ Landscape VIDEO thumbnail only. This is NOT a Short's cover.
   * YouTube has two formats and this is the trap: a Short stands up in the Shorts feed and in the
   * channel grid, so a landscape cover is centre-cropped and loses its sides — the same defect that
   * makes the tall piece fail on X. A Short's cover is UPRIGHT: use `story`.
   * Also worth checking before making one at all: the frame YouTube picks on its own is often fine,
   * especially for a video that opens on its subject instead of fading in.
   */
  thumb: { w: 1280, h: 720, where: 'landscape video thumbnail 1280x720' },
};

/** Board names, for finding which one a built piece declared on its <body>. */
export const BOARD_NAMES = Object.keys(BOARDS);

/** Video formats. The stage is sized in whole pixels at record time, never in viewport units. */
export const FORMATS = {
  reels: { w: 1080, h: 1920, where: 'Reels / Shorts / TikTok 9:16' },
  square: { w: 1080, h: 1080, where: 'square feed video 1:1' },
  /* X, YouTube, the site. ⚠️ For X this is the CORRECT shape: the timeline shows a 16:9 whole and
     crops taller media hard, the same reason the `x16` still board exists. X also autoplays MUTED,
     so a landscape piece has to read with the sound off. */
  wide: { w: 1920, h: 1080, where: 'X / YouTube / site 16:9' },
};

export const FORMAT_NAMES = Object.keys(FORMATS);
