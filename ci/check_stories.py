#!/usr/bin/env python3
"""Story script checks that need neither Unity nor a licence.

The stories ship as plain text under Assets/Arknights/Resources/Story/*.story
and are read by Data.Story.StoryScript in the game. These rules are the same
ones, so a broken script fails the pull request instead of being skipped on a
phone:

  1. Every line is "SPEAKER: text" or "> narration", says something, and fits
     in 500 characters - what the admin page can store as a correction.
  2. Directives are @title (once) and @bg <name>|none, and every background a
     script names exists under Story/Backgrounds.
  3. Line ids (#a1f3 - 4 to 8 lower-case letters and digits) are unique within
     a script. With --strict a line without one is an error: it could never be
     corrected from the admin page once shipped.
  4. Every story the seeded event names (server SeedEvents.cs) is in the build.

Exit 0 when clean, 1 otherwise.
"""

import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
STORY_DIR = ROOT / "Assets" / "Arknights" / "Resources" / "Story"
BACKGROUND_DIR = STORY_DIR / "Backgrounds"
SEED = ROOT / "server" / "src" / "Promuse.Persistence" / "SeedEvents.cs"

STORY_ID = re.compile(r"^[a-z0-9_]{1,64}$")
LINE_ID = re.compile(r"^[a-z0-9]{4,8}$")
MAX_TEXT = 500
MAX_SPEAKER = 32
IMAGE_EXTENSIONS = (".png", ".jpg", ".jpeg")

# new("stage_AIW", 30, "RD-1", "before_id", "after_id") in SeedEvents.cs
SEED_STAGE = re.compile(
    r'new\(\s*"[^"]+"\s*,\s*\d+\s*,\s*(?:"[^"]*"|null)\s*,\s*(?:"([a-z0-9_]+)"|null)\s*(?:,\s*(?:"([a-z0-9_]+)"|null))?\s*\)'
)


def split_line_id(line):
    """(text, id, bad_id) - the same rule as StoryScript.SplitLineId."""
    hash_at = line.rfind("#")
    if hash_at <= 0 or not line[hash_at - 1].isspace():
        return line, None, None
    token = line[hash_at + 1:].strip()
    if not token or " " in token:
        return line, None, None
    if not LINE_ID.match(token):
        return line, None, token
    return line[:hash_at].rstrip(), token, None


def check_script(path, strict, backgrounds):
    errors, warnings = [], []
    story_id = path.stem
    if not STORY_ID.match(story_id):
        errors.append(f"'{story_id}' is not a story id - lower-case letters, digits and underscores")

    ids = set()
    title_seen = False
    lines = 0
    text = path.read_text(encoding="utf-8-sig")

    for number, raw in enumerate(text.replace("\r\n", "\n").replace("\r", "\n").split("\n"), start=1):
        line = raw.strip()
        if not line or line.startswith("//"):
            continue

        if line.startswith("@"):
            name, _, value = line[1:].partition(" ")
            name, value = name.lower(), value.strip()
            if name == "title":
                if title_seen:
                    errors.append(f"{number}: a script has one @title")
                elif not value:
                    errors.append(f"{number}: @title needs the text to show")
                title_seen = True
            elif name == "bg":
                if value != "none":
                    if not STORY_ID.match(value):
                        errors.append(f"{number}: @bg takes an image name or 'none'")
                    elif value not in backgrounds:
                        errors.append(f"{number}: no background '{value}' under {BACKGROUND_DIR.relative_to(ROOT).as_posix()}")
            else:
                errors.append(f"{number}: unknown directive '@{name}' - only @title and @bg exist")
            continue

        line, line_id, bad = split_line_id(line)
        if bad is not None:
            errors.append(f"{number}: '#{bad}' is not a line id - 4 to 8 lower-case letters and digits")
            continue

        if line.startswith(">"):
            said = line[1:].strip()
        else:
            colon = line.find(":")
            if colon <= 0:
                errors.append(f"{number}: not a line - write 'SPEAKER: text' or '> narration'")
                continue
            if len(line[:colon].strip()) > MAX_SPEAKER:
                errors.append(f"{number}: the speaker's name is longer than {MAX_SPEAKER} characters")
                continue
            said = line[colon + 1:].strip()

        if not said:
            errors.append(f"{number}: the line says nothing")
            continue
        if len(said) > MAX_TEXT:
            errors.append(f"{number}: longer than {MAX_TEXT} characters")
            continue

        if line_id is None:
            (errors if strict else warnings).append(
                f"{number}: no line id - run Arknights/Story/Assign Missing Line Ids")
        elif line_id in ids:
            errors.append(f"{number}: the line id #{line_id} is used twice")
            continue
        else:
            ids.add(line_id)

        lines += 1

    if lines == 0 and not errors:
        errors.append("the script has no lines")

    return errors, warnings


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--strict", action="store_true", help="a line without an id is an error")
    args = parser.parse_args()

    backgrounds = set()
    if BACKGROUND_DIR.is_dir():
        backgrounds = {p.stem for p in BACKGROUND_DIR.iterdir() if p.suffix.lower() in IMAGE_EXTENSIONS}

    scripts = sorted(STORY_DIR.glob("**/*.story")) if STORY_DIR.is_dir() else []
    failed = False

    for path in scripts:
        errors, warnings = check_script(path, args.strict, backgrounds)
        rel = path.relative_to(ROOT).as_posix()
        for e in errors:
            print(f"ERROR   {rel}:{e}")
        for w in warnings:
            print(f"warning {rel}:{w}")
        failed |= bool(errors)

    shipped = {p.stem for p in scripts}
    if SEED.is_file():
        for match in SEED_STAGE.finditer(SEED.read_text(encoding="utf-8-sig")):
            for story in match.groups():
                if story and story not in shipped:
                    print(f"ERROR   {SEED.relative_to(ROOT).as_posix()}: the seeded event names story "
                          f"'{story}', which is not under {STORY_DIR.relative_to(ROOT).as_posix()}")
                    failed = True

    print(f"{len(scripts)} story script(s) checked" + (" - FAILED" if failed else ", all good"))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
