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

### Live events

The same page's **Events** tab creates and edits events. An event is built from optional
modules - check-in days, missions with point milestones, an event currency with its own shop,
event stages, and a headhunting banner to link to - so a check-in-only event and a full
festival are the same thing with different parts filled in. The database starts with one,
`resonant_dawn`, using every module.

An event's stages are its own: played from the event screen, in the order listed - each opens
once the one before it is cleared there - and each can carry a story before it (played the first
time it is entered) and after it (played after its first clear). Only a run started from the
event screen clears a stage and pays the currency; the same song played from the song list does
not.

Home's **Operation** tile opens the mode select screen: **EVENT** leads to the events, **STORY**
to the main campaign. The little news screen on Home goes straight to
the event screen. The screens are built by `Arknights/Events/Build Event UI`, which also creates
the Event Token item (id 20) and points the Operation tile at the mode select. An event's
currency and rewards can be any item id, but only items with an `ItemMeta` under
`Resources/Meta/Item` show a name and icon.

### The main campaign

STORY opens the campaign's chapters: stages played in order, a chapter opening when the last stage
of the one before is cleared. A stage is a chart plus what it pays - Orundum on its first clear
only, and drops rolled by the server on every clear (shown on the stage as Common / Uncommon /
Rare). Each stage can have a story before and after it. SONG LIST, top right, still opens the old
song map for practice and the leaderboards.

The admin page's **Campaign** tab edits it all: chapters, stages, the chart each plays, Orundum,
drops (chance in %), stories. Sanity is the chart's own price. The database starts with two
chapters built from the charts the game ships; adding a song is a row there, not a code change.
The screen is built by `Arknights/Campaign/Build Campaign UI`.

Every cleared ranked run - campaign, event or song list - also pays player EXP: Sanity × the
config's **EXP per Sanity** (12 when empty). Each level reached pays level / 5 Originite Prime and
refills Sanity; the results screen shows all of it.

### Stories

The dialogue around a stage is a plain text file, `Assets/Arknights/Resources/Story/<id>.story`,
written in any text editor and shipped inside the game:

```
// a comment
@title Resonant Dawn - RD-1        the card shown before the first line
@bg rhodes_deck                    background from Story/Backgrounds; "none" for none

AMIYA: Doctor, the signal is back.   #a1f3
Radio: ...anyone receiving?          #b2c4
> The wind carries a faint melody.   #c9e1
```

A speaker that is an operator id shows that operator's name and portrait; any other name is shown
as written; `>` is narration. The `#id` after a line is how it is corrected later, so **never change
an id once the story has shipped** - `Arknights/Story/Assign Missing Line Ids` writes one on every
line that has none. Unity reports a broken script in the Console the moment it is saved, and
`Arknights/Story/Story Preview...` lists every script with its problems and plays one in Play Mode.
`Arknights/Story/Build Story UI` builds the dialogue screen and three placeholder backgrounds;
replace a PNG in `Story/Backgrounds` with real art of the same name.

A typo found after release is fixed on the admin page's **Stories** tab, without a build: open the
script file there, correct the line, save. The game picks it up the next time a story plays. A
correction only applies while the game's own line still reads what it was written against, so
once a build carries the fix it stops applying by itself.

Point an event stage at a story by its id in the Events tab (**Story before** / **Story after**).
Whether a player has seen a story is remembered on the device; the stage's STORY button replays
it.

### Mail, notices and the monthly sign-in

Home's three top-left icons open them, and the admin page has a tab for each:

- **Mail** - send to one player by username, or to every player who exists at that moment, with
  up to ten items attached and an expiry. Opening a mail collects its items; a box shows the
  newest hundred. A sent mail can be retracted from every box.
- **Notices** - the announcement board's Event / System / News tabs. A notice is a banner and then
  its content in blocks: paragraphs, with pictures between them, added and reordered on the page.
  A paragraph is TextMeshPro rich text, and its toolbar wraps the selected text in bold, italic,
  underline, a size or a colour; the preview beside it draws the notice the way the game does.
  Banners and pictures are PNG or JPEG uploads, kept in `server/src/Promuse.Api/App_Data/banners/`
  (gitignored; `Notices:BannerDirectory` moves it). The server also writes the paragraphs into the
  notice's plain body, so a build from before blocks still shows the text.
