# logistic.LicenseServer — Docker deployment

Tiny ASP.NET Core minimal API that backs the 30-day trial. Stores one SQLite
file (`/data/licenses.db`), serves `/v1/activate` and `/v1/heartbeat`, and signs
every successful response with an Ed25519 key the client embeds.

The client refuses to launch unless this server says the token is valid.
**Keep it up, behind HTTPS.**

The container ships with both the server and the `license-admin` CLI baked in,
so all day-to-day token management happens via `docker compose`.

Two deployment paths are documented:

- [**A. Plain Docker Compose**](#a-plain-docker-compose) — manual `docker compose up`, you handle TLS yourself.
- [**B. Dokploy on Pi5**](#b-dokploy-on-pi5) — let Dokploy + its built-in Traefik handle deploys, env vars, and HTTPS for you. ✨ Recommended.

---

## A. Plain Docker Compose

### 1. Build the image (on the Pi5)

```bash
git clone <this repo> /opt/logistic-license
cd /opt/logistic-license
docker compose build
```

(Building on the Pi gives a native arm64 image. To cross-build from your laptop:
`docker buildx build --platform linux/arm64 -f logistic.LicenseServer/Dockerfile .`)

### 2. Generate the signing keypair (once)

```bash
docker compose run --rm license-admin init-keys
```

You get two values:

```
# Public key (embed in client LicenseManager.cs):
<public-key>

# Private key (set on the server as env var):
export LICENSE_SERVER_SIGNING_KEY=<private-key>
```

- **Public key** → paste into `logistic/Services/LicenseConfig.cs` →
  `ServerPublicKeyBase64` before publishing each client build.
- **Private key** → put in a `.env` file next to `docker-compose.yml`:

```bash
cat > .env <<'EOF'
LICENSE_SERVER_SIGNING_KEY=<paste-private-key-here>
EOF
chmod 600 .env
```

### 3. Start the server

```bash
docker compose up -d license-server
docker compose ps
curl http://127.0.0.1:5080/healthz   # → OK
```

The container binds to `127.0.0.1:5080` only — **do not** expose port 5080 to
the internet directly; it has no TLS. Put it behind one of:

#### Cloudflare Tunnel (no port-forwarding)

Run cloudflared on the host (not in the same container):

```bash
curl -L --output cloudflared.deb https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-arm64.deb
sudo dpkg -i cloudflared.deb
cloudflared tunnel login
cloudflared tunnel create logistic-license

# ~/.cloudflared/config.yml:
#   tunnel: <tunnel-id>
#   credentials-file: /home/<you>/.cloudflared/<tunnel-id>.json
#   ingress:
#     - hostname: license.yourdomain.com
#       service: http://127.0.0.1:5080
#     - service: http_status:404

cloudflared tunnel route dns logistic-license license.yourdomain.com
sudo cloudflared service install
```

Your activation URL is now `https://license.yourdomain.com`. Paste it into
`logistic/Services/LicenseConfig.cs` → `ServerUrl` before publishing clients.

#### Caddy reverse proxy (if you have a static IP + DNS)

```
license.yourdomain.com {
    reverse_proxy 127.0.0.1:5080
}
```

`sudo systemctl restart caddy` — Caddy gets a Let's Encrypt cert automatically.

### 4. Issue, list, revoke tokens

```bash
docker compose run --rm license-admin mint --client "Acme Co." --days 30
docker compose run --rm license-admin list
docker compose run --rm license-admin show tr_xxx
docker compose run --rm license-admin revoke tr_xxx
```

The `license-admin` container mounts the same `license-data` volume as the
server, so everything stays in sync.

### 5. Health check & uptime monitoring

```bash
curl https://license.yourdomain.com/healthz   # → OK
```

Add this URL to your uptime monitor (UptimeRobot, BetterUptime, etc.) — if the
server goes down, **no client can launch the app**.

Compose itself runs a health probe every 30 s; `docker compose ps` shows it.

### 6. Backups

The SQLite file lives in the named volume `license-data`. Back it up with:

```bash
docker run --rm -v logistic-project_license-data:/data -v $PWD:/backup alpine \
    cp /data/licenses.db /backup/licenses.db.$(date +%F)
```

(Replace `logistic-project_license-data` with whatever `docker volume ls` shows.)

### 7. Upgrading

```bash
git pull
docker compose build
docker compose up -d license-server
```

The volume survives rebuilds; existing tokens keep working.

---

## B. Dokploy on Pi5

[Dokploy](https://dokploy.com) is a self-hosted PaaS that reads the same
`docker-compose.yml` you already have, then handles deploys, env vars, TLS,
and reverse-proxying through its built-in Traefik. Much less manual setup than
path A.

**Pre-req**: Dokploy installed on the Pi5 (the one-line installer takes ~5 min,
see <https://docs.dokploy.com/docs/core/installation>) and your repo pushed
somewhere Dokploy can pull from (GitHub, Gitea, or any git URL).

The whole flow is **9 steps, ~20 min** end-to-end. After this, all day-to-day
token management happens in your browser via `/admin` — no SSH needed.

### 1. Generate the signing keypair (locally)

`init-keys` just prints the keypair — it doesn't touch any database — so the
cleanest place to run it is your dev Mac, **before** pushing to git:

```bash
# In the repo root on your Mac
docker run --rm --entrypoint license-admin \
    logistic-license:latest init-keys
```

(If you haven't built the image locally yet, swap in:
`docker build -f logistic.LicenseServer/Dockerfile -t logistic-license:latest .`)

Save the two values it prints:
- **Public key** → step 2 below
- **Private key** → step 4 below

### 2. Bake the public key into the client

Edit [`logistic/Services/LicenseConfig.cs`](../logistic/Services/LicenseConfig.cs)
and set both constants for the production build:

```csharp
internal const string ServerUrl = "https://license.yourdomain.com";
internal const string ServerPublicKeyBase64 = "<paste public key from step 1>";
```

Commit and push. Every client `.exe` / `.app` you publish from here on will
have this URL + key embedded.

### 3. Pick a strong admin password

You'll need it in step 4 to log into the web admin UI. Generate one:

```bash
openssl rand -base64 24
```

Save the output — you'll paste it into Dokploy and use it to log in.

### 4. Create the Compose application in Dokploy

In the Dokploy web UI:

1. **Projects → Create project** (e.g. "logistic-trial")
2. **Create service → Compose**
3. **Source**: connect your git repo (GitHub OAuth, or paste a Gitea URL + token)
4. **Compose Path**: `docker-compose.yml` (the default)
5. **Environment** tab — paste:
   ```
   LICENSE_SERVER_SIGNING_KEY=<private key from step 1>
   LICENSE_SERVER_ADMIN_PASSWORD=<password from step 3>
   ```
   (Optionally `LICENSE_SERVER_ADMIN_USER=<custom-user>` — default is `admin`.)
6. Click **Deploy**. First build takes 5–10 min on a Pi5; subsequent rebuilds
   are cached and finish in seconds.

> The `.env` file in the repo is **ignored** by Dokploy — env vars come from
> the UI you just filled in. `${LICENSE_SERVER_SIGNING_KEY:?...}` in
> `docker-compose.yml` reads whatever Dokploy hands it at runtime.

### 5. Add the domain + HTTPS

In the service's **Domains** tab:

1. **Add Domain** → `license.yourdomain.com`
2. **Container Port**: `5080`
3. **HTTPS**: on
4. **Certificate**: Let's Encrypt
5. Save.

Point a DNS `A` record for `license.yourdomain.com` at the Pi5's public IP
(or use Cloudflare proxy). Dokploy + Traefik request the cert on first request.

### 6. Verify

```bash
curl https://license.yourdomain.com/healthz   # → OK
```

Then open `https://license.yourdomain.com/admin` in your browser. Login prompt
appears → use `admin` + the password from step 3. You should see an empty
token list with the "+ ออกโทเค็นใหม่" form.

### 7. Mint tokens (everyday workflow)

Either from the web UI (`/admin` → fill form → copy token from the success
page) — recommended — or from a terminal if you prefer the CLI:

**Dokploy → service → Terminal** tab (in the browser):
```bash
license-admin mint --client "Acme" --days 30
license-admin list
license-admin revoke tr_xxx
```

**SSH on the Pi5**:
```bash
docker exec -it $(docker ps -qf name=license-server) \
    license-admin mint --client "Acme" --days 30
```

All three (web UI, Terminal, SSH) share the same `license-data` volume, so
edits made in one are visible everywhere instantly.

### 8. Send token + client app to your customer

1. On your Mac, run `./scripts/publish/publish-win.ps1` or
   `./scripts/publish/publish-osx.sh both` to produce the single-file binary.
2. Send the customer:
   - The binary (`logistic.exe` or `Logistic.app.zip`)
   - The token (`tr_…`) you minted via the admin UI
   - [`scripts/publish/CLIENT-INSTALL.md`](../scripts/publish/CLIENT-INSTALL.md)
     (Thai install guide with SmartScreen/Gatekeeper bypass)

### 9. Maintenance

- **Push code changes** → Dokploy auto-redeploys (or click Deploy manually).
  The `license-data` volume survives rebuilds, so all tokens keep working.
- **Logs**: Dokploy → service → **Logs** tab.
- **Backups**: Dokploy has a **Backups** tab for the `license-data` volume —
  schedule a daily snapshot to S3/local.
- **Uptime monitoring**: add `https://license.yourdomain.com/healthz` to
  UptimeRobot / BetterUptime. If the server dies, **no client can launch the
  app** — you want to know within minutes, not days.

### Common gotchas

- **Forgot to set `LICENSE_SERVER_ADMIN_PASSWORD`** → `/admin` returns
  `503 Admin disabled`. Set it in Dokploy → Environment, save, the
  container restarts.
- **`/admin` returns 502 after deploy** → first build can take ≥5 min on Pi5;
  watch the Logs tab. The server only starts listening after EF Core finishes
  `EnsureCreated` on the SQLite file.
- **Changed the signing key** → all existing tokens stop working (the client
  has the OLD public key embedded). Treat key rotation as "publish a new
  client build + re-mint every token."
- **Pi5 reboots / SD card dies** → restore the `license-data` volume from
  backups before redeploying, otherwise the SQLite file is empty and every
  client gets `unknown_token`.

### 6. Health, logs, backups

- **Logs**: Dokploy → service → **Logs** tab
- **Health**: Dokploy → service → **Monitoring** (uses the same `/healthz`
  probe defined in the compose file)
- **Backups**: Dokploy has a built-in **Backups** tab for compose volumes.
  Schedule a daily backup of `license-data` to S3/local. Or just `docker cp`
  the file off the Pi periodically.

### 7. Updating

Push to git → in Dokploy click **Deploy** (or enable auto-deploy on push).
The named volume `license-data` is preserved across deploys, so existing
tokens keep working.

### Notes & gotchas

- The compose file binds `127.0.0.1:5080` on the host. Dokploy's Traefik talks
  to the container over Docker's internal network, so this binding is harmless
  but redundant — feel free to delete the `ports:` block in
  `docker-compose.yml` if you want it gone (Dokploy doesn't need it).
- If you ever change the signing key, **all existing tokens stop working**
  (the client still has the old public key embedded). Treat key rotation as
  "issue a new client build + re-mint every token."
- Pi5 must stay online — if it dies, **no client can launch the app**. Consider
  a UPS and external uptime monitoring of `https://license.yourdomain.com/healthz`.

---

## Web admin UI

The server includes a built-in HTML admin page at **`/admin`** for managing
tokens from a browser — handy when you don't want to SSH into the Pi just to
mint a token.

### Enabling it

Set `LICENSE_SERVER_ADMIN_PASSWORD` (and optionally `LICENSE_SERVER_ADMIN_USER`,
defaults to `admin`) in your environment. Without the password, `/admin`
returns `503 Admin disabled` — i.e. it's **opt-in**, never accessible by accident.

**Plain Docker Compose**:

```bash
# Append to .env (next to docker-compose.yml)
echo "LICENSE_SERVER_ADMIN_PASSWORD=$(openssl rand -base64 24)" >> .env
docker compose up -d --force-recreate license-server
```

**Dokploy**: in the service's **Environment** tab, add:

```
LICENSE_SERVER_ADMIN_PASSWORD=<strong-password-here>
```

Save → Dokploy redeploys automatically.

### Using it

Open `https://license.yourdomain.com/admin` (or `http://127.0.0.1:5080/admin`
for local testing). Browser prompts for the username + password you set.

The page lets you:
- See every token at a glance — client name, expiry, status (active / เหลือ N วัน /
  หมดอายุ / ยกเลิกแล้ว), machine binding, last seen.
- **+ ออกโทเค็นใหม่** — fill client name + days, click button → success page
  shows the new token in a big copy-friendly box. (Copy it before navigating
  away; the token is only shown once.)
- **ยกเลิก** — revoke a token immediately. Confirmation dialog before it acts.

The same SQLite file backs both the web UI and the `license-admin` CLI, so
changes you make in the browser are visible to CLI commands instantly (and
vice-versa).

### Security notes

- Uses HTTP Basic Auth — **only safe behind HTTPS** (Cloudflare Tunnel /
  Caddy / Dokploy's Traefik all give you this automatically). Don't expose
  port 5080 directly to the internet.
- No CSRF tokens. Acceptable here because: there's only ever one admin user,
  Basic-Auth credentials aren't auto-sent cross-origin, and the worst case
  is the admin themselves accidentally revoking a token (which they can
  re-mint).
- No edit/extend — to give a customer more days, just `mint` a new token and
  `revoke` the old one. Simpler than tracking partial-state edits.

---

## Endpoints (for reference)

| Path | Auth | Body | Success | Failure |
|---|---|---|---|---|
| `POST /v1/activate` | none | `{token, machineId}` | `{expiresAt, serverNow, signature}` | 404 unknown_token, 409 wrong_machine, 410 expired/revoked |
| `POST /v1/heartbeat` | none | `{token, machineId}` | same as activate | same as activate |
| `GET /healthz` | none | — | `"OK"` (text) | — |
| `GET /admin` | Basic Auth | — | HTML list | 401 / 503 if password unset |
| `POST /admin/mint` | Basic Auth | form: `clientName`, `days` | HTML success page (one-shot token) | redirect with flash error |
| `POST /admin/revoke` | Basic Auth | form: `token` | redirect to list | redirect with flash error |

The `signature` is Ed25519 over a canonical JSON string of
`{expiresAt, machineId, serverNow, tokenId}` (keys sorted alphabetically, UTF-8).
The client refuses any response that doesn't verify against the embedded public key.
