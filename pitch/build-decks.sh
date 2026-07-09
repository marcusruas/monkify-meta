#!/usr/bin/env bash
# Build every supplier pitch deck (deck.md → deck.pptx) with the Monkify Marp theme.
#
# Requirements:
#   - Node.js + npx (marp-cli is fetched via npx on first run and cached).
#   - A Chromium-based browser for pptx export. Auto-detected: Edge, then Chrome.
#     Override with:  CHROME_PATH="/path/to/browser"  ./build-decks.sh
#
# Usage:
#   ./build-decks.sh                     # build all pitch/suppliers/*/deck.md
#   ./build-decks.sh suppliers/playtech  # build one supplier folder
#
set -euo pipefail
cd "$(dirname "$0")"                      # pitch/

THEME="assets/theme/monkify.css"

# --- locate a Chromium browser for Marp's pptx export ---
if [ -z "${CHROME_PATH:-}" ]; then
  for c in \
    "/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
    "/c/Program Files/Microsoft/Edge/Application/msedge.exe" \
    "/c/Program Files/Google/Chrome/Application/chrome.exe" \
    "/c/Program Files (x86)/Google/Chrome/Application/chrome.exe" \
    "$(command -v google-chrome 2>/dev/null || true)" \
    "$(command -v chromium 2>/dev/null || true)" ; do
    if [ -n "$c" ] && [ -f "$c" ]; then CHROME_PATH="$c"; break; fi
  done
fi
if [ -z "${CHROME_PATH:-}" ]; then
  echo "ERROR: no Chromium browser found. Set CHROME_PATH=... and re-run." >&2
  exit 1
fi
export CHROME_PATH
echo "Using browser: $CHROME_PATH"

# --- which decks? ---
if [ $# -gt 0 ]; then
  DECKS=()
  for arg in "$@"; do DECKS+=("${arg%/}/deck.md"); done
else
  mapfile -t DECKS < <(ls suppliers/*/deck.md 2>/dev/null)
fi
if [ ${#DECKS[@]} -eq 0 ]; then echo "No deck.md files found."; exit 0; fi

# --- build each ---
for deck in "${DECKS[@]}"; do
  out="${deck%.md}.pptx"
  echo "==> $deck -> $out"
  npx -y @marp-team/marp-cli@latest "$deck" \
    --theme "$THEME" \
    --pptx -o "$out" \
    --image-scale 3 \
    --jpeg-quality 100 \
    --allow-local-files
done
echo "Done. Built ${#DECKS[@]} deck(s)."
