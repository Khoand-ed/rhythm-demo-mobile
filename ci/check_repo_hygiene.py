#!/usr/bin/env python3
"""Repository hygiene checks that need neither Unity nor a licence.

Three failure modes, each cheap to detect and expensive to discover late:

  1. Build output committed by accident. .gitignore never untracks anything that
     was already committed, so a file added before the ignore rule stays tracked
     forever and quietly grows the repository.

  2. A binary committed raw instead of through Git LFS. This is what happens when
     someone clones or commits without `git lfs install`, and the damage is
     permanent history bloat.

  3. A merge-conflict marker left inside a tracked file. In a .unity or .prefab
     this corrupts the asset, and Unity reports it only as a vague parse error.

Exit 0 when clean, 1 otherwise.
"""

import fnmatch
import re
import subprocess
import sys

# Patterns .gitignore already covers. Anything matching these that is still
# tracked predates its ignore rule.
SHOULD_NOT_BE_TRACKED = [
    "Library/*", "Temp/*", "Obj/*", "obj/*", "Build/*", "Builds/*", "Logs/*",
    "UserSettings/*", "MemoryCaptures/*", "Recordings/*",
    "*.csproj", "*.unityproj", "*.sln", "*.slnx", "*.suo", "*.user", "*.userprefs",
    "*.pidb", "*.booproj", "*.svd", "*.pdb", "*.mdb", "*.opendb", "*.VC.db",
    "*.apk", "*.aab", "*.unitypackage", "*.app",
    "server/*/[Bb]in/*", "server/*/[Oo]bj/*",
]

# The backend's project files are the exception to the *.csproj / *.sln rules
# above. Those rules exist because Unity regenerates its own and they should
# never be tracked - but the ones under server/ are hand-written source and
# have to be. Without this the first backend commit fails the check, because
# the scan below also matches patterns against a path's basename.
#
# fnmatch's * crosses / (unlike a shell glob), so one pattern covers any depth
# underneath server/. Actual build output there is still caught by the two
# server/ patterns added above.
STRAY_EXEMPT = [
    "server/*.csproj", "server/*.sln", "server/*.slnx",
    "server/*.props", "server/*.targets",
]

CONFLICT_RE = re.compile(rb"^(<<<<<<< |>>>>>>> |=======$)", re.MULTILINE)

# The conflict scan must not read its own pattern literals back as a finding.
SCAN_SKIP_PREFIXES = ("ci/",)


def tracked_paths():
    out = subprocess.run(
        ["git", "-c", "core.quotepath=off", "ls-files", "-z"],
        check=True, stdout=subprocess.PIPE,
    ).stdout

    return [p.decode("utf-8", "surrogateescape") for p in out.split(b"\0") if p]


def lfs_patterns():
    """Glob patterns that .gitattributes routes through the `lfs` macro."""
    patterns = []

    try:
        with open(".gitattributes", "r", encoding="utf-8", errors="replace") as handle:
            for line in handle:
                line = line.split("#", 1)[0].strip()
                if not line or line.startswith("[attr]"):
                    continue

                fields = line.split()
                if len(fields) >= 2 and "lfs" in fields[1:]:
                    patterns.append(fields[0])
    except OSError:
        pass

    return patterns


def lfs_tracked():
    """Paths git-lfs actually stores. Empty set if git-lfs is unavailable."""
    result = subprocess.run(["git", "lfs", "ls-files", "-n"],
                            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)

    if result.returncode != 0:
        return None

    return {line.strip().replace("\\", "/")
            for line in result.stdout.decode("utf-8", "surrogateescape").splitlines()
            if line.strip()}


def main():
    paths = tracked_paths()
    problems = 0

    # 1. Tracked build output
    stray = sorted(
        path for path in paths
        if any(fnmatch.fnmatch(path, pattern) or fnmatch.fnmatch(path.split("/")[-1], pattern)
               for pattern in SHOULD_NOT_BE_TRACKED)
        and not any(fnmatch.fnmatch(path, pattern) for pattern in STRAY_EXEMPT)
    )

    if stray:
        print(f"Tracked files that .gitignore says should not be ({len(stray)}):")
        for path in stray:
            print(f"  {ascii(path)}")
        print("  Fix with: git rm --cached <path>")
        problems += len(stray)

    # 2. LFS pointer integrity
    patterns = lfs_patterns()
    in_lfs = lfs_tracked()

    if in_lfs is None:
        print("\nSKIPPED LFS check: git-lfs is not available here.")
    elif patterns:
        should_be_lfs = {
            path for path in paths
            if any(fnmatch.fnmatch(path.split("/")[-1], pattern) for pattern in patterns)
        }

        raw = sorted(should_be_lfs - in_lfs)

        if raw:
            print(f"\nBinaries committed raw instead of through LFS ({len(raw)}):")
            for path in raw[:40]:
                print(f"  {ascii(path)}")
            if len(raw) > 40:
                print(f"  ... and {len(raw) - 40} more")
            print("  Usually means a clone or commit happened without `git lfs install`.")
            problems += len(raw)
        else:
            print(f"\nLFS: {len(should_be_lfs)} file(s) match an lfs pattern, all stored in LFS.")

    # 3. Conflict markers
    conflicted = []
    lfs_names = in_lfs or set()

    for path in paths:
        if path.startswith(SCAN_SKIP_PREFIXES) or path in lfs_names:
            continue

        try:
            with open(path, "rb") as handle:
                blob = handle.read()
        except OSError:
            continue

        if b"\0" in blob[:8000]:
            continue  # binary

        if CONFLICT_RE.search(blob):
            # "=======" alone is a legitimate markdown/ini divider, so only report
            # a file that also carries one of the unambiguous markers.
            if re.search(rb"^(<<<<<<< |>>>>>>> )", blob, re.MULTILINE):
                conflicted.append(path)

    if conflicted:
        print(f"\nMerge-conflict markers in tracked files ({len(conflicted)}):")
        for path in conflicted:
            print(f"  {ascii(path)}")
        problems += len(conflicted)

    print(f"\nScanned {len(paths)} tracked path(s)")

    if problems:
        print(f"FAIL: {problems} problem(s)")
        return 1

    print("OK: no stray build output, LFS intact, no conflict markers")
    return 0


if __name__ == "__main__":
    sys.exit(main())
