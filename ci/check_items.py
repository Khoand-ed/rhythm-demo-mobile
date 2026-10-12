#!/usr/bin/env python3
"""Item catalog check that needs neither Unity nor a licence.

server/data/items.json is what the server names items by on the admin page.
Arknights/Depot/Export Item Catalog writes it from the ItemMeta assets under
Assets/Arknights/Resources/Meta/Item, and this fails when the two disagree:

  1. items.json is valid JSON with an "items" list of {id, name, rarity}.
  2. Every ItemMeta is in it with the same name and rarity, and it names no
     item that has no ItemMeta.
  3. Ids are unique on both sides.

What the server seeds - the shop, headhunting, events, the campaign, sign-in,
a new account's items - is checked against items.json by the API tests
(ItemCatalogTests), where it can be read the way the server reads it.

Exit 0 when clean, 1 otherwise. The fix is to run the export in Unity.
"""

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
ITEM_DIR = ROOT / "Assets" / "Arknights" / "Resources" / "Meta" / "Item"
CATALOG = ROOT / "server" / "data" / "items.json"
FIX = "run Arknights/Depot/Export Item Catalog in Unity"

# A serialized field of the MonoBehaviour: two spaces in, then "key: value".
FIELD = re.compile(r"^  ([A-Za-z_][A-Za-z0-9_]*):(?: (.*))?$")


def scalar(raw, continuation):
    """A YAML scalar as Unity writes one: plain, 'single' or "double" quoted, folded over lines."""
    text = " ".join([raw.strip()] + [c.strip() for c in continuation]).strip()
    if text.startswith("'") and text.endswith("'") and len(text) >= 2:
        return text[1:-1].replace("''", "'")
    if text.startswith('"') and text.endswith('"') and len(text) >= 2:
        return json.loads(text)
    return text


def read_meta(path):
    """{field: raw value} for the top-level fields of one ItemMeta asset."""
    fields, key, raw, more = {}, None, "", []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        match = FIELD.match(line)
        if match:
            if key is not None:
                fields[key] = scalar(raw, more)
            key, raw, more = match.group(1), match.group(2) or "", []
        elif key is not None and line.startswith("    "):
            more.append(line)
        elif key is not None:
            fields[key] = scalar(raw, more)
            key, raw, more = None, "", []
    if key is not None:
        fields[key] = scalar(raw, more)
    return fields


def metas():
    """{id: (name, rarity, file)} from the assets, and the errors found reading them."""
    found, errors = {}, []
    for path in sorted(ITEM_DIR.glob("*.asset")):
        fields = read_meta(path)
        if "id" not in fields or "name" not in fields:
            continue  # not an ItemMeta
        try:
            item_id, rarity = int(fields["id"]), int(fields.get("rarity", "0"))
        except ValueError:
            errors.append(f"{path.name}: id or rarity is not a number")
            continue
        if item_id in found:
            errors.append(f"item id {item_id} is claimed by both {found[item_id][2]} and {path.name}")
            continue
        found[item_id] = (fields["name"], rarity, path.name)
    return found, errors


def catalog():
    """{id: (name, rarity)} from items.json, and the errors found reading it."""
    if not CATALOG.exists():
        return {}, [f"{CATALOG.relative_to(ROOT)} is missing - {FIX}"]
    try:
        document = json.loads(CATALOG.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        return {}, [f"{CATALOG.relative_to(ROOT)} is not valid JSON: {e}"]

    items, errors = {}, []
    entries = document.get("items") if isinstance(document, dict) else None
    if not isinstance(entries, list):
        return {}, [f"{CATALOG.relative_to(ROOT)} has no \"items\" list"]
    for entry in entries:
        if not isinstance(entry, dict) or not isinstance(entry.get("id"), int) or not isinstance(entry.get("name"), str):
            errors.append(f"items.json: {entry!r} is not {{id, name, rarity}}")
            continue
        if entry["id"] in items:
            errors.append(f"items.json lists item {entry['id']} twice")
            continue
        items[entry["id"]] = (entry["name"], entry.get("rarity"))
    return items, errors


def main():
    game, errors = metas()
    served, more = catalog()
    errors += more

    if not errors:
        for item_id, (name, rarity, file) in sorted(game.items()):
            if item_id not in served:
                errors.append(f"{file} (item {item_id}, {name}) is not in items.json - {FIX}")
            elif served[item_id] != (name, rarity):
                errors.append(f"item {item_id}: the game has {name!r} rarity {rarity}, items.json has "
                              f"{served[item_id][0]!r} rarity {served[item_id][1]} - {FIX}")
        for item_id in sorted(set(served) - set(game)):
            errors.append(f"items.json names item {item_id} ({served[item_id][0]}), which has no ItemMeta - {FIX}")

    print(f"Checked {len(game)} ItemMeta asset(s) against {CATALOG.relative_to(ROOT).as_posix()}")
    for error in errors:
        print(f"ERROR: {error}")
    if errors:
        return 1
    print("OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
