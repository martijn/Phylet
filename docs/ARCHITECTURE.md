# Phylet Architecture

## Overview

Phylet is a `.NET 10` solution with two primary runtime projects:

- `Phylet`
  - ASP.NET Core host
  - SSDP, UPnP/DLNA controllers, DIDL generation, eventing, and media HTTP endpoints
- `Phylet.Data`
  - SQLite persistence
  - runtime device configuration loading
  - media library scanning and browse/query model

The host owns protocol behavior. The data project owns persisted state and the media catalog.

The repository also includes two test projects:

- `Phylet.Tests`
- `Phylet.Data.Tests`

## Startup Flow

Startup stays explicit in `Program.cs`:

1. Bind `DlnaOptions`
2. Register `Phylet.Data`
3. Build the web app
4. Map controllers
5. Run `InitializePhyletAsync()`
6. Start the host and hosted services

`LibraryScanService` is registered as a hosted background service. Its initial scan is queued internally at service startup rather than being invoked directly from `Program.cs`.

`InitializePhyletAsync()` performs:

1. device configuration initialization
   - apply migrations
   - load or seed persisted device UUID and friendly name
   - publish the runtime device configuration snapshot

The background `LibraryScanService` performs:

1. initial library scan after the host starts
   - scan the configured media root
   - update the persisted SQLite catalog
2. serialized future rescan requests
   - coalesce repeated requests while a scan is already queued or running

If the configured storage paths are invalid, startup fails. If individual media files are unreadable, the scan logs a warning and continues.

## Configuration And Storage

- Mutable device identity lives in SQLite.
- `Dlna:Manufacturer`, `Dlna:ModelName`, and subscription timeout remain in appsettings.
- `Storage:DatabasePath` controls the SQLite file location.
- `Storage:MediaPath` controls the media root.
- In Development, `Storage:MediaPath` defaults to `Phylet/MediaTest` when unset.
- In Docker, the image defaults are `/data/phylet.db` and `/media`.

SQLite is the source of truth for:

- device UUID and friendly name
- scanned media library entities
- browse ids used by the DLNA layer

## CI And Container Publishing

GitHub Actions workflow `.github/workflows/docker-publish.yml` currently:

1. restores the solution
2. runs `dotnet test --configuration Release --no-restore` from the repository root
3. builds a multi-platform container image for `linux/amd64` and `linux/arm64`
4. publishes to `ghcr.io/<owner>/phylet` on pushes to `main` and tags matching `v*`

Pull requests run the test and Docker build path but do not publish an image.

## DLNA Browse Model

The root containers are:

- `artists`
- `albums`
- `files`

Object ids are stable string wrappers over database ids:

- `artist:{id}`
- `album:{id}`
- `folder:{id}`
- `track:{id}`
- `file-track:{id}`

`track:{id}` is used for album browsing, while `file-track:{id}` is used in the filesystem-style `Files` tree so the same track can appear under different parents without ambiguous parent ids.

## Media Library Model

The scanner currently imports:

- MP3
- FLAC
- M4A
- OGG
- WAV
- AIFF / AIF

Tagged files participate in `Artists` and `Albums`. Supported audio files without usable album metadata still appear in `Files`.

Track identity is path-led for now:

- unique key: relative path under the media root
- change detection: file size + last modified timestamp
- file moves/renames become new track ids

The scanner never modifies media files. It only reads metadata and persists catalog state to SQLite.
