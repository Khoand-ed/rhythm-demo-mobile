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
| `scripts/` | everything run on the VM - see below |
| `local/` | the same stack on Docker Desktop, for rehearsing a change before it reaches the VM |

Every pull request that touches this folder runs `deploy-checks`: shellcheck on the scripts, `docker
compose config` on both compose files, and each service's own validator on its configuration -
`caddy validate`, `alloy fmt`, `promtool check config`, `loki -verify-config` - at the versions the
compose files pin.

On the VM, `/srv/promuse` holds what is not in the repository:

| Path | What |
|---|---|
| `deploy/` | a copy of this folder - the scripts run from here |
| `edge/.env`, `staging/.env`, `production/.env` | the configuration and secrets each project runs with, readable by `deploy` only |
| `backups/<environment>/` | seven days of nightly backups |
| `metrics/` | what the scripts tell Alloy: last backup, what is deployed |

---

## First time

Some steps need your accounts; the scripts do the rest.

### 1. The accounts

- **Azure VM.**
  - Ubuntu Server 24.04 LTS, x64, size B2s (2 vCPU, 4 GB) or larger, a 64 GB disk, a static public IP.
  - In its network security group, allow 22 (from your own IP only), 80 and 443.
  - Add a budget alert to the subscription. The VM costs about 30-40 USD a month, so student credit lasts two to three months. *Stop (deallocate)* it when no one needs it.
- **DuckDNS** (duckdns.org).
  - Create three subdomains: `<name>-staging`, `<name>`, `<name>-grafana`.
  - Note the account's token.
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

### 4. The edge

Generate the two secrets the edge needs (hex: the scripts write them unquoted). Then start it once as `deploy`:

```bash
openssl rand -hex 24      # GRAFANA_ADMIN_PASSWORD
openssl rand -hex 24      # MONITOR_DB_PASSWORD

STAGING_HOST=<name>-staging.duckdns.org \
PRODUCTION_HOST=<name>.duckdns.org \
GRAFANA_HOST=<name>-grafana.duckdns.org \
ACME_EMAIL=<your email, for Let's Encrypt> \
GRAFANA_ADMIN_PASSWORD=<...> \
DISCORD_WEBHOOK_URL=<...> \
MONITOR_DB_PASSWORD=<...> \
  /srv/promuse/deploy/scripts/edge.sh
```

It writes them to `/srv/promuse/edge/.env`. Later runs need no variables: they reuse that file.

DuckDNS reads the same file. Add the two lines by hand, with an editor - never with `echo`, which keeps the token in the shell's history:

```
DUCKDNS_DOMAINS=<name>-staging,<name>,<name>-grafana
DUCKDNS_TOKEN=<the token>
```

Then run `scripts/duckdns-update.sh` once. Its timer repeats it every five minutes. Caddy obtains the certificates as soon as the names point at the VM.

### 5. The first deploy

The pipeline (next module) builds and pushes the image. Until it exists, build one on the VM from a clone of the repository:

```bash
docker build -f deploy/docker/api.Dockerfile -t promuse-api:manual .
DB_PASSWORD=$(openssl rand -hex 24) JWT_SIGNING_KEY=$(openssl rand -hex 32) \
  /srv/promuse/deploy/scripts/deploy.sh staging promuse-api:manual
```

Each environment has its own `DB_PASSWORD` and `JWT_SIGNING_KEY`. They are written into `/srv/promuse/<environment>/.env` and reused by every later deploy.

> **Do not lose them.** Postgres keeps the password it was first started with, so a new one locks the API out of the database. A new signing key signs every player out.

### 6. The game

Point a build at the VM:
1. Type `https://<name>-staging.duckdns.org` and `https://<name>.duckdns.org` into `Assets/Promuse.Net/Resources/ServerTarget.asset`, and commit them.
2. Choose *Arknights/Build/Server: Staging* (or *Production*).
3. Build: `Promuse-<version>-staging-dev.apk` plays over the Internet, from any network.

The login screen shows `STAGING · v<version>` on every build that is not production (SETUP: *Which server the game talks to*).

---

## Every day

### Deploy, redeploy, roll back

```bash
/srv/promuse/deploy/scripts/deploy.sh staging ghcr.io/<owner>/promuse-api:sha-<commit>
```

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

The dashboards, datasources and alert rules are files under `monitoring/grafana/`. Change them there and run `edge.sh` - an edit made in Grafana is not kept.

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
- **Secrets live in `/srv/promuse/*/.env` (mode 600) and, with the pipeline, in GitHub.** Never in this repository. Generate them as hex.
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
