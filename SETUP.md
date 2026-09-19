# Setup

Six error messages in this codebase point here, so this is the document they mean.

Promuse is a Unity mobile rhythm game with an Arknights-style front-end. Two halves
live in one project: the front-end (login, home, shop, depot, song select) under
`Assets/Arknights/`, and the rhythm core (conductor, note spawning, judging) under
`Assets/Source/`.

---

## Before you clone

**Install Git LFS first.** This is the one step that cannot be fixed afterwards
without a second clone.

```bash
git lfs install
git clone https://github.com/Khoand-ed/rhythm-demo-mobile.git
```

Every `.png`, `.wav`, `.mp3`, `.psd` and `.fbx` in the repository is stored in LFS
(173 files, about 31 MB). Clone without LFS and they arrive as ~130-byte text
pointers. Unity then fails to import them and reports errors that have nothing to
do with the real cause.

Already cloned without it?

```bash
git lfs install
git lfs pull
```

---

## Requirements

| | |
|---|---|
| Unity | **6000.5.1f1** exactly — opening with another version rewrites serialization and produces noise in every `.unity` and `.prefab` |
| Modules | Android Build Support (+ OpenJDK, Android SDK & NDK) |
| Python 3 | Optional. Only used by the checks in `ci/`; the game does not need it |

The project uses URP 17.5 with the 2D Renderer, the new Input System, and vendored
XLua, Spine and DOTween under `Assets/Plugins/`.

---

## First run

1. Open the project in Unity 6000.5.1f1. First import takes several minutes.
2. **File → Build Settings → Android → Switch Platform.** This matters beyond
   building: `ABManager` compiles per platform, so on Windows it looks for an
   AssetBundle named `Win` inside `StreamingAssets/`, while on Android it looks
   for `Android` in `persistentDataPath`. Android is the project's target.
3. Open `Assets/Arknights/Scenes/StartMenu.unity` and press Play.
4. Log in with **`Saukiya` / `123456`** (or `Test` / `123456`). These are seeded
   local accounts in `Assets/Arknights/Resources/Data/User/`, not real credentials.

### Four red errors on boot are expected

```
[Asset] Missing asset 'm_sys_title_intro' from bundle 'Audio/Music/Login'
[Asset] Missing asset 'm_sys_void_intro'  from bundle 'Audio/Music/Home'
...
```

The front-end's background music lives in `Assets/Arknights/Audio/`, which is
**gitignored** and therefore not in the repository. The game runs fine without it —
only the menus are silent. Gameplay music **is** in the repository under
`Assets/Beatmaps/`, so songs play normally.

To add menu music: drop your own audio anywhere in the project, run
`Arknights/Audio/Set Login Music...` (and the Home / Battle / Shop variants), then
`Arknights/AssetBundles/Build For Active Target`.

### You do not need to build AssetBundles to run the game

`Asset.Load` falls back to `Resources`, and every UI prefab is committed under
`Assets/Arknights/Resources/Prefab/UI/`. Bundles are only needed for the audio
above and for device builds.

---

## The three editor menus

| Menu | What lives there |
|---|---|
| **`Arknights/`** | Content pipeline — AssetBundles, placeholder art, audio import, and the Home / Depot screen builders |
| **`Beatmap/`** | Charting — `Beatmap Studio...` and `Timeline Editor...` author charts; `Import All Beatmaps` turns the JSON under `Assets/Beatmaps/` into `SongChart` assets |
| **`Tools/Rhythm/`** | Gameplay scene setup — note system, HUD, hit feedback, skip button, song-select wiring |

Two conventions these tools all follow, worth knowing before you write another one:

- **Idempotent find-or-create.** Re-running a setup tool never moves or restyles
  something you have already adjusted by hand. Each one is paired with an explicit
  `Reset ... To Default` for when you do want that.
- **Guarded and prefixed.** Every tool refuses to run in Play Mode, and logs with a
  `[ClassName]` prefix.

---

## Project layout

```
Assets/
├── Arknights/      front-end: UI screens, data, managers, Lua, its own asmdef
├── Source/         rhythm core (default assembly, no namespace)
│   └── Script/Core/  pure logic in the Rhythm.Core assembly - the testable part
├── Bridge/         the seam between the two halves (see below)
├── Editor/         all 30 menu tools + the Beatmap DSP toolchain
├── Beatmaps/       chart JSON, SongChart assets, gameplay audio
└── Tests/EditMode/ EditMode tests for Rhythm.Core

ci/                 repo integrity checks, run locally and by CI
docs/engine-reference/unity/   version-pinned Unity API notes for AI agents
.claude/            agent definitions, permissions, one pre-commit hook
```

### Why `Assets/Bridge/` exists

An assembly definition can never reference the default assembly (`Assembly-CSharp`)
— the dependency only runs the other way. `Assets/Arknights/` has its own asmdef, so
it cannot see `SongChart`. Code that needs **both** the Arknights `UIBase` and the
rhythm core's types has to live in the default assembly, and `Assets/Bridge/` is
where it goes. Do not "tidy" those files into `Assets/Arknights/`; they will stop
compiling.

`Assets/Source/Script/Core/` is the same constraint from the other side: those five
files were moved into their own assembly so tests can reference them.

---

## Tests

Window → General → Test Runner → EditMode → Run All.

The tests cover pure logic only — judging windows, miss damage, fever gain, chart
sorting. Nothing there touches a scene or the Editor, so they finish in well under
a second.

---

## Checks

The same checks CI runs, runnable locally:

```bash
python ci/check_meta.py              # every asset has a .meta, no orphans, no duplicate GUIDs
python ci/check_beatmaps.py --all --strict
python ci/check_repo_hygiene.py      # stray build output, LFS integrity, conflict markers
```

A missing or duplicated `.meta` is the classic way a Unity repository breaks: Unity
regenerates it with a fresh GUID and every prefab that referenced the old one
silently loses its link, surfacing days later as "Missing (Mono Script)".

`.github/workflows/project-checks.yml` runs all three on every pull request. It needs
no Unity licence and no secrets.

---

## Things that will bite you

| | |
|---|---|
| Renaming the product or company in Player Settings | `persistentDataPath` is derived from them, so the deployed AssetBundles are orphaned and all audio disappears. Run `Arknights/AssetBundles/Deploy To Persistent Data Path` afterwards |
| Editing Lua under `Assets/Arknights/LuaScripts/` | Takes effect immediately in the Editor (it reads from disk first), but a device build reads the AssetBundle — rebuild bundles before testing on a phone |
| Setting `excludePlatforms` on `Arknights.asmdef` | Drops the entire front-end from the Android build. The game will not run on a device |
| Moving a `.cs` without its `.meta` | Breaks every reference to it. Move both, or move it inside Unity |
