# Deploy runbook (homelab)

One-time setup of the Debian VM `source` (`vm.example.lan`) so that pushes to
`main`/`release` auto-deploy via the GitHub Actions workflow
(`.github/workflows/deploy.yml`). Run everything below **on the VM** as a normal
sudo-capable user.

## 1. Docker present

```bash
docker version            # daemon reachable?
docker compose version    # v2 compose plugin present?
```

If missing, install Docker Engine + the compose plugin (get.docker.com), then:

```bash
sudo usermod -aG docker "$USER"   # run docker without sudo
# log out / back in for the group to take effect
```

## 2. Runtime env files (hold the real secrets, never in git)

```bash
sudo mkdir -p /opt/thosedays
sudo chown "$USER" /opt/thosedays
```

Create `/opt/thosedays/.env.staging` and `/opt/thosedays/.env.prod` using the
committed templates in `deploy/.env.staging.example` / `.env.prod.example` as the
shape. Fill in **real** DB passwords. The workflow references these absolute
paths. Minimum contents:

```
# /opt/thosedays/.env.staging
COMPOSE_PROJECT_NAME=thosedays-staging
APP_IMAGE=ghcr.io/ofbirds/thosedaysapp:staging
APP_PORT=9123
DB_HOST_PORT=5433
DB_NAME=thosedays
DB_USER=thosedays
DB_PASSWORD=<real-staging-password>
```

```
# /opt/thosedays/.env.prod
COMPOSE_PROJECT_NAME=thosedays-prod
APP_IMAGE=ghcr.io/ofbirds/thosedaysapp:prod
APP_PORT=9124
DB_HOST_PORT=5434
DB_NAME=thosedays
DB_USER=thosedays
DB_PASSWORD=<real-prod-password>
```

```bash
chmod 600 /opt/thosedays/.env.*
```

## 3. Self-hosted GitHub Actions runner

On GitHub: **repo → Settings → Actions → Runners → New self-hosted runner →
Linux / x64**. That page shows a download block and a `./config.sh` line with a
**registration token** (short-lived). Use that page's exact version/token, but
with the customizations below.

```bash
mkdir -p ~/actions-runner && cd ~/actions-runner
# (use the download/extract lines GitHub shows for the current runner version)

# Configure — note the custom label `source`, which the workflow targets:
./config.sh \
  --url https://github.com/OfBirds/ThoseDaysApp \
  --token <REGISTRATION_TOKEN_FROM_GITHUB> \
  --name source \
  --labels source \
  --unattended

# Install + start as a systemd service so it survives reboots:
sudo ./svc.sh install
sudo ./svc.sh start
sudo ./svc.sh status
```

The runner connects outbound to GitHub — no inbound ports opened.

> The runner runs `docker compose`, so the runner's user must be in the `docker`
> group (step 1). If you installed the service before adding the group, restart
> it: `sudo ./svc.sh stop && sudo ./svc.sh start`.

## 4. First deploy

Open a PR against `main` → `validate.yml` builds the image, pushes to GHCR, the
runner pulls, migrates, and brings up the staging stack. Then verify on the LAN:

- staging app:  `http://vm.example.lan:9123/`
- staging DB (DBeaver): `vm.example.lan:5433`

Merge the PR → `release.yml` does the same for prod (there is no `release` branch;
merging to `main` is the release):

- prod app: `http://vm.example.lan:9124/`
- prod DB (DBeaver): `vm.example.lan:5434`

## Day-to-day

- Deploys are automatic: PRs targeting `main` → staging, pushes to `main` → prod.
  All PRs share the one staging stack, so validate runs are serialized; if GitHub
  cancels a superseded queued run, re-run it from the Actions tab.
- Manual stack control on the VM:
  ```bash
  cd <repo>/deploy   # or wherever the runner checked it out
  docker compose --env-file /opt/thosedays/.env.staging ps
  docker compose --env-file /opt/thosedays/.env.staging logs -f app
  ```
- Images are tagged `:staging`/`:prod` (moving) and `:<sha>` (immutable) in GHCR,
  so rollback = point the env file's `APP_IMAGE` at a known `:<sha>` and re-run
  `up -d`.
