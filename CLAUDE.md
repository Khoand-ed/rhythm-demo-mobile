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

Run before pushing. All three are stdlib Python, no dependencies:

```bash
python ci/check_meta.py              # missing/orphan .meta, duplicate GUIDs
python ci/check_beatmaps.py --all --strict
python ci/check_repo_hygiene.py      # stray build output, LFS integrity, conflict markers
```

`.github/workflows/project-checks.yml` runs the same three on every PR — no Unity
licence, no secrets. There is no CI that compiles C#; only the Editor can do that.

## Traps

| | |
|---|---|
| Renaming `productName`/`companyName` | `persistentDataPath` derives from them, orphaning deployed AssetBundles — all audio vanishes. Run `Arknights/AssetBundles/Deploy To Persistent Data Path` after |
| `excludePlatforms` on `Arknights.asmdef` | Drops the whole front-end from the Android build; the game will not run on a device |
| Moving a `.cs` without its `.meta` | Breaks every reference. Move both, or move it inside Unity |
| Editing Lua in `Assets/Arknights/LuaScripts/` | Live in the Editor (reads disk first), but a device build reads the AssetBundle — rebuild before testing on a phone |
| An `Image` with alpha 0 | Still a raycast target. A transparent full-screen container will swallow every click behind it |
| Retuning a score award, the multiplier ladder, a judge/health/fever value, or an operator's CharMeta or passive | The server checks results against `server/data/ruleset.json`. Update it in the same change: `RulesetParityTests` fails in CI naming the field, and a device on rules the server does not hold has every run rejected as `RULESET_MISMATCH` |
| A passive that rolls dice | Draw from `RunState.NextRoll()`, never `UnityEngine.Random` - that one is shared with the whole scene, so the server could never draw the same numbers when it replays the run |
| Committing art from `Assets/Arknights/Resources/LocalArt/`, or pointing a tracked asset into it | The repo is public and that art is another game's, so the folder is gitignored on purpose. A tracked reference into it is a missing reference on every clone. Local art is reached only through `CharArtOverrides`; real art goes in `CharMeta`'s own fields |
| Importing a Spine atlas | `SpineSettings.asset`'s `defaultShader` must name a shader that exists (`Spine/Skeleton`), or the importer throws on every atlas. An atlas page is premultiplied: import it as Default with Alpha Is Transparency **off** - this project's default texture preset (Sprite, alpha dilation on) puts light speckles round every edge. `Arknights/Art/Install Placeholder Operator Art` does both |
| Anything in `CharacterPresenter` choosing by chance | It is presentation, and the server replays a run from its seed. Pick from the event (lane, note type), never from `RunState.NextRoll()` or `UnityEngine.Random` |

## Working here

- `Assets/Arknights/Audio/` and `Assets/StreamingAssets/` are gitignored, so four
  missing-audio errors on boot are expected, not a regression.
- `Assets/Arknights/Resources/LocalArt/` is gitignored too: the operators' reference art, reinstalled by
  `Arknights/Art/Install Placeholder Operator Art`. The game runs without it - the operators show their
  tracked placeholders and the stage and Home screen stay empty.
- Commit messages follow Conventional Commits (`feat(depot): ...`).
- Do not commit or push unless asked.
