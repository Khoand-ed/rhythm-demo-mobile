# Running Promuse on a server

How the API runs outside a laptop: one Ubuntu VM, two environments - **staging** and
**production** - behind HTTPS, with metrics, logs, alerts and nightly backups. Everything here
is files in this folder and the scripts beside them; nothing is configured by hand on the VM
except what this page says to.

```
Internet ─ 80/443 ─> Caddy (HTTPS from Let's Encrypt, one site per name)
   ├─ <name>-staging.duckdns.org ──> api-staging ────> postgres-staging      promuse-staging
   ├─ <name>.duckdns.org ──────────> api-production ─> postgres-production   promuse-production
   └─ <name>-grafana.duckdns.org ──> Grafana                                  promuse-edge
Alloy ── OTLP metrics from both APIs, every container's log, the host, the containers,
         both databases, an HTTPS probe of each /health/ready ──> Prometheus + Loki ──> Grafana ──> Discord
```

| Compose project | File | Holds |
|---|---|---|
| `promuse-edge` | `compose/edge.compose.yml` | Caddy, Alloy, Prometheus (15 days), Loki (14 days), Grafana |
| `promuse-staging`, `promuse-production` | `compose/app.compose.yml` | that environment's API and Postgres, its own volumes |

They meet on one Docker network, `promuse-edge`. Only Caddy publishes a port. Every other
service - both databases included - is reachable only from inside that network.

| Folder | What is in it |
|---|---|
| `docker/` | the API image (`api.Dockerfile`) and the smoke test CI runs on it |
| `compose/` | the two compose files |
| `caddy/` | the Caddyfile |
| `monitoring/` | Alloy's config, Prometheus, Loki, and Grafana's datasources, dashboards and alerts |
| `scripts/` | everything run on the VM - see below - and `ship.sh`, which sends a deploy to it |
| `local/` | the same stack on Docker Desktop, for rehearsing a change before it reaches the VM |

Every pull request that touches this folder or a workflow runs `deploy-checks`:
- shellcheck on the scripts;
- `docker compose config` on both compose files;
- each service's own validator on its configuration - `caddy validate` and `caddy fmt`, `alloy fmt`, `promtool check config`, `loki -verify-config` - at the versions the compose files pin;
- actionlint on every workflow.

On the VM, `/srv/promuse` holds what is not in the repository:

| Path | What |
|---|---|
| `deploy/` | a copy of this folder - the scripts run from here. Every deploy rewrites it, in place, with main's |
| `deploy.previous/` | the copy before that one |
| `edge/.env`, `staging/.env`, `production/.env` | the configuration and secrets each project runs with, readable by `deploy` only |
| `backups/<environment>/` | seven days of nightly backups |
| `metrics/` | what the scripts tell Alloy: last backup, what is deployed |

---

## First time

Some steps need your accounts; the scripts do the rest.

### 1. The accounts

- **Azure VM.**
  - Ubuntu Server 24.04 LTS, x64, size B2s (2 vCPU, 4 GB) or larger, a 64 GB disk, a static public IP.
  - In its network security group, allow 22, 80 and 443 from anywhere. SSH cannot be narrowed to your own IP: the pipeline's runners connect from GitHub's addresses, which are many and change. What guards it instead is `setup-vm.sh`: keys only, no passwords, no root, and fail2ban against guessing.
  - Add a budget alert to the subscription. The VM costs about 30-40 USD a month, so student credit lasts two to three months. *Stop (deallocate)* it when no one needs it.
- **DuckDNS** (duckdns.org).
  - Create three subdomains: `<name>-staging`, `<name>`, `<name>-grafana`.
  - Type the VM's IP into each one's *current ip* and press *update ip*. Caddy can obtain a certificate only once the names lead to the VM, and the first deploy waits for one.
  - Note the account's token. The VM's timer keeps the addresses current from then on.
- **Discord.**
  - In the channel that should receive alerts: *Edit Channel → Integrations → Webhooks → New Webhook*.
  - Copy its URL.

### 2. A deploy key

On your own machine:

```bash
ssh-keygen -t ed25519 -C promuse-deploy -f promuse-deploy
```

