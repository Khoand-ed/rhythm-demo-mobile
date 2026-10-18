"""Writes the golden input trace both replay tests read.

    python server/tests/golden/make_replay_trace.py

A made-up but plausible run of stage_AIW_hard (song_002/hard.json): 60 frames a second from a
second before the first note, every note pressed off its time by a fixed pattern of -150..+150 ms
(so Perfects, Greats and Hits all happen), both lanes for a Twin, and a Hold kept down until
just past its end.

The bytes are the test, not this script: Promuse.Scoring.Tests (on .NET) and the Unity EditMode
tests (on Unity's runtime) replay the same file through RunJudge and must come to the same
pinned numbers. Re-running this script rewrites the file; only do that together with the pins.
"""

import json
import math
import os
import struct

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CHART = os.path.join(ROOT, "Assets", "Beatmaps", "song_002", "hard.json")
OUT = os.path.join(os.path.dirname(__file__), "replay_stage_AIW_hard.trace")

FPS = 60
LANES = 2


def f32(x):
    """The nearest float32, as the game stores song time."""
    return struct.unpack("<f", struct.pack("<f", x))[0]


def notes(path):
    """The chart as ScoringChart / BeatmapImporter read it: sorted, stably, by hit time."""
    raw = json.load(open(path, encoding="utf-8"))["notes"]
    out = []
    for n in raw:
        kind = (n.get("type") or "").strip().lower()
        if kind not in ("tap", "hold", "twin"):
            continue
        lane = 0
        if kind != "twin":
            side = (n.get("lane") or "").strip().lower()
            if side not in ("left", "right"):
                continue
            lane = 0 if side == "left" else 1
        if kind == "hold":
            start, end = f32(n.get("startTime", 0)), f32(n.get("endTime", 0))
            if end <= start:
                continue
            hit, duration = start, f32(end - start)
        else:
            hit = f32(n.get("time", 0))
            if hit == 0 and n.get("startTime", 0):
                hit = f32(n["startTime"])
            duration = 0.0
        if hit < 0:
            continue
        out.append((hit, lane, duration, kind))
    out.sort(key=lambda n: n[0])
    return out


def main():
    chart = notes(CHART)
    first_note = chart[0][0]
    last = max(hit + duration for hit, _, duration, _ in chart) + 0.5
    start = max(0.0, first_note - 1.0)
    frames = int(math.ceil((last - start) * FPS)) + 1

    times = [f32(start + i / FPS) for i in range(frames)]
    pressed = [0] * frames
    held = [0] * frames

    for n, (hit, lane, duration, kind) in enumerate(chart):
        offset = (((n * 53) % 301) - 150) / 1000.0
        lanes = [0, 1] if kind == "twin" else [lane]

        for ln in lanes:
            frame = min(max(int(round((hit + offset - start) * FPS)), 0), frames - 1)
            while frame < frames - 1 and pressed[frame] & (1 << ln):
                frame += 1
            pressed[frame] |= 1 << ln
            held[frame] |= 1 << ln

            if kind == "hold":
                end = hit + duration
                f = frame
                while f < frames:
                    held[f] |= 1 << ln
                    if times[f] >= end + 2.0 / FPS:
                        break
                    f += 1

    with open(OUT, "wb") as out:
        out.write(b"PT" + bytes([1, LANES]) + struct.pack("<i", frames))
        for i in range(frames):
            out.write(struct.pack("<f", times[i]) + bytes([pressed[i], held[i]]))

    print(f"{len(chart)} notes, {frames} frames -> {os.path.relpath(OUT, ROOT)}")


if __name__ == "__main__":
    main()
