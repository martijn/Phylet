<img src="docs/phylet.svg" alt="Phylet logo" width="350" />

# Phylet Music Server

Phylet is a audio-only UPnP/DLNA server, designed for audiophiles
or music lovers that want to stream a music library from their
NAS or PC to supported Digital Media Players.

Supported audio formats are: FLAC, MP3, M4A (AAC and ALAC), OGG, WAV, and AIFF.
Phylet reads tags and album art into an internal database, so your files and
directory structure will not be altered. Untagged files are presented under the
"Files" tree in library structure.

Phylet has been tested with Lyngdorf Amplifiers, Samsung TV's, and VLC Media
Player.

## Run With Docker

This is the preferred way to run Phylet, for example as a TrueNAS App. Host
networking must be used to allow for the multicast traffic to reach you
clients. Phylet serves files and a small dashboard over HTTP on port 5128.

Be aware that the multicast traffic might not work on Docker runtimes that use
a virtual machine, such as those on macOS and Windows.

### docker run

```bash
docker pull ghcr.io/martijn/phylet:latest
docker run --rm \
  --network host \
  -v /srv/media:/media:ro \
  -v phylet-data:/data \
  ghcr.io/martijn/phylet:latest
```

### Docker Compose

Mount your media folder read-only under /media, and a Docker volume for data to
persist library metadata and configuration.

```yaml
services:
  phylet:
    image: ghcr.io/martijn/phylet:latest
    network_mode: host
    restart: unless-stopped
    volumes:
      - /srv/media:/media:ro
      - phylet-data:/data

volumes:
  phylet-data:
```

## Run in development

Requirements:

- .NET 10 SDK

By default, the app listens on `http://*:5128` in every environment and serves files from the `Phylet/Mediatest` directory. Some configuration can be set using environment variables:

```bash
Storage__MediaPath=/srv/media \
Storage__DatabasePath=/srv/phylet/phylet.db \
dotnet run
```

If you omit `Storage__DatabasePath`, Phylet stores SQLite data in the OS app-data location:

- macOS: `~/Library/Application Support/Phylet/phylet.db`
- Linux: `$XDG_DATA_HOME/Phylet/phylet.db` or `~/.local/share/Phylet/phylet.db`
- Windows: `%LocalAppData%\\Phylet\\phylet.db`

If you omit `Storage__MediaPath` outside Development, startup fails with a configuration error.

## Configuration

There isn't much to configure yet, but you can change the announced server
name by modyfing the SQLite database:

```
$ sqlite3 /my-data-dir/phylet.db
sqlite> update DeviceConfigurations set Value='<new name>' where Key='device.friendly_name';
```
