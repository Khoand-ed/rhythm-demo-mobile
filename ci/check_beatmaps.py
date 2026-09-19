#!/usr/bin/env python3
"""Beatmap chart validation.

One implementation, two callers, so what blocks locally and what blocks in CI can
never drift apart:

  --staged            what .claude/hooks/validate-beatmap-json.sh runs before a
                      commit. Parse validity only: fast, and no rule that could
                      reject a chart someone is deliberately experimenting with.
  --all [--strict]    what CI runs. Adds the structural rules.

This is not ceremony. Assets/Editor/Beatmap/BeatmapJsonWriter.cs builds its JSON
by hand with a StringBuilder, and its Escape() covers only backslash and quote --
a song name or author containing a newline or tab produces invalid JSON, and
note.lane is interpolated with no escaping at all. Its Number() helper formats
with "0.###", which turns a non-finite float into NaN or Infinity: Python's json
module accepts both as an extension, so parse_constant below rejects them
explicitly. Unity's JsonUtility does not accept them, so a chart containing one
parses here and then fails on the device.

Exit 0 when clean, 1 otherwise.
"""

import argparse
import glob
import json
import math
import os
import subprocess
import sys

BEATMAPS = "Assets/Beatmaps"
NOTE_TYPES = {"Tap", "Hold", "Twin"}
LANES = {"Left", "Right"}

# Twin notes occupy both lanes at once, so BeatmapImporter.cs skips the lane
# lookup for them; requiring one here would reject every valid Twin.
NEEDS_LANE = {"Tap", "Hold"}


def reject_constant(name):
    raise ValueError(f"{name} is not valid JSON (BeatmapJsonWriter can emit this "
                     f"for a non-finite float; Unity's JsonUtility will reject it)")


def parse(text, label):
    """Parse, returning (data, error). Never raises."""
    try:
        return json.loads(text, parse_constant=reject_constant), None
    except ValueError as exc:
        return None, f"{label}: {exc}"


def finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) \
        and math.isfinite(value)


def check_structure(data, label):
    """The rules that only CI enforces. Returns a list of problem strings."""
    problems = []

    def bad(message):
        problems.append(f"{label}: {message}")

    if not isinstance(data, dict):
        bad("top level is not an object")
        return problems

    if not data.get("songId"):
        bad("songId is missing or empty")

    bpm = data.get("bpm")
    if not finite(bpm) or bpm <= 0:
        bad(f"bpm must be a positive number (got {bpm!r})")

    difficulty = data.get("difficulty")
    if not isinstance(difficulty, int) or isinstance(difficulty, bool) \
            or not 1 <= difficulty <= 10:
        bad(f"difficulty must be an integer 1-10 (got {difficulty!r})")

    for optional in ("offset", "beatPhase"):
        if optional in data and not finite(data[optional]):
            bad(f"{optional} must be a finite number (got {data[optional]!r})")

    notes = data.get("notes")
    if not isinstance(notes, list):
        bad("notes is missing or not a list")
        return problems

    seen_ids = {}

    for index, note in enumerate(notes):
        where = f"note[{index}]"

        if not isinstance(note, dict):
            bad(f"{where} is not an object")
            continue

        note_id = note.get("id")
        if note_id is None:
            bad(f"{where} has no id")
        elif note_id in seen_ids:
            bad(f"{where} reuses id {note_id!r} (first seen at note[{seen_ids[note_id]}])")
        else:
            seen_ids[note_id] = index

        note_type = note.get("type")
        if note_type not in NOTE_TYPES:
            bad(f"{where} type {note_type!r} is not one of {sorted(NOTE_TYPES)}")
            continue

        where = f"{where} ({note_type} id={note_id!r})"

        if note_type in NEEDS_LANE and note.get("lane") not in LANES:
            bad(f"{where} lane {note.get('lane')!r} is not one of {sorted(LANES)}")

        if note_type == "Hold":
            start, end = note.get("startTime"), note.get("endTime")

            if not finite(start) or start < 0:
                bad(f"{where} startTime must be a finite number >= 0 (got {start!r})")
            if not finite(end) or end < 0:
                bad(f"{where} endTime must be a finite number >= 0 (got {end!r})")
            if finite(start) and finite(end) and end <= start:
                bad(f"{where} endTime {end} is not after startTime {start}")
        else:
            time = note.get("time")
            if not finite(time) or time < 0:
                bad(f"{where} time must be a finite number >= 0 (got {time!r})")

    return problems


def staged_charts():
    """(path, text) for every staged beatmap chart, read from the staged blob.

    Reading the index rather than the working tree matters in both directions:
    stage a good version and keep editing, and the working tree would block a
    commit that is actually fine; stage a broken version and then fix the file,
    and the working tree would wave a broken commit through.
    """
    out = subprocess.run(
        ["git", "-c", "core.quotepath=off", "diff", "--cached", "--name-only", "-z"],
        check=True, stdout=subprocess.PIPE,
    ).stdout

    charts = []

    for raw in out.split(b"\0"):
        if not raw:
            continue

        path = raw.decode("utf-8", "surrogateescape")
        if not path.startswith(BEATMAPS + "/") or not path.endswith(".json"):
            continue

        blob = subprocess.run(["git", "show", f":{path}"], stdout=subprocess.PIPE)
        if blob.returncode != 0:
            continue  # staged deletion

        charts.append((path, blob.stdout.decode("utf-8-sig", "replace")))

    return charts


def all_charts():
    charts = []

    for path in sorted(glob.glob(os.path.join(BEATMAPS, "**", "*.json"), recursive=True)):
        with open(path, "rb") as handle:
            charts.append((path.replace(os.sep, "/"),
                           handle.read().decode("utf-8-sig", "replace")))

    return charts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--staged", action="store_true",
                        help="validate charts staged for commit")
    source.add_argument("--all", action="store_true",
                        help="validate every chart under " + BEATMAPS)
    parser.add_argument("--strict", action="store_true",
                        help="also apply structural rules (CI only)")
    args = parser.parse_args()

    charts = staged_charts() if args.staged else all_charts()

    if not charts:
        print("No beatmap charts to check.")
        return 0

    problems = []

    for path, text in charts:
        data, error = parse(text, path)

        if error:
            problems.append(error)
            continue

        if args.strict:
            problems.extend(check_structure(data, path))

    for problem in problems:
        print(f"  {problem}")

    print(f"\nChecked {len(charts)} chart(s)"
          f"{' with structural rules' if args.strict else ''}")

    if problems:
        print(f"FAIL: {len(problems)} problem(s)")
        return 1

    print("OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
