# neuroncodec.com

The NeuronCodec public site and its admin dashboard: a small ASP.NET Core Razor Pages app over
SQLite, built to the "Organic" design system.

- **Public site** — home, articles index (paginated), article pages, about, projects, plus
  `sitemap.xml`, `robots.txt` and an RSS feed.
- **Admin** — markdown article editor with live preview, a media library, categories and tags,
  per-article and site-wide SEO, password management and TOTP two-factor authentication.

## Stack

| | |
|---|---|
| Runtime | .NET 10 / ASP.NET Core Razor Pages |
| Database | SQLite via EF Core (migrations applied at startup) |
| Markdown | Markdig, sanitised with HtmlSanitizer |
| Editor | EasyMDE + highlight.js + marked + DOMPurify, vendored via LibMan |
| Auth | Cookie auth, PBKDF2-HMACSHA256 passwords, RFC 6238 TOTP |
| Versioning | GitVersion → `AssemblySemVer` → Docker image tag |

## Local development

Needs the .NET 10 SDK.

```bash
dotnet restore
dotnet run --project src/NeuronCodec.Web
```

The site comes up on <http://localhost:5226>. On first run it creates the database, applies
migrations, and seeds an admin account.

In `Development` with no password configured it seeds `admin` / `ChangeMe!Dev123` and forces a
password change at first sign-in. Outside Development it refuses to start unless
`NeuronCodec__Admin__Password` is set.

Run the tests with:

```bash
dotnet test
```

Local runtime state (SQLite file, Data Protection keys, uploaded media) lives under
`src/NeuronCodec.Web/.runtime/` and is gitignored. Delete that folder to start clean.

To refresh the vendored editor libraries after editing `libman.json`:

```bash
dotnet tool install --global Microsoft.Web.LibraryManager.Cli
libman restore
```

## Docker Compose

Create a `.env` beside `docker-compose.yml`:

```bash
NCODEC_ADMIN_PASSWORD=choose-a-strong-bootstrap-password
NCODEC_BASE_URL=https://neuroncodec.com
NCODEC_PORT=8080
```

Then:

```bash
docker compose up -d --build
```

The site is on <http://localhost:8080>; the admin is at `/admin`. Sign in with the seeded
credentials — you will be required to change the password before anything else is reachable.

Two named volumes keep state across rebuilds:

| Volume | Mount | Holds |
|---|---|---|
| `ncodec-data` | `/app/data` | `neuroncodec.db` and the Data Protection key ring |
| `ncodec-uploads` | `/app/uploads` | Uploaded media, served read-only at `/uploads` |

`docker compose up --build` after a code change reuses both, so content and sessions survive.

> Losing `ncodec-data` signs every session out **and** makes any stored TOTP secret
> unreadable — the key ring that protects it lives there. Back it up with the database.

#### The Data Protection key ring

`/app/data/keys/` holds the private keys that encrypt auth cookies and the stored TOTP secret.
Treat it as a secret:

- **Back it up with the database.** Restoring `neuroncodec.db` without its key ring leaves the
  TOTP secret undecryptable — you would have to sign in with a recovery code and re-enrol.
- **Never commit it.** `.gitignore` excludes `**/keys/key-*.xml` and `**/.runtime/`, which is
  where local runs write theirs. A key that reaches a public repo is compromised: delete the
  directory so a fresh ring is generated, then re-enrol two-factor.
- **Do not share one ring between environments.** Staging and production should each keep their
  own volume.

To run a published image instead of building locally:

```bash
NCODEC_IMAGE=ghcr.io/neuroncodec/neuroncodec.com:0.1.0.0 docker compose up -d
```

### Behind a reverse proxy

The container serves plain HTTP on `8080` and honours `X-Forwarded-For` / `X-Forwarded-Proto`,
so terminate TLS at your proxy and set `NCODEC_BASE_URL` to the public origin — that value is
what canonical links, the sitemap and share tags use.

### Behind a Cloudflare Tunnel

Cloudflare terminates TLS and forwards plain HTTP to the container, so the origin never sees an
`https://` request. **`NCODEC_BASE_URL` is not optional in this setup**: without it every
canonical URL, `<loc>` in the sitemap and `og:url` would advertise the internal hostname the
tunnel connected to.

```bash
NCODEC_BASE_URL=https://neuroncodec.com
```

Point the tunnel at the service over the compose network rather than a published port, and drop
the `ports:` mapping so the origin is not reachable except through Cloudflare:

```yaml
  cloudflared:
    image: cloudflare/cloudflared:latest
    restart: unless-stopped
    command: tunnel --no-autoupdate run
    environment:
      TUNNEL_TOKEN: ${CLOUDFLARE_TUNNEL_TOKEN:?set CLOUDFLARE_TUNNEL_TOKEN in .env}
    depends_on:
      - web
```

With the tunnel's public hostname routed to `http://web:8080`. Two things to check on the
Cloudflare side:

