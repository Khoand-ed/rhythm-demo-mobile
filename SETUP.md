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
3. **Start the backend.** The game no longer has an offline login - accounts live
   on the server, and without it the login screen will say it cannot connect.

   ```bash
   docker compose -f server/docker-compose.yml up -d
   ```

   Create or update the schema - on first run, and again after pulling any change that
   adds a migration under `server/src/Promuse.Persistence/Migrations/`. The API does not
   migrate itself on startup, deliberately: with more than one instance running, every
   one of them would race to alter the same tables.

   ```bash
   (cd server && dotnet tool restore && dotnet ef database update --project src/Promuse.Persistence --startup-project src/Promuse.Api)
   ```

   ```bash
   dotnet run --project server/src/Promuse.Api --urls http://127.0.0.1:5199
   ```

   On Windows there may be two `dotnet.exe`: the one under `Program Files` is
   runtime-only, so if you get `No .NET SDKs were found`, put
   `%LOCALAPPDATA%\Microsoft\dotnet` first on `PATH`.

4. Open `Assets/Arknights/Scenes/StartMenu.unity` and press Play.
5. **Register an account** on the login screen. Username 3-24 characters, letters,
   digits and underscore; password 8-128.

   The seeded `Saukiya` / `Test` assets under
   `Assets/Arknights/Resources/Data/User/` are no longer used for anything -
   nothing loads them, and the plaintext password they carry authenticates
   nothing.

   `127.0.0.1` is the development machine. A build running on a phone needs the
   machine's LAN address instead - see `PromuseConfig` in
   `Assets/Promuse.Net/PromuseApi.cs`.

### Remote config and the admin page

Maintenance, the minimum client version, the feature flags (headhunting, store, ranked
play, leaderboards) and the Home announcement are edited live at
`http://127.0.0.1:5199/admin/`, without a build or a restart. The page signs in with an
ordinary account that has been made administrator - and there is deliberately no endpoint
that can do that, so the first one is granted by hand:

```bash
docker exec promuse-postgres psql -U promuse -d promuse -c "UPDATE accounts SET is_admin = TRUE WHERE username = 'YOUR_USERNAME';"
```

Every save is a new version with your note beside it, and any old version can be put back
from the history. A save is live on the next request through the same API instance, and
within five seconds on any other.

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

### The operator art is local-only too

The operators' animated art and portraits are reference sprites from another game, used while this
one is built - AMIYA as herself, and three stand-ins for NOVA, ECHO and PULSE. The repository is public, so they are **gitignored** under
`Assets/Arknights/Resources/LocalArt/` and never committed. Without them the game runs exactly as
before: every operator shows its tracked placeholder, and the rhythm stage and the Home screen
simply have nobody standing on them.

To put the art on your machine run `Arknights/Art/Install Placeholder Operator Art`. It remembers
where the source sprites are (the folder holding `AMIYA/` and `ART/`);
`Choose Placeholder Art Source Folder...` changes that, and `Remove Local Character Art` takes it
all off again. Each operator's motion table - which animation answers a tap, a hold, a miss - is
installed beside its rig as `<ID>_Motion.asset` and can be tuned in the Inspector; re-running the
installer keeps it. The installer needs the default shader in
`Assets/Editor/SpineSettings.asset` to be `Spine/Skeleton`, which is what the repository carries.

When you have art of your own, put it in the operator's `CharMeta` (tracked) and remove the local
folder. No code changes: `CharMeta` asks the local registry first and falls back to its own fields.

### You do not need to build AssetBundles to run the game

`Asset.Load` falls back to `Resources`, and every UI prefab is committed under
`Assets/Arknights/Resources/Prefab/UI/`. Bundles are only needed for the audio
above and for device builds.

---

## The three editor menus

| Menu | What lives there |
|---|---|
| **`Arknights/`** | Content pipeline — AssetBundles, placeholder art, audio import, local character art, and the Home / Depot screen builders |
| **`Beatmap/`** | Charting — `Beatmap Studio...` and `Timeline Editor...` author charts; `Import All Beatmaps` turns the JSON under `Assets/Beatmaps/` into `SongChart` assets |
| **`Tools/Rhythm/`** | Gameplay scene setup — note system, HUD, hit feedback, character stage, skip button, song-select wiring, loading screen |

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