`promuse-deploy.pub` goes to the VM (next step). `promuse-deploy`, the private half, goes into GitHub as a secret for the pipeline, and nowhere else.

### 3. The VM

Copy this folder to the VM, then prepare it - Docker, the `deploy` user, SSH keys only, the firewall, fail2ban, security updates, `/srv/promuse` and the timers (`scripts/setup-vm.sh` lists each step):

```bash
scp -r deploy <admin>@<vm-ip>:/tmp/promuse-deploy
ssh <admin>@<vm-ip>
sudo mkdir -p /srv/promuse && sudo cp -r /tmp/promuse-deploy /srv/promuse/deploy
sudo /srv/promuse/deploy/scripts/setup-vm.sh "<the line in promuse-deploy.pub>"
```

It is safe to run again. From here on, sign in as `deploy` with the key: password logins and root logins are off.

Still in the same session, print the line the pipeline will check the VM against. It comes from inside the VM itself, so a machine pretending to be the VM later is refused:

```bash
echo "<vm-ip> $(cut -d' ' -f1,2 /etc/ssh/ssh_host_ed25519_key.pub)"
```

### 4. GitHub

The pipeline deploys with what GitHub holds; nothing is typed on the VM again.

**Two environments.** *Settings → Environments → New environment*:

| Environment | Deployment protection rules | Deployment branches and tags |
|---|---|---|
| `staging` | none | *Selected*: branch `main` |
| `production` | *Required reviewers*: you | *Selected*: tag `v*`, and branch `main` (a rollback is run from main) |

Create both **before** the next step. A job that names an environment GitHub does not have creates it, with no reviewer - the deploy refuses to run under one that has no secrets, which is the guard against exactly that.

**Variables and secrets** - from a clone of the repository, with the GitHub CLI signed in. The values are typed on your machine only:

```bash
# Repository variables: not secret.
gh variable set VM_HOST --body "<vm-ip>"
gh variable set VM_KNOWN_HOSTS --body "<the line printed in step 3>"
gh variable set STAGING_HOST --body "<name>-staging.duckdns.org"
gh variable set PRODUCTION_HOST --body "<name>.duckdns.org"
gh variable set GRAFANA_HOST --body "<name>-grafana.duckdns.org"
gh variable set ACME_EMAIL --body "<your email, for Let's Encrypt>"
gh variable set DUCKDNS_DOMAINS --body "<name>-staging,<name>,<name>-grafana"

# Repository secrets, shared by both environments.
gh secret set VM_SSH_KEY < promuse-deploy           # the private half from step 2
gh secret set DISCORD_WEBHOOK_URL                   # asks for the value: paste it
gh secret set DUCKDNS_TOKEN                         # the same
gh secret set GRAFANA_ADMIN_PASSWORD                # your Grafana sign-in: make one with `openssl rand -hex 24` and keep it
gh secret set MONITOR_DB_PASSWORD --body "$(openssl rand -hex 24)"

# Each environment's own.
for env in staging production; do
  gh secret set DB_PASSWORD --env "$env" --body "$(openssl rand -hex 24)"
  gh secret set JWT_SIGNING_KEY --env "$env" --body "$(openssl rand -hex 32)"
done
```

Hex everywhere: the scripts write values unquoted into the VM's `.env` files. `VM_PORT` and `VM_USER` are optional variables, for an SSH port other than 22 or a user other than `deploy`.

The database passwords and signing keys never need to be read again: every deploy passes them on.

> **Do not change `DB_PASSWORD` or `JWT_SIGNING_KEY` once an environment is running.** Postgres keeps the password it was first started with, so a new one locks the API out of the database - the deploy fails, and so does its rollback, which runs on the same `.env`. A new signing key signs every player out. *Rules* below says how to change one.

### 5. The first deploy

Merge anything into main, or run the workflow by hand: *Actions → deploy → Run workflow*, environment `staging`, tag the `sha-<commit>` of a run on main that built an image. `edge.sh` starts the edge, then `deploy.sh` staging. Production's first deploy is the first version tag (*Every day* below).