- **Pick one canonical hostname.** Redirect `www` to the apex (or the reverse) with a redirect
  rule. The app always emits `NCODEC_BASE_URL` in its canonical tag, so search engines settle on
  one address either way, but a redirect avoids serving the same page on two hostnames.
- **Do not enable Cloudflare's HTML minification or Rocket Loader on `/admin`.** The editor
  mounts against specific element ids and Rocket Loader defers scripts in a way that breaks it.

## Environment variables

Configuration binds from `appsettings.json`, overridden by environment variables. Nested keys
use a double underscore.

| Variable | Default | Purpose |
|---|---|---|
| `NeuronCodec__Admin__Username` | `admin` | Username for the account seeded on an empty database |
| `NeuronCodec__Admin__Password` | — | Bootstrap password. **Required** outside Development. Must be changed at first sign-in |
| `NeuronCodec__Site__BaseUrl` | request origin | Absolute origin for canonical URLs, sitemap and OG tags |
| `NeuronCodec__Site__Name` | `NeuronCodec` | Site name in titles and share tags |
| `NeuronCodec__Site__Tagline` | `open-source foundation` | Footer tagline |
| `NeuronCodec__DataPath` | `/app/data` | Directory for the SQLite file and Data Protection keys |
| `NeuronCodec__Uploads__Path` | `/app/uploads` | Directory for uploaded media |
| `NeuronCodec__Uploads__MaxBytes` | `26214400` | Per-file upload limit (25 MB) |
| `ConnectionStrings__Default` | derived from `DataPath` | EF Core SQLite connection string |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Listen port inside the container |

Anything set through Admin → SEO is stored in the database and takes precedence over the
`Site` values above.

## Admin

`/admin`, cookie-authenticated, single account.

- **Articles** — full CRUD, draft / published / scheduled, slug editing, categories, tags,
  featured image, reading-time override, duplicate-to-draft, and per-article SEO. A scheduled
  article becomes visible the moment its date passes; a background sweep flips its stored status
  a minute later, so publication never depends on that job running.
- **Media** — drag-and-drop upload, rename, alt text, copy URL, delete. Deleting is refused
  while an article still references the file. The same library backs the picker in the editor.
- **SEO** — split into Identity (name, tagline, canonical base URL, default description),
  Sharing (default share image, Twitter handles), Structured data (organisation details and
  `sameAs` profile URLs) and Crawling (sitemap toggle, `robots.txt` additions). Articles emit
  `BlogPosting` and `BreadcrumbList` JSON-LD plus `article:published_time` / `article:modified_time`;
  the home page emits `WebSite` and `Organization`.
- **Security** — split into Password, Two-factor (QR enrolment) and Recovery codes (ten
  single-use codes).

Sections with more than one concern are split into sub-pages, and the sidebar groups them into
collapsible sections built on `<details>` — so they fold without script and the group holding
the current page opens automatically.

Drafts and not-yet-due scheduled articles return 404 to the public but stay reachable for a
signed-in admin, which is what the editor's Preview link uses.

### Security notes

- Passwords are PBKDF2-HMACSHA256, 210,000 iterations, per-password salt.
- The TOTP secret is encrypted at rest with ASP.NET Data Protection; recovery codes are hashed.
- Codes are single-use within their window, and eight failed sign-ins lock the account for
  15 minutes.
- Changing the password or altering 2FA bumps a security stamp that invalidates every other
  session.
- Rendered markdown is sanitised, and uploads are served with `nosniff` and a `sandbox` CSP.

## CI/CD

`.github/workflows/ci.yml` runs on pushes to `master`, on tags, and on pull requests:

1. **version** — runs GitVersion and exposes `AssemblySemVer`.
2. **build** — restores, builds and tests at that version.
3. **docker** — builds the image and pushes it to `ghcr.io/neuroncodec/neuroncodec.com`,
   tagged with `AssemblySemVer`, a short SHA, and `latest` on the default branch.

Pull requests build the image but never publish it. Version bumps follow Conventional Commits:
`feat:` bumps the minor, most other types bump the patch, and `!` or a `BREAKING CHANGE` footer
bumps the major.

## Layout

```
src/NeuronCodec.Web/
  Data/            EF Core context, entities and migrations
  Services/        Auth, TOTP, markdown, media, SEO, slugs, startup tasks
  Pages/           Razor Pages — public site at the root, admin under /Admin
  wwwroot/
    ds/styles.css  The Organic design system, unmodified
    css/site.css   Public-site classes lifted from the design's inline styles
    css/admin.css  Admin layer, same tokens
    lib/           Vendored editor libraries (libman.json)
tests/             Unit tests for TOTP, hashing, slugs, markdown and visibility rules
ncodec/            The original design files this was built from
```

`wwwroot/ds/styles.css` is the design system's own file and should be replaced wholesale rather
than edited; everything else takes its colours, spacing and radii from that file's variables.

## License

MIT — see [LICENSE](LICENSE).
