# Promuse — mobile rhythm game

Unity **6000.5.1f1**, URP 17.5 (2D Renderer), Android, landscape. Two halves in one
project: an Arknights-style front-end (`Assets/Arknights/`) and the rhythm core
(`Assets/Source/`). Setup, first run and the full tool list live in `SETUP.md`.

## Before asserting a Unity API exists

Check `docs/engine-reference/unity/VERSION.md` and the relevant `modules/*.md`.
Training data for this project's engine version is thin — 6000.5 postdates it. If an
API is uncertain, say so rather than asserting, and prefer a web search on the
official docs over recall.

## Assemblies — the rule that breaks things

**An asmdef can never reference the default assembly.** The dependency only runs the
other way: predefined assemblies auto-reference every asmdef, never the reverse.

| Assembly | Holds | Can see |
|---|---|---|
| `Arknights` (asmdef) | `Assets/Arknights/` — UI, data, managers, Lua | DOTween, Spine, XLua, TMP, uGUI |
| `Rhythm.Core` (asmdef) | `Assets/Source/Script/Core/` — judging, health, fever, chart data | UnityEngine only |
| `Promuse.Net` (asmdef) | `Assets/Promuse.Net/` — wire contracts, HTTP transport, token store | UnityEngine, Newtonsoft |
| `Promuse.Manufacture` (asmdef) | `Assets/Manufacture/` — Manufacture's data, its screen and the room's operators | `Arknights`, `Promuse.Net`, DOTween, Spine, TMP, uGUI |
| `Assembly-CSharp` (default) | `Assets/Source/Script/`, `Assets/Bridge/` | **everything above** |
| `Assembly-CSharp-Editor` (default) | `Assets/Editor/` | everything above |

Consequences that come up constantly:

- **`Assets/Bridge/` exists because of this rule.** Code needing both the Arknights
  `UIBase` and the rhythm core's `SongChart` can only live in the default assembly.
  Do not move those files into `Assets/Arknights/` — they will stop compiling.
- **New code that spans both halves goes in `Assets/Bridge/`.**
- **`Assets/Promuse.Net/` is the only thing that talks to the backend.** Its
  `Contracts/` folder is linked *out* to `server/src/Promuse.Contracts` the way
  `Rhythm.Core` is linked out to `Promuse.Scoring` - one definition of the wire
  format, compiled by Unity and by .NET. Those files must stay **C# 9**: Unity 6
  compiles at that level, so no `required`, no file-scoped namespaces, no
  collection expressions. `UnityCompat/IsExternalInit.cs` is what makes `record`
  compile at all and is deliberately outside `Contracts/`, because net10.0
  already has that type.
- `Assets/Source/Script/Core/` was split out so tests can reference it. Keep it
  dependency-free: `UnityEngine` and nothing from the default assembly.
- **`Promuse.Manufacture` sees `Arknights`, never the other way round.** Nothing in
  `Arknights` may name a Manufacture type: Home's tile opens the screen through
  `OpenUIButton`, by its name, and a badge for it would be a component of
  `Promuse.Manufacture` - each placed on the prefab by an editor tool
  (`Arknights/Manufacture/Build Manufacture UI`). Its screen prefab still goes in
  `Assets/Arknights/Resources/Prefab/UI/`, where `UIManager` loads screens and
  `BackKeyTests` checks each has a way back.
- Gameplay scripts under `Assets/Source/Script/` carry **no namespace**. Match that.

## Editor tools

Three menu roots, and a new tool belongs under the one that matches its job:
`Arknights/` (content pipeline), `Beatmap/` (charting), `Tools/Rhythm/` (scene setup).

Every tool here follows the same four conventions — follow them in new ones:

1. **Idempotent find-or-create.** Re-running never moves or restyles something already
   adjusted by hand.
2. **Paired with an explicit `Reset ... To Default`** for when the user does want that.
3. **Guarded**: `if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }`
4. **Logged with a `[ClassName]` prefix**, ending in a summary of what was wired.

## UI