If the very first deploy fails waiting for `/health/ready`, Caddy may still be fetching the certificates: *Re-run jobs* once the names lead to the VM.

### 6. The game

Point a build at the VM:
1. Type `https://<name>-staging.duckdns.org` and `https://<name>.duckdns.org` into `Assets/Promuse.Net/Resources/ServerTarget.asset`, and commit them.
2. Choose *Arknights/Build/Server: Staging* (or *Production*).
3. Build: `Promuse-<version>-staging-dev.apk` plays over the Internet, from any network.

The login screen shows `STAGING · v<version>` on every build that is not production (SETUP: *Which server the game talks to*).

---

## Every day

### Deploy, redeploy, roll back

Through the pipeline, `.github/workflows/deploy.yml`:

| To | Do | It runs |
|---|---|---|
| **staging** | merge into main | the backend checks; then the image `sha-<commit>` is built, started (the same smoke test as a pull request) and pushed to GHCR; then staging deploys it |
| **production** | `git tag v1.2.0 <commit> && git push origin v1.2.0` | the image main built for that commit is tagged `v1.2.0` - the same image, not rebuilt; production waits until you approve (*Review deployments* on the run), then deploys it |
| **again, or back** | *Actions → deploy → Run workflow* from main: the environment and an image tag (`v1.1.0`, `sha-<commit>`) | that image, deployed as above. Production still waits for approval |

A version tag on a commit main never built - a branch, or a commit whose checks failed - stops with *No image for vX.Y.Z*. Tag a commit staging has run.

Each deploy is `deploy-environment.yml`, one environment at a time - a deploy that has started is never cancelled, and one that arrives meanwhile waits. Its last step is `scripts/ship.sh`, the same script a deploy from a laptop runs:
1. It copies main's `deploy/` folder to `/srv/promuse/deploy` over SSH, keeping the one before as `deploy.previous`. It is always main's, whatever the image: the edge serves both environments, so an older tag's configuration must not come back with an older image. The copy is written over the old one in place, never swapped for it: the services hold their configuration folders open, and would keep reading a swapped-out one.
2. On the VM, it signs Docker in to GHCR with the run's own token. That token reads this repository's packages and expires with the job; the VM signs out when the deploy ends.
3. It runs `edge.sh`. That rewrites the edge's `.env` from GitHub's values, starts anything not running, and reloads or restarts only a service whose configuration files changed - Caddy reloads in place, without dropping a connection.
4. It runs `deploy.sh <environment> <image>`. That rewrites the environment's `.env` the same way and deploys, as below.

Every value reaches the VM inside the SSH session's input, never on a command line, where the VM's process list - or the run's log - would show it.

Until the repository has a `VM_HOST` variable, every run still builds and tags images, and says it deployed nothing.

`deploy.sh` takes these steps, in order:
1. Pulls the image.
2. Makes sure Postgres and Alloy's read-only `promuse_monitor` role exist.
3. Backs up.
4. Runs `--migrate`.
5. Recreates the API.
6. Waits up to two minutes for `https://<name>/health/ready` to answer `Healthy` through Caddy.

What happens next depends on how that ends:
- **The API never becomes ready:** the previous image goes back up, Discord is told, and the script exits non-zero.
- **The migration fails:** the API is never touched.
- **It works:** the deployment is recorded (the *Deployed* panel and a mark on every dashboard).

**Rolling back on purpose** is a deploy of the older image.

The API rolls back; the database does not. A migration must therefore leave the schema usable by the version before it: add in one release, remove in a later one.

**By hand**, when GitHub is not available - from a clone, with the deploy key:

```bash
VM_HOST=<vm-ip> SSH_KEY_FILE=promuse-deploy deploy/scripts/ship.sh staging ghcr.io/<owner>/promuse-api:sha-<commit>
```

The VM reuses the values its `.env` files already hold. The package is private, so the VM has to sign in to pull: also set `GHCR_USER=<your GitHub user>` and `GHCR_TOKEN=<a token with read:packages only>`. Or, signed in on the VM as `deploy`, run `/srv/promuse/deploy/scripts/deploy.sh staging <image>` on an image already there.

