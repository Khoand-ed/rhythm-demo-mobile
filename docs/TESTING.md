# Testing what is built

Two ways to exercise the backend, and they answer different questions.

**Through the game** is the only thing that proves a screen is wired to the right
call and draws what comes back. It is slow, and several states are hard to reach
by playing — an empty wallet, a dropped response, two devices writing at once.

**Through the API** reaches every one of those in a second, and is how the
error paths get exercised at all. It proves nothing about the UI.

Do both. This file is written so each step says what the right answer is, and
every number in it was taken from a real run rather than from the source.

---

## 0. Get it running

```bash
docker compose -f server/docker-compose.yml up -d
```

```bash
dotnet run --project server/src/Promuse.Api --urls http://127.0.0.1:5199
```

`--urls` is not optional. Without it the SDK reads `launchSettings.json` and
binds **5014**, while the client's `PromuseConfig` points at **5199**, and the
game answers "Cannot reach the server" while a perfectly healthy API is running.

Check it before anything else:

```bash
curl -s http://127.0.0.1:5199/health/live && curl -s http://127.0.0.1:5199/health/ready
```

Both print `Healthy`. `ready` also reaches Postgres, so it is the one that fails
when the container is down.

---

## 1. Through the game

Open `Assets/Arknights/Scenes/StartMenu.unity` and press Play.

### 1.1 Register and sign in

Register on the login screen: username 3–24 characters (letters, digits,
underscore), password 8–128.

| Try | Expected |
|---|---|
| Register a fresh username | Lands in the game, roster of 4 operators, stamina 82/82 |
| Register the same name twice | "That username is taken" — not a crash, not a second account |
| Quit Play, press Play again | Signed straight back in, no password asked |
| Stop the API, press Play | "Cannot reach the server" |

A new account owns **5 Originite Prime, 500 Orundum, 1000 LMD** and no Orirock.
That last part matters below.

### 1.2 The shop

Every offer is priced in **Orirock (item 6)**, and a new account has none — so
the first thing the shop proves is the refusal.

| Try | Expected |
|---|---|
| Buy anything | "You do not own enough of the required item" |