`ItemInfoUI` and `ItemIconComponent` declare `UnityEngine.UI.Text` fields, so prefabs
bound to them need **legacy Text, not TextMeshProUGUI** — TMP will not bind. The rhythm
HUD uses TMP. Do not mix the two within one screen without checking which the script
expects. `Tools/Convert All Legacy Text To TMP` exists for a coordinated migration.

Use fully qualified `UnityEngine.UI.Image` / `Button` in editor scripts; the project
has other `Image` types in scope.

## Checks

Run before pushing. All five are stdlib Python, no dependencies:

```bash
python ci/check_meta.py              # missing/orphan .meta, duplicate GUIDs
python ci/check_beatmaps.py --all --strict
python ci/check_stories.py --strict  # story scripts parse, carry line ids, name real backgrounds
python ci/check_items.py             # server/data/items.json matches the ItemMetas
python ci/check_repo_hygiene.py      # stray build output, LFS integrity, conflict markers
```

`.github/workflows/project-checks.yml` runs the same five on every PR — no Unity
licence, no secrets. There is no CI that compiles C#; only the Editor can do that.

## Traps

| | |
|---|---|
| Renaming `productName`/`companyName` | `persistentDataPath` derives from them, orphaning deployed AssetBundles — all audio vanishes. Run `Arknights/AssetBundles/Deploy To Persistent Data Path` after |
| `excludePlatforms` on `Arknights.asmdef` | Drops the whole front-end from the Android build; the game will not run on a device |
| Moving a `.cs` without its `.meta` | Breaks every reference. Move both, or move it inside Unity |
| Editing Lua in `Assets/Arknights/Resources/LuaScripts/` | Live in the Editor (reads disk first); a device has no such file and reads the copy built into the player through the `Resources` fallback, so rebuild the player before testing on a phone. Keep the folder under `Resources` - outside it a phone has no Lua at all and the front-end never starts |
| An `Image` with alpha 0 | Still a raycast target. A transparent full-screen container will swallow every click behind it |
| Retuning a score award, the multiplier ladder, a judge/health/fever value, or an operator's CharMeta or passive | The server checks results against `server/data/ruleset.json`. Update it in the same change: `RulesetParityTests` fails in CI naming the field, and a device on rules the server does not hold has every run rejected as `RULESET_MISMATCH` |
| Reading an operator's rhythm stats | `CharMeta.RulesAt(level)` (the shared `OperatorRules.AtLevel`), never `GetMaxHp()`/`GetScoreModifier()` alone: those are level 1. A ranked run plays at the level on its ticket (`SongSession.CharacterLevel`) - the one the server recorded and reviews at - so a stat read at any other level is a fingerprint the server rejects as `RULESET_MISMATCH` |
| Changing the level curve, a level cap, a promotion's cost or what a battle record is worth | `OperatorLevels` in `Promuse.Net/Contracts/CharacterContracts.cs`, compiled into the game and the server. `CharMeta.GetMaxLevel`/`GetMaxExp` only forward to it. A new EXP record also needs its `ItemMeta` (DepotUISetup) and the item catalog export, or the server refuses it as not a record and every screen shows it as "Item #id" |
| A passive that rolls dice | Draw from `RunState.NextRoll()`, never `UnityEngine.Random` - that one is shared with the whole scene, so the server could never draw the same numbers when it replays the run |
| Changing how a frame is judged - a window, a miss, a hold, fever, the passive's clock | It belongs in `RunJudge.Step` (Rhythm.Core), which the server runs again over every ranked run's input trace. Anything that moves the score outside it, or reads a clock or an input other than the frame's recorded values, is a result the replay cannot reproduce: every honest run is flagged `REPLAY_MISMATCH`. Avoid float multiply-adds there (a phone may fuse them). A deliberate change re-pins `ReplayGoldenTests` and `RunJudgeTests` together, and `fever.autoActivate` must stay on - opening fever by hand is not in the trace |
| Committing art from `Assets/Arknights/Resources/LocalArt/`, or pointing a tracked asset into it | The repo is public and that art is another game's, so the folder is gitignored on purpose. A tracked reference into it is a missing reference on every clone. Local art is reached only through `CharArtOverrides`; real art goes in `CharMeta`'s own fields. A player built where it is installed packs it (it is under `Resources`) - accepted, as a study build that is never published |
| Importing a Spine atlas | `SpineSettings.asset`'s `defaultShader` must name a shader that exists (`Spine/Skeleton`), or the importer throws on every atlas. An atlas page is premultiplied: import it as Default with Alpha Is Transparency **off** - this project's default texture preset (Sprite, alpha dilation on) puts light speckles round every edge. `Arknights/Art/Install Placeholder Operator Art` does both |
| Adding or changing a headhunting banner | The server owns the rules: odds, pity, price, schedule and pool live in `gacha_banners` / `gacha_pool_entries` (seeded in `PromuseDbContext`). The `GachaBanner` asset is art only. A banner shows only when both exist with the same `bannerId` - either alone shows nothing |
| Adding a remote-config feature flag | Three places or it is only half a switch: the field in `FeatureFlags` (shared contract), `.RequireFeature(...)` on the endpoint it guards (the server refuses), and a `RemoteConfigManager.IsOn` check on the screen (the client explains). A flag only the client obeys is a suggestion an old build ignores |
| An event paying an item the client has no `ItemMeta` for | The server pays it and the bag holds it, but every screen shows "Item #id" with no icon. Add the meta (`DepotUISetup`'s seed list) in the same change as the event that first uses the item, then run `Arknights/Depot/Export Item Catalog`: the admin page names items from `server/data/items.json`, `ci/check_items.py` fails until it matches the metas, and `ItemCatalogTests` fails when the server seeds an item it does not list |
| Changing or reusing a story line's `#id` after it shipped | The admin page corrects a line by `(story, id)`, and the game applies a correction only while the line still reads what it was written against. A new id orphans its correction; a reused id points an old one at a different line. Add lines with `Arknights/Story/Assign Missing Line Ids`, never by hand-copying an id |
| Making the server compile or load something new from `Assets/` | Three places, or the image that ships is not the server that was tested: the link in the `.csproj`, the paths in `backend-checks.yml` (or a change there runs no backend job), and `deploy/docker/api.Dockerfile` - its `COPY` and the allow-list in `api.Dockerfile.dockerignore`. Missed in the last, `dotnet build` passes while the image build fails, or ships without the file |
| Adding a metric, or a tag to one (`PromuseMetrics`) | Every distinct tag value is a series of its own in the metrics database. Tag with small fixed sets - a method, a verdict, an error code, a route *pattern* - never a player id, a run id or a request path (`/v1/runs/<guid>/complete` is a new series per run). Count after the transaction commits, or a rolled-back write is counted as done |
| Changing the database schema | A deploy rolls the API back when the new version does not come up, but never the migration it already ran: the previous version must still work on the new schema. Add a column or table in one release and stop using the old one in a later one - never rename or drop in the same release that stops reading it |
| Changing a secret or a value on the VM | Set it in GitHub (repository variables and secrets, or the environment's own secrets) - every deploy rewrites the VM's `.env` files from them, so a hand edit there is put back by the next one. A new value the pipeline should pass needs `ship.sh`'s list and `deploy-environment.yml`'s `env:`. `DB_PASSWORD` is the exception that bites: Postgres keeps its first password, so change it in Postgres before GitHub, or the deploy and its rollback both fail (`deploy/README.md`, Rules) |
| Adding a service to the VM's compose files (`deploy/compose/`) | Never publish a port: only Caddy does, because Docker's rules come before the firewall and a published port is open to the Internet. Give it a memory limit - nine containers share 4 GB. On the shared `promuse-edge` network, reach another project's container by its environment's alias (`api-staging`, `postgres-production`): Compose registers every service name on every network, so `postgres` there names both databases and a connection lands on either |
| Saving something on the device that belongs to one server - a session, a queue, a cache, a "seen" flag | Each server keeps its own (a build can be Local, Staging or Production - `ServerTarget`): name a PlayerPrefs key with `Api.Scoped(key)` and put a file under `Api.DataDirectory`, never `persistentDataPath` itself. Unscoped, a staging build reads production's data, or sends a queued result to the wrong server. Local's scope is empty, so Local keeps the names a device already has |
| Sending the player back to the login screen | `LoginUI` reopens any session the device still holds, without a password (`SessionResume`). Sign out first with `PlayerManager.Exit()` - it lets go of the session at once, before the logout is answered - or the screen signs the player straight back in |
| Moving the API to a new machine or container | Notice banners and the pictures between a notice's paragraphs are files in `server/src/Promuse.Api/App_Data/banners/` (gitignored), not database rows. Copy that folder, or point `Notices:BannerDirectory` at storage that is kept - otherwise every notice shows without its banner and pictures |
| Playing a campaign stage from anywhere but the campaign screen | Same rule as event stages: only a run started with `campaignStageId` (`SongSession.SetReturn(ui, null, stageId)`) clears it, pays its first-clear Orundum and rolls its drops. The same chart from the song list pays EXP only |
| Playing an event stage from anywhere but the event screen | Only a run started with `eventId` (`SongSession.SetReturn(ui, eventId)`) clears the stage, unlocks the next and pays the currency; the server refuses a locked stage before charging stamina |
| Showing text a player typed (the profile signature, or anything like it later) | TMP reads `<tags>`: turn `richText` off on the field and on its text, or a line of `<size=300>` breaks the card of everyone who opens it. The server drops invisible characters (a right-to-left override reverses a line on every screen); it does not escape markup, because only the screen knows it is TMP |
| Anything new that shows a player to others - a list, a chat, a friend request | A signed-up player has no name until the name screen: `players.display_name` and `tag` are null, and only the owner ever sees them that way because a run, a signature and a report are refused with `NAME_REQUIRED` before it. A new way of being seen needs the same refusal, or it shows a blank name. The username only signs in and is never shown |
| A new screen, or a panel opened over one | Android Back presses the top screen's own way out (`UIBase.OnBack` / `ExitButton`). A screen with no back or close button and no `OnBack` is closed by a bare `Hide` that may not be what its button does, and a panel opened over a screen's content needs an `OnBack` that closes it first - or Back leaves the whole screen. `BackKeyTests` fails for a screen prefab with no way back |
| Opening someone's profile card | `ProfileUI.Open(playerId)`. `UIManager.Show("ProfileUI")` on its own - which is all Home's button does - always opens the player's own card |
| Showing an operator's standing art | `SplashFrame.Show(image, meta)` (Lua: `CS.UI.SplashFrame.Show`), never `image.sprite = meta.GetImage()`. The splash is whole and uncut; only a frame keeps its proportions. A new screen's `Image` needs a frame - add it to the list in `OperatorSplashFrames` |
| Setting `Application.targetFrameRate` anywhere else, or removing `Manager.FrameRate` | It sets 60 once, before the first scene. A phone left to itself draws at 30, and a tap is judged on the first frame that sees it - up to 33 ms late, most of a Great window. A second place setting it (a scene, a screen) silently wins wherever it runs |
| Anything in `CharacterPresenter` choosing by chance | It is presentation, and the server replays a run from its seed. Pick from the event (lane, note type), never from `RunState.NextRoll()` or `UnityEngine.Random` |
| Sizing an operator anywhere it stands | `Tools.RigHeight` (`Assets/Arknights/Utils/`) is the one measure, shared by the rhythm stage (`CharacterPresenter`) and the Manufacture room (`ManufactureChibi`): the top of what the idle pose draws, never the height a Spine export records. A second measure makes the same operator a different size in each place |

## Working here

- `Assets/Arknights/Audio/` and `Assets/StreamingAssets/` are gitignored, so four
  missing-audio errors on boot are expected, not a regression.
- `Assets/Arknights/Resources/LocalArt/` is gitignored too: the operators' reference art, reinstalled by
  `Arknights/Art/Install Placeholder Operator Art`. The game runs without it - the operators show their
  tracked placeholders and the stage and Home screen stay empty.
- Commit messages follow Conventional Commits (`feat(depot): ...`).
- Do not commit or push unless asked.