Each merge leaves an image in GHCR (*Packages → promuse-api*). Delete old versions there now and then - only ones whose tags are all `sha-`: a released image is one version carrying both its `sha-` tag and its `v` tag, and deleting it deletes the release.

### Administrators, backups, restores

```bash
/srv/promuse/deploy/scripts/make-admin.sh staging <username>       # the /admin page signs in with this account
/srv/promuse/deploy/scripts/backup.sh [staging|production]         # also nightly at 20:00 UTC (03:00 in Vietnam), and before every deploy
/srv/promuse/deploy/scripts/restore.sh staging /srv/promuse/backups/staging/promuse-staging-<time>.dump \
                                               /srv/promuse/backups/staging/promuse-staging-<time>-banners.tar.gz
```

**Backups** take the database (`pg_dump`) and the uploaded notice pictures. They are kept seven days.

**Restore** asks for the environment's name before it goes on. It backs up the current state first, then stops the API, restores, and starts it again.

The backups live on the VM's own disk, so they protect against mistakes - a bad migration, a deleted row - but not against losing the VM. Copy one off now and then:

```bash
scp deploy@<vm-ip>:/srv/promuse/backups/production/<file> .
```

### Logs and state

```bash
docker compose -p promuse-staging ps
docker compose -p promuse-staging logs --tail 100 api
docker compose -p promuse-edge logs --tail 100 caddy
journalctl -u promuse-backup -u promuse-duckdns --since today
```

Grafana's *Explore → Loki* searches every container's log.

The labels are:
- `environment`: `staging`, `production`, `edge`;
- `service`: `api`, `postgres`, `caddy`...;
- `level`: `error`, `warn`, `info`.

The API logs one JSON object a line, so `{environment="production", service="api"} | json | Category="..."` filters by any of its fields. A 500 answer carries the trace id that is also in its log line.

---

## Monitoring

`https://<name>-grafana.duckdns.org` signs in as `admin` with `GRAFANA_ADMIN_PASSWORD`. There is no anonymous access and no sign-up.

| Dashboard | Shows |
|---|---|
| Promuse · API | ready, request rate, 5xx share, p95; requests by status, p50/p95 by route, the database pool and query time, the sign-in limiter, the error log |
| Promuse · Game | runs by verdict and the flagged share, why results were rejected or flagged, sign-ins, headhunting by rarity, the shop, LiveOps refusals |
| Promuse · Infrastructure | CPU, memory, disk, memory and CPU by container, restarts, both databases, the probes, the last backup, what each environment runs |

The dashboards, datasources and alert rules are files under `monitoring/grafana/`. Change them there; the next deploy restarts Grafana with them. An edit made in Grafana is not kept.

### Alerts (to Discord)

| Alert | When | First look |
|---|---|---|
| API not ready | `/health/ready` failing for 2 minutes, in an environment deployed at least once | `docker compose -p promuse-<env> ps`, then the api and postgres logs |
| API 5xx above 5% | for 5 minutes | the error log on the API dashboard; each 500 has its trace id |
| API p95 above 1 second | for 10 minutes | p95 by route; memory and CPU on Infrastructure |
| Disk above 85% | for 10 minutes | `/srv/promuse/backups`, `docker image prune` |
| Memory below 10% | for 10 minutes | memory by container; each has a limit in the compose files |
| Backup older than 26 hours | for 5 minutes | `journalctl -u promuse-backup` |
| Rejected runs spiking | over 10 in 15 minutes | the rejection reasons on the Game dashboard |

---

## Rehearsing on Docker Desktop

`local/rehearse.sh` runs the whole stack on this machine through the same scripts the VM runs. It uses names under `.localhost`, Caddy on 8443, and throwaway values from `local/*.env`.

```bash
deploy/local/rehearse.sh up                    # build the image, start the edge, deploy both environments
deploy/local/traffic.sh staging                # a little traffic for the dashboards
deploy/local/rehearse.sh deploy staging <image>
deploy/local/rehearse.sh down                  # stop everything and delete its volumes
```