Now grant some Orirock ([§4](#4-inspecting-and-resetting)) and try again:

| Try | Expected |
|---|---|
| Buy 1 Recruitment Permit (8 Orirock) | Wallet drops by 8, permit appears in the depot |
| Sign out, sign back in | The permit is still there |
| Set quantity to 5 and buy | Wallet drops by 40, 5 permits — **one** purchase, not five |

That last row is the one worth watching: the Store missions count **checkouts**,
not units. Buying 5 at once advances "purchase 3 times" by 1.

### 1.3 A song

Song select → operator select → the run starts.

| Try | Expected |
|---|---|
| Start `stage_001` | Stamina drops by **6** before the song loads |
| Start `stage_AIW_hard` | Drops by **12** |
| Finish and clear the song | Mission counters move (check the board next) |
| Fail the song (let HP run out) | Counters do **not** move — a fail is not a clear |
| Play with stamina below the cost | Refused at the character screen, and you can back out |

The stamina is charged by the server **before** the scene loads, so closing the
app mid-song does not give a free attempt.

### 1.4 The mission board

| Try | Expected |
|---|---|
| Open the board | Both tabs draw; a clear you just finished is already counted |
| Claim a finished mission | Points go up. **No items** — missions pay points |
| Claim a reward the points now reach | Items land in the depot, wallet updates |
| Press CLAIM ALL | Missions first, then every reward the new total unlocks |
| Claim the same reward twice | Refused |
| Sign out and back in | Everything claimed is still claimed |

The board is fetched on every open, so a run finished elsewhere shows up without
a restart.

The seeded boards:

| Board | Mission | Target | Points |
|---|---|---|---|
| Daily | `daily.play1` — clear any song | 1 | 1 |
| Daily | `daily.play2` — clear any song | 2 | 2 |
| Daily | `daily.buy1` — purchase from the Store | 1 | 3 |
| Weekly | `weekly.play5` — clear any song | 5 | 2 |
| Weekly | `weekly.play10` — clear any song | 10 | 3 |
| Weekly | `weekly.buy3` — purchase from the Store | 3 | 5 |

| Board | Reward | Needs | Pays |
|---|---|---|---|
| Daily | `daily.r1` | 1 pt | 500 LMD |
| Daily | `daily.r2` | 2 pt | 100 Orundum |
| Daily | `daily.r3` | 3 pt | 3 Orirock |
| Weekly | `weekly.r1` | 3 pt | 2000 LMD |
| Weekly | `weekly.r2` | 6 pt | 300 Orundum |
| Weekly | `weekly.r3` | 10 pt | 5 Sugar Substitute |

"Needs" counts points **claimed**, not points earned — finishing a mission is
not enough, its own button has to be pressed.

Boards reset at **04:00 UTC+7**, which is 21:00 UTC the day before. Nothing is
scheduled to do it: the period is part of the row key, so yesterday's counters
are simply no longer the ones being read.

---

## 2. Through the API

Everything below was run against a live server; the expected answers are
transcribed from it.

Set up a shell:

```bash
S=http://127.0.0.1:5199
KEY() { python -c 'import uuid;print(uuid.uuid4())'; }
```

### 2.1 An account

```bash
T=$(curl -s -X POST $S/v1/auth/register -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $(KEY)" \
  -d '{"username":"tester_1","password":"correct horse battery"}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['tokens']['accessToken'])")
```

```bash
curl -s -H "Authorization: Bearer $T" $S/v1/players/me | python -m json.tool
```

Level 1, exp 0, stamina `82/82` with `nextPointAt: null` (a full bar has no
deadline), 4 characters, inventory `[{0,5},{1,500},{2,1000}]`.

### 2.2 Idempotency

The header is required on everything that creates or consumes. Leaving it off:

```bash
curl -s -X POST $S/v1/auth/guest -H 'Content-Type: application/json' -d '{"deviceId":"x"}'
```

→ `400 IDEMPOTENCY_KEY_REQUIRED`.

The interesting case is a retry. Buy something twice with the **same** key:

```bash
OFFER=11111111-0000-0000-0000-000000000005   # Recruitment Permit, 8 Orirock
K=$(KEY)
curl -s -X POST $S/v1/shop/purchases -H "Authorization: Bearer $T" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $K" \
  -d "{\"offerId\":\"$OFFER\",\"quantity\":5}"
```

Run that exact command a second time. The wallet is charged **once**; the second
call replays the first answer. Now change `quantity` to 1 while keeping `$K`:

→ `409 IDEMPOTENCY_KEY_CONFLICT`. The same key for a different request is not a
retry, and answering it with the first response would be a lie.

Records are scoped per account, so two players presenting the same key never
meet in the same record.

### 2.3 A run, start to finish

```bash
RUN=$(curl -s -X POST $S/v1/runs -H "Authorization: Bearer $T" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $(KEY)" \
  -d '{"stageId":"stage_001"}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['runId'])")
```

The ticket carries a **seed** the server issued. It exists because
`JudgeUpgradePassive` rolls per judgement: a result nobody can re-simulate
cannot be validated, and a client that chose its own seed would re-roll until
the draw was favourable. Phase 4 replays against it.

Close it — the key is the run id, not a fresh Guid:

```bash
curl -s -X POST $S/v1/runs/$RUN/complete -H "Authorization: Bearer $T" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $RUN" \
  -d '{"won":true}' | python -m json.tool
```

| Try | Expected |
|---|---|
| The call above | `200`, `daily.play1` is now `1/1` and complete |
| The exact same call again | `200`, replayed — `daily.play1` still `1/1` |
| Same run, **new** key | `409 RUN_NOT_OPEN` |
| `{"won":false}` on a fresh run | Closes, counters do not move |
| Someone else's run id | `409 RUN_NOT_OPEN` — worded exactly like a run that does not exist, so a stranger cannot probe which ids are real |

### 2.4 Claims

```bash
curl -s -X POST $S/v1/missions/claims -H "Authorization: Bearer $T" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $(KEY)" \
  -d '{"tab":"Daily","missionId":"daily.play1"}'
```

`granted` is `[]` and the board's `points` goes to 1 — missions pay points. That
makes `daily.r1` claimable:

```bash
curl -s -X POST $S/v1/missions/reward-claims -H "Authorization: Bearer $T" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $(KEY)" \
  -d '{"tab":"Daily","rewardId":"daily.r1"}'
```

`granted: [{itemId: 2, amount: 500}]`, and LMD in the inventory goes 1000 → 1500.
Claim it again → `409 ALREADY_CLAIMED`. Claim a mission that is not finished →
`409 MISSION_NOT_COMPLETE`.

### 2.5 Two devices writing at once

Every write to the player carries the version it was based on.

```bash
ET=$(curl -s -D - -o /dev/null -H "Authorization: Bearer $T" $S/v1/players/me \
  | grep -i '^etag' | tr -d '\r' | sed 's/[Ee][Tt]ag: //')
```

| Try | Expected |
|---|---|
| `PUT /v1/players/me/squad` with no `If-Match` | `428 PRECONDITION_REQUIRED` |
| With the current ETag | `200`, `stateVersion` goes up by one |
| Again with the **same** (now stale) ETag | `412 STATE_CONFLICT`, and the message names the version it is actually at |

```bash
curl -s -X PUT $S/v1/players/me/squad -H "Authorization: Bearer $T" \
  -H "If-Match: $ET" -H 'Content-Type: application/json' \
  -d '{"squad":["AMIYA",null,null,null]}'
```

This is the second device arriving with a stale copy. It is told to re-read
rather than allowed to overwrite.

### 2.6 Refresh tokens

Each refresh rotates the token, and presenting a spent one kills the whole
family — including tokens that were still valid a moment earlier. Register a
throwaway account and keep its refresh token:

```bash
R1=$(curl -s -X POST $S/v1/auth/register -H 'Content-Type: application/json'   -H "Idempotency-Key: $(KEY)"   -d '{"username":"tester_2","password":"correct horse battery"}'   | python -c "import sys,json;print(json.load(sys.stdin)['tokens']['refreshToken'])")
```

```bash
R2=$(curl -s -X POST $S/v1/auth/refresh -H 'Content-Type: application/json' \
  -d "{\"refreshToken\":\"$R1\"}" \
  | python -c "import sys,json;print(json.load(sys.stdin)['refreshToken'])")
```

| Try | Expected |
|---|---|
| Refresh with `R1` | New pair; `R2 != R1` |
| Refresh with `R2` | New pair `R3` |
| Refresh with `R1` again | `401 REFRESH_TOKEN_REUSED` |
| Refresh with `R3`, which was valid | `401 REFRESH_TOKEN_REUSED` — the family is gone |

A stolen token is therefore worth one use before the theft becomes visible.

---

## 3. The automated suites

```bash
cd server && dotnet test
```

133 tests. The API ones start a real Postgres through Testcontainers, so Docker
has to be up; they do not touch the dev database.

```bash
python ci/check_meta.py
```

```bash
python ci/check_beatmaps.py --all --strict
```

```bash
python ci/check_repo_hygiene.py
```

The same checks run on every PR, alongside the .NET build and a job that
applies the migrations to a real database. There is no CI that compiles C# for
the client — only the Editor can do that, so check the Unity console before
pushing.

---

## 4. Inspecting and resetting

```bash
docker exec -it promuse-postgres psql -U promuse -d promuse
```

Grant Orirock so the shop is reachable — replace the id with the `playerId` from
`GET /v1/players/me`:

```sql
INSERT INTO player_items (account_id, item_id, amount)
VALUES ('<playerId>', 6, 100)
ON CONFLICT (account_id, item_id) DO UPDATE
SET amount = player_items.amount + 100;
```

Refill the stamina bar instead of waiting (it regenerates 1 point per 3 minutes):

```sql
UPDATE players SET stamina = 82, stamina_updated_at = now()
WHERE account_id = '<playerId>';
```

Worth a look while testing:

| Table | Holds |
|---|---|
| `players`, `player_items` | The wallet and the state version |
| `runs` | One row per attempt: stage, seed, `completed_at`, `won` |
| `purchases` | The checkout ledger |
| `mission_counters` | Progress, keyed by period — this is what resets |
| `mission_claims`, `mission_reward_claims` | What has been taken this period |
| `idempotency_records` | Stored responses, 24h retention |

To watch a board reset without waiting for 04:00, move its counters into a past
period:

```sql
UPDATE mission_counters SET period_key = '2020-01-01' WHERE account_id = '<playerId>';
```

Start over completely:

```bash
docker compose -f server/docker-compose.yml down -v
```

That drops the volume. The next start re-runs the migrations and re-seeds the
catalogue, so accounts are gone but the shop and the missions come back.

---

## 5. Not covered yet

- **Score is not submitted.** `POST /v1/runs/{id}/complete` carries only `won`.
  The score, the input trace and the replay validation against the seed are
  Phase 4, and the endpoint is shaped to take them without a second call.
- **No leaderboard.**
- **Gacha is still client-side.** Nothing in this file touches it.
- **The rate limiter has no test.** `RateLimit.AuthPermitPerMinute` is 20 and is
  applied, but nothing asserts it.
