# Phylet Agent Notes

This file captures the current working shape of the app so future work can resume quickly.

## Current state (2026-03-21)

The `.NET 10` MVP is working end-to-end with:
- Samsung TV: discovery, browse, cover art, playback
- Lyngdorf amplifier: discovery, browse, playback

## Implemented architecture

Primary runtime projects:
- `Phylet`
  - Web API host and DLNA endpoints
- `Phylet.Data`
  - SQLite persistence, runtime device configuration, and current library service

Test projects:
- `Phylet.Tests`
  - host/controller coverage
- `Phylet.Data.Tests`
  - data/configuration/library coverage

Host-side components:
- `Services/SsdpService.cs`
  - SSDP listener + M-SEARCH responses
  - `NOTIFY ssdp:alive` and `ssdp:byebye`
  - Auto-resolves LAN-advertised base URL from active listen address and preferred outbound interface
- `Controllers/DeviceDescriptionController.cs`
  - `/description.xml`
- `Controllers/ContentDirectoryController.cs`
  - `/ContentDirectory/scpd.xml`
  - `/upnp/control/contentdirectory` SOAP actions
  - `/upnp/event/contentdirectory` SUBSCRIBE/UNSUBSCRIBE
- `Controllers/ConnectionManagerController.cs`
  - `/ConnectionManager/scpd.xml`
  - `/upnp/control/connectionmanager`
  - `/upnp/event/connectionmanager` SUBSCRIBE/UNSUBSCRIBE
- `Controllers/MediaController.cs`
  - `GET|HEAD /media/audio/{trackId}` with range support
  - `GET|HEAD /media/image/{albumId}` with range support
- `Services/DidlBuilder.cs`
  - DIDL-Lite browse responses
  - `protocolInfo` with DLNA fields
  - `res@size` when file exists
- `Services/EventSubscriptionService.cs`
  - basic SID/timeout handling for event subscriptions
- `Services/StartupDiagnosticsService.cs`
  - startup self-check logging for effective config + media presence

Data-side components:
- `Configuration/DeviceConfigurationInitializer.cs`
  - applies SQLite migrations at startup
  - generates/persists device UUID on first run
  - loads runtime device config snapshot
- `Configuration/DatabasePathResolver.cs`
  - OS-appropriate app-data default DB location
  - supports override via `Storage:DatabasePath`
- `Configuration/MediaPathResolver.cs`
  - validates the configured media root
  - resolves media file paths relative to the configured root
- `Library/LibraryService.cs`
  - SQLite-backed browse/query layer for Artists, Albums, Files, media resources, and library statistics
- `Library/LibraryScanner.cs`
  - startup library scan
  - read-only metadata import into SQLite
  - tolerates per-file failures and continues scanning

## Important interoperability decisions

- Keep `HEAD` enabled on media endpoints; Samsung requires this in practice.
- Keep DLNA media response headers:
  - `transferMode.dlna.org`
  - `contentFeatures.dlna.org`
- Keep DIDL `res@size`; several clients depend on it.
- Keep event subscription endpoints returning valid `SID` + `TIMEOUT`.

## Config notes

See `Phylet/appsettings*.json`:
- `Dlna:Manufacturer` and `Dlna:ModelName` stay in appsettings
- `Dlna:DefaultSubscriptionTimeoutSeconds` controls SUBSCRIBE timeout
- `Storage:DatabasePath` optionally overrides the SQLite file path
- `Storage:MediaPath` optionally overrides the media root
- `DeviceUuid` and `FriendlyName` are persisted in SQLite and initialized on first startup

Docker defaults:
- `Storage__DatabasePath=/data/phylet.db`
- `Storage__MediaPath=/media`

CI and publishing:
- `.github/workflows/docker-publish.yml`
  - runs `dotnet test --configuration Release --no-restore`
  - builds `linux/amd64` and `linux/arm64`
  - publishes `ghcr.io/<owner>/phylet` on pushes to `main` and `v*` tags

## Logging notes

- Repetitive request-level logs are `Debug`.
- High-signal service logs remain `Information`.
- Unsupported SSDP search targets are `Debug` (not warning noise).

## Media fixture for local development

Expected files in `Phylet/MediaTest/`:
- `track1.mp3`
- `track2.mp3`
- `cover.jpg`

Development `MediaTest` is only a fallback fixture. The real library model is SQLite-backed and scanned from the configured media root.

## Reference docs

High-level architecture notes:
- `docs/ARCHITECTURE.md`

Protocol baseline is currently described in:
- `docs/INTEROP_CONTRACT.md`

When resuming, update that file if wire-level behavior changes.
