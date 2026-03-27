# Phylet DLNA Interop Contract

This document freezes wire behavior that is currently verified with Samsung TV and Lyngdorf amplifier.

## SSDP

- Multicast listener: `239.255.255.250:1900` (IPv4)
- Responds to `M-SEARCH` for:
  - `ssdp:all`
  - `upnp:rootdevice`
  - `uuid:<device-guid>`
  - `urn:schemas-upnp-org:device:MediaServer:1`
  - `urn:schemas-upnp-org:service:ContentDirectory:1`
  - `urn:schemas-upnp-org:service:ConnectionManager:1`
- Sends `NOTIFY ssdp:alive` on startup and periodically.
- Sends `NOTIFY ssdp:byebye` on shutdown.
- `LOCATION` points to `/description.xml` on the advertised runtime base URL derived from the active server listen address.

## Device Description

- Endpoint: `GET /description.xml`
- Namespace: `urn:schemas-upnp-org:device-1-0`
- Device UUID and friendly name are loaded from persisted runtime configuration on startup.
- Advertises `iconList` entries for:
  - `/icons/server-48.png`
  - `/icons/server-120.png`
  - `/icons/server-240.png`
- Services exposed:
  - ContentDirectory control: `/upnp/control/contentdirectory`
  - ConnectionManager control: `/upnp/control/connectionmanager`
  - Event URLs:
    - `/upnp/event/contentdirectory`
    - `/upnp/event/connectionmanager`

## ContentDirectory

- SCPD endpoint: `GET /ContentDirectory/scpd.xml`
- Control endpoint: `POST /upnp/control/contentdirectory`
- Actions supported:
  - `Browse`
  - `GetSearchCapabilities`
  - `GetSortCapabilities`
  - `GetSystemUpdateID`
- Browse args behavior:
  - `BrowseFlag`: `BrowseMetadata` or `BrowseDirectChildren`
  - `StartingIndex`: non-negative integer
  - `RequestedCount`: non-negative integer (`0` means all)
- Fault codes:
  - `401` Invalid Action
  - `402` Invalid Args
  - `701` No Such Object
  - `710` No Such Container

## ConnectionManager

- SCPD endpoint: `GET /ConnectionManager/scpd.xml`
- Control endpoint: `POST /upnp/control/connectionmanager`
- Actions supported:
  - `GetProtocolInfo`
  - `GetCurrentConnectionIDs`
  - `GetCurrentConnectionInfo`

## Eventing

- Supported methods:
  - `SUBSCRIBE /upnp/event/contentdirectory`
  - `UNSUBSCRIBE /upnp/event/contentdirectory`
  - `SUBSCRIBE /upnp/event/connectionmanager`
  - `UNSUBSCRIBE /upnp/event/connectionmanager`
- Responses:
  - `200` for accepted subscription/renewal/unsubscribe
  - `412` for invalid/unknown SID renewals or unsubscribe requests
- Subscription headers returned:
  - `SID: uuid:<guid>`
  - `TIMEOUT: Second-<n>`

## Media URLs

- Audio endpoint: `GET|HEAD /media/audio/{trackId}`
- Artwork endpoint: `GET|HEAD /media/image/{albumId}`
- Audio response headers:
  - `Content-Type`: depends on scanned track format (`audio/mpeg`, `audio/flac`, `audio/mp4`, `audio/ogg`, `audio/wav`, `audio/aiff`)
  - `transferMode.dlna.org: Streaming`
  - `contentFeatures.dlna.org`: derived from the track format
- Artwork response headers:
  - `Content-Type`: `image/jpeg` or `image/png`
  - `transferMode.dlna.org: Interactive`
  - `contentFeatures.dlna.org`: `JPEG_TN` or `PNG_TN` profile metadata
- HTTP range is enabled for both audio and images.

## DIDL-Lite

- Includes namespaces:
  - DIDL-Lite, `dc`, `upnp`, `dlna`
- Root containers `artists`, `albums`, and `files` include `upnp:albumArtURI` pointing at static PNG section icons.
- Track `res` includes:
  - `protocolInfo` with the scanned track MIME type and DLNA parameters
  - `size` (when media file exists)
- Includes `upnp:albumArtURI` with a `dlna:profileID` that matches the actual image type (`JPEG_TN` or `PNG_TN`).
- Track titles are presented as `<track>. <title>` when a positive track number is known.

## Browse Roots And Object IDs

- Root: `0`
- Root containers:
  - `artists`
  - `albums`
  - `files`
- Entity ids:
  - `artist:{id}`
  - `album:{id}`
  - `folder:{id}`
  - `track:{id}`
  - `file-track:{id}`
