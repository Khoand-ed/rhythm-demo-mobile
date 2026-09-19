#!/usr/bin/env python3
"""Unity .meta integrity check.

A missing or orphaned .meta file is the classic way a Unity repository breaks:
Unity regenerates the missing one with a fresh GUID on the next import, and every
prefab, scene and ScriptableObject that referenced the old GUID silently loses its
link. The symptom shows up days later as "Missing (Mono Script)" in an Inspector,
long after the commit that caused it.

Four rules, all checked against what git tracks rather than what is on disk, so a
file that exists locally but was never committed cannot mask a problem:

  1. every tracked file under Assets/ has a tracked <file>.meta
  2. every directory under Assets/ has a tracked <dir>.meta
  3. every tracked <name>.meta has a tracked <name> file or <name>/ directory
  4. no two tracked .meta files share a guid

Scoped to Assets/ deliberately. Packages/manifest.json correctly has no .meta, so
widening the scope produces immediate false positives.

Exit 0 when clean, 1 otherwise.
"""

import os
import re
import subprocess
import sys

ASSETS = "Assets/"
GUID_RE = re.compile(rb"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)


def tracked_paths():
    """Every path git tracks, as forward-slash strings.

    core.quotepath=off keeps git from escaping non-ASCII bytes into C-style
    octal ("b\\303\\240i"), which this repository would otherwise hit: it tracks
    Assets/Settings/Build Profiles/Android(tm).asset and eighteen XLua documents
    with Chinese filenames. surrogateescape then carries any byte sequence that
    is not valid UTF-8 through without raising.
    """
    out = subprocess.run(
        ["git", "-c", "core.quotepath=off", "ls-files", "-z"],
        check=True, stdout=subprocess.PIPE,
    ).stdout

    return [p.decode("utf-8", "surrogateescape") for p in out.split(b"\0") if p]


def unity_ignores(path):
    """True when Unity would not import this path, so it needs no .meta.

    Mirrors Unity's own rules: a path segment starting with a dot or ending with
    a tilde is hidden, "cvs" is legacy version-control cruft, and .tmp files are
    transient.
    """
    for segment in path.split("/"):
        if segment.startswith(".") or segment.endswith("~") or segment.lower() == "cvs":
            return True

    return path.lower().endswith(".tmp")


def main():
    paths = [p for p in tracked_paths() if p.startswith(ASSETS) and not unity_ignores(p)]

    assets = set()
    metas = set()

    for path in paths:
        if path.endswith(".meta"):
            metas.add(path[: -len(".meta")])
        else:
            assets.add(path)

    # Git stores no empty directories, so every directory is inferred from the
    # files inside it. Assets/ itself is the project root and needs no .meta.
    directories = set()
    for path in assets:
        parts = path.split("/")
        for i in range(1, len(parts)):
            directory = "/".join(parts[:i])
            if directory != "Assets":
                directories.add(directory)

    missing_file_meta = sorted(assets - metas)
    missing_dir_meta = sorted(directories - metas)
    orphan_meta = sorted(metas - assets - directories)

    # Duplicate GUIDs corrupt asset references just as badly as a missing .meta,
    # and cost nothing to detect while the file list is already in hand.
    by_guid = {}
    for stem in sorted(metas):
        meta = stem + ".meta"
        try:
            with open(meta, "rb") as handle:
                found = GUID_RE.search(handle.read())
        except OSError:
            continue

        if found:
            by_guid.setdefault(found.group(1).decode("ascii").lower(), []).append(meta)

    duplicate_guids = {g: files for g, files in by_guid.items() if len(files) > 1}

    def report(title, items, render=ascii):
        if not items:
            return 0

        print(f"\n{title} ({len(items)}):")
        for item in items[:40]:
            print(f"  {render(item)}")
        if len(items) > 40:
            print(f"  ... and {len(items) - 40} more")
        return len(items)

    problems = 0
    problems += report("Assets with no tracked .meta", missing_file_meta)
    problems += report("Directories with no tracked .meta", missing_dir_meta)
    problems += report("Orphaned .meta with no asset", orphan_meta)

    if duplicate_guids:
        print(f"\nDuplicate GUIDs ({len(duplicate_guids)}):")
        for guid, files in sorted(duplicate_guids.items()):
            print(f"  {guid}")
            for meta in files:
                print(f"    {ascii(meta)}")
        problems += len(duplicate_guids)

    print(f"\nScanned {len(assets)} assets and {len(metas)} .meta files under {ASSETS}")

    if problems:
        print(f"FAIL: {problems} problem(s)")
        return 1

    print("OK: every asset has a .meta, no orphans, no duplicate GUIDs")
    return 0


if __name__ == "__main__":
    sys.exit(main())