- **Sign-in** - one square per server day, in order, starting over each month. `default` holds 31
  squares; a month (`2026-11`) can have its own table, and falls back to the default without one.

Every item field on the admin page - mail attachments, sign-in squares, event rewards and shop,
campaign drops - is a picker reading `1 · Orundum`. The names come from `server/data/items.json`,
which `Arknights/Depot/Export Item Catalog` writes from the game's ItemMetas: run it after adding or
renaming one. An id the list does not know still shows, as `#99 (unknown)`, so nothing is lost.

The first time Home shows each server day, the calendar opens (and signs today's square) if it is
waiting, then the notice board if anything is showing. The screens are built by
`Arknights/Home/Build Mail, Notice and Calendar UI`, which also wires the three icons and their red dots.

### Profile cards

The **PROFILE** button beside Home's news panel opens the player's own card, after the reference's
"My Info": the operator standing on it, the day the account was made, the furthest campaign stage
cleared, how many operators they own, the level and `name#tag`, a line they write themselves, and a
rhythm record - runs finished, charts cleared, full combos and best ranked score. Pressing a row on
a song's leaderboard opens that player's card, read-only.

- **The tag** is four digits after the name, unique per name, drawn when the player is created.
  Names repeat - every guest is `Doctor` and a number - a name and its tag never do.
- **The operator on the card** is the desktop character once a screen sets it. Until then it is the
  squad leader, and failing that the operator raised furthest.
- **The line** is edited on the card itself, 60 characters at most. The server keeps it as one line
  with nothing invisible in it, and the field shows what was kept.

Built by `Arknights/Home/Build Profile UI`, which also places the button inside Home's `LeftPanel`
so it leans with the news panel. `Reset Profile UI To Default` rebuilds the screen and puts the
button back where it started.

### Operator levels

Pressing the level panel on an operator's page (LV, EXP and the cap) opens the level-up screen. Drag
the dial down to choose a target level and the battle records for it are picked - as few as reach it,
with the least EXP left over - or press a record to add one by hand. Level Up spends the records and
the LMD they cost.

- **Records:** Basic (200 EXP), Frontline (400), Tactical (1000) and Strategic (2000), dropped by the
  campaign from 1-3 onwards. The admin can also mail them.
- **LMD:** 3 in 10 of the EXP that goes in, at elite 0.
- **Cap:** set by rarity and elite phase - 40 for a 3-star at elite 0, 45 for a 4-star, 50 for a
  5-star. Promotion, which raises it, is not built yet.
- **Stats:** each level past the first adds 2 max HP and +0.002 to the score and fever modifiers
  (per operator, in `CharMeta` and `server/data/ruleset.json`). A run is played, and reviewed, at
  the level the server recorded when it opened, so a level-up mid-song changes nothing about it.

The curve, the caps and what a record is worth live in one place, `OperatorLevels` in the shared
contracts, so the screen previews exactly what the server does. Built by
`Arknights/Character/Build Level Up UI`; `Reset Level Up UI To Default` rebuilds it.

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

Each operator's splash is installed **whole** as `splash.png`, never cut, together with where the
figure and the face are on it. Every screen that shows an operator standing - the Roster card, the
operator detail, the operator picker, headhunting and dialogue - draws it through a `SplashFrame`,
which scales the whole picture by the figure instead of squeezing it into the frame: the Roster card
is filled and framed on the face, the other screens show the whole figure.
`Arknights/Art/Fit Operator Splash Frames` sets those frames up on the prefabs (it has already been
run on the ones in the repository); `Reset Operator Splash Frames To Default` puts its values back.
An install made before splashes were kept whole is upgraded by running the installer again: the
old cut `portrait.png` is replaced and removed.

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
| **`Arknights/`** | Content pipeline — AssetBundles, placeholder art, audio import, local character art, the Home / Depot / event / campaign / mail, notice and calendar / profile / level-up screen builders, and the story tools |
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
python ci/check_stories.py --strict  # every story script parses, has line ids, names real backgrounds
python ci/check_items.py             # server/data/items.json matches the ItemMetas
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
