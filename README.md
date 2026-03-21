<img src="docs/phylet.svg" alt="Phylet logo" width="350" />

# Phylet Music Server

Phylet is a audio-focused UPnP/DLNA audio server, designed for audiophiles
or music lovers that want to stream a music library from for example their
NAS to supported renderers.

Supported audio formats:

- FLAC
- MP3
- M4A
- OGG
- WAV
- AIFF / AIF

Phylet keeps track of your files in an internal database an reads tags and
album art. Your files and directory structure will not be touched.

Phylet has been tested with Lyngdorf Amplifiers, Samsung TV's, and VLC Media
Player.

## Run With Docker

This is the preferred way to run Phylet, for example as a TrueNAS App. Host networking must be used to allow for the multicast traffic to reach your clients. Phylet listens on port 5128 by default.

### Build

```bash
docker build -t phylet:latest .
```

### Run

```bash
docker run --rm \
  --network host \
  -v /srv/media:/media:ro \
  -v phylet-data:/data \
  phylet:latest
```

### Docker Compose

Mount your media folder read-only under /media, and a Docker volume for data to
persist library metadata and configuration.

```yaml
services:
  phylet:
    image: phylet:latest
    build:
      context: .
      dockerfile: Dockerfile
    container_name: phylet
    network_mode: host
    restart: unless-stopped
    volumes:
      - /srv/media:/media:ro
      - phylet-data:/data

volumes:
  phylet-data:
```

### Notes

- Use `network_mode: host`. Do not replace it with `ports:` mapping for DLNA use.
- SSDP uses UDP multicast on `239.255.255.250:1900`, and DLNA clients must also be able to reach the HTTP address Phylet advertises back to them.

## GitHub Actions And GHCR

The repository includes a workflow at `.github/workflows/docker-publish.yml` that:

- runs `dotnet test` on `Phylet.sln`
- builds a multi-platform container image for `linux/amd64` and `linux/arm64`
- publishes the image to `ghcr.io/martijn/phylet` on pushes to `main` and tags matching `v*`

Pull requests run the tests and validate the Docker build, but do not push an image.

### First-Time GitHub Setup

1. Create the GitHub repository at `https://github.com/martijn/Phylet`.
2. Push this repository to the `main` branch.
3. Ensure GitHub Actions is enabled for the repository.
4. After the first successful publish, open the `phylet` package under GitHub Packages and set its visibility to public if you want anonymous pulls.

No personal access token is required for this workflow. It uses the repository `GITHUB_TOKEN` with `packages: write` permission to publish to GHCR from the same repository.

Once the workflow has published at least once, pulls look like:

```bash
docker pull ghcr.io/martijn/phylet:latest
```

## Run A Published Local Copy

`Storage:MediaPath` is required outside Development. There is no production fallback to `Phylet/MediaTest`.

### Publish

With the .NET 10 SDK installed:

```bash
dotnet publish Phylet/Phylet.csproj -c Release -o ./publish
```

This produces a runnable app in `./publish`.

### Run

By default, the published app listens on `http://*:5128` in every environment.

Example on macOS or Linux:

```bash
Storage__MediaPath=/srv/media \
Storage__DatabasePath=/srv/phylet/phylet.db \
./publish/Phylet
```

Or run the published DLL with `dotnet`:

```bash
Storage__MediaPath=/srv/media \
Storage__DatabasePath=/srv/phylet/phylet.db \
dotnet ./publish/Phylet.dll
```

If you omit `Storage__DatabasePath`, Phylet stores SQLite data in the OS app-data location:

- macOS: `~/Library/Application Support/Phylet/phylet.db`
- Linux: `$XDG_DATA_HOME/Phylet/phylet.db` or `~/.local/share/Phylet/phylet.db`
- Windows: `%LocalAppData%\\Phylet\\phylet.db`

If you omit `Storage__MediaPath` outside Development, startup fails with a configuration error.

### Notes

- For DLNA discovery on a local machine, the host must be on the same LAN as the clients and allow SSDP multicast on `239.255.255.250:1900`.
- If you override `ASPNETCORE_URLS`, bind to an address reachable from the LAN so Phylet can advertise a usable HTTP address back to clients.
- `Phylet/MediaTest` is only a development fixture used when running with `ASPNETCORE_ENVIRONMENT=Development`.