| | |
|---|---|
| `https://staging.localhost:8443/health/ready` | staging through Caddy (certificate from Caddy's own CA: `curl -k`) |
| `http://127.0.0.1:3000` | Grafana, `admin` / `local-rehearsal-only` |

Where it differs from the VM:
- **The disk panel is empty.** Docker Desktop has no `/` filesystem to measure.
- **Certificates last 12 hours.** They come from Caddy's own CA.
- **Alerts go nowhere** unless something listens on port 9999 of this machine (`DISCORD_WEBHOOK_URL` in `local/edge.env`).

State goes to `local/state/` (gitignored).

---

## Rules

- **Only Caddy publishes a port.** Docker's own firewall rules come before ufw, so a published port is open to the Internet whatever ufw says.
- **On the edge network, address another project's container by its environment's name** (`api-staging`, `postgres-production`). Compose gives every container its service name on every network it joins, so `api` or `postgres` there names both environments' at once.
- **Every container has a memory limit.** Four gigabytes is shared by nine of them.
- **Deploy through the pipeline.** It is the one way that runs the checks, deploys an image that was seen to start, records who approved production, and keeps one deploy per environment at a time. *By hand* above is for when GitHub is down.
- **Secrets are set in GitHub, and only there.** Every deploy writes them into `/srv/promuse/*/.env` (mode 600), so a value edited on the VM is quietly put back by the next deploy. Never in this repository. Generate them as hex.
- **Changing a secret that something else keeps:**
  - `DB_PASSWORD`: change it in Postgres first - `docker compose -p promuse-<env> exec postgres psql -U promuse -d promuse`, then `\password promuse`, which asks for it rather than keeping it in the shell's history - then in GitHub, then deploy.
  - `GRAFANA_ADMIN_PASSWORD`: Grafana reads it only the first time it starts. Change it in Grafana (*Profile → Change password*), then in GitHub: deploys use it to mark the dashboards.
  - `JWT_SIGNING_KEY`: changes when the next deploy runs, and signs every player out.
  - `MONITOR_DB_PASSWORD`: change it, then deploy both environments. Each deploy sets it in its own database while Alloy starts using it for both at once, so the other database's panels stay empty until its deploy runs.
- **A migration keeps the previous version working** - see *Deploy* above.

## Sources

Each configuration follows its tool's own documentation, read for the versions pinned in the compose files:
- Grafana Alloy - the components used, under grafana.com/docs/alloy/latest/reference/components/: `otelcol.receiver.otlp`, `otelcol.processor.transform`, `otelcol.exporter.prometheus`, `prometheus.remote_write`, `prometheus.exporter.unix|cadvisor|postgres|blackbox`, `discovery.docker`, `loki.source.docker`, `loki.process`; and `sys.env` from the standard library.
- Prometheus storage and the remote write receiver: prometheus.io/docs/prometheus/latest/storage/.
- Loki: the filesystem configuration example and retention - grafana.com/docs/loki/latest/configure/examples/configuration-examples/ and …/operations/storage/retention/.
- Grafana provisioning - datasources, dashboards, environment variables: grafana.com/docs/grafana/latest/administration/provisioning/. Alerting: …/alerting/set-up/provision-alerting-resources/file-provisioning/.
- Caddy's `reverse_proxy`, and its handling of `X-Forwarded-*`: caddyserver.com/docs/caddyfile/directives/reverse_proxy.
- Docker Engine on Ubuntu: docs.docker.com/engine/install/ubuntu/.
- Unattended upgrades: ubuntu.com/server/docs/how-to/software/automatic-updates/.
- DuckDNS' update API: duckdns.org/spec.jsp.
- GitHub Actions, under docs.github.com/en/actions: reusable workflows (`workflow_call`, `secrets: inherit`, and environment secrets in a called job); deployments and environments (required reviewers, deployment branches and tags, and an environment created by naming it); concurrency (one pending run per group); the contexts available to each key (`vars` in a job's `if`, `steps` in an environment's `url`); and publishing to the Container registry with `GITHUB_TOKEN`, linked to the repository by the `org.opencontainers.image.source` label.
- `docker buildx imagetools create`: docs.docker.com/reference/cli/docker/buildx/imagetools/create/.
