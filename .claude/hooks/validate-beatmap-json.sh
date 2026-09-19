#!/usr/bin/env bash
# Block a commit that would stage a broken beatmap chart.
#
# Adapted from Claude-Code-Game-Studios' validate-commit.sh, retargeted from its
# assets/data/*.json convention to this project's Assets/Beatmaps/ tree, with
# four corrections to the original:
#
#   - it fired on every Bash call; this exits early unless the command is a commit
#   - it used `command -v python`, which on Windows matches the Microsoft Store
#     App Execution Alias stub that prints "Python was not found" and returns 9009
#   - it read the working tree, so it blocked commits that were actually fine and
#     waved through ones that were not; check_beatmaps.py --staged reads the index
#   - it blocked when python was missing, which is unrecoverable from inside a
#     session; this warns and lets the commit through, because CI is the real gate
#
# Worth knowing: this only runs when `git commit` goes through Claude's Bash tool.
# A commit from a terminal, Rider or VS Git never reaches it. CI is the gate that
# always applies.

set -uo pipefail

PAYLOAD=$(cat)

# --- is this a commit? ------------------------------------------------------
# The PreToolUse matcher is "Bash", so this runs on every shell command. Get out
# of the way immediately for the ones that are not commits.
if ! printf '%s' "$PAYLOAD" | grep -qE '"command"[[:space:]]*:[[:space:]]*"[^"]*git[[:space:]]+commit'; then
    exit 0
fi

# --- find a python that actually works --------------------------------------
# `-c "import json"` rather than `command -v`: the Store stub resolves but fails.
PYTHON=""
for candidate in python python3 py; do
    if "$candidate" -c "import json" >/dev/null 2>&1; then
        PYTHON="$candidate"
        break
    fi
done

if [ -z "$PYTHON" ]; then
    echo "NOTE: skipping beatmap chart validation (no working python found)." >&2
    echo "      CI still checks this on push." >&2
    exit 0
fi

# --- validate ---------------------------------------------------------------
ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
CHECKER="$ROOT/ci/check_beatmaps.py"

[ -f "$CHECKER" ] || exit 0

if ! OUTPUT=$(cd "$ROOT" && "$PYTHON" "$CHECKER" --staged 2>&1); then
    echo "BLOCKED: a staged beatmap chart is not valid JSON." >&2
    echo "$OUTPUT" >&2
    exit 2
fi

exit 0
