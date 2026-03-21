using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;
using Phylet.Data.Configuration;
using Phylet.Data.Library;
using Phylet.Services;

namespace Phylet.Controllers;

[ApiController]
public sealed class HomeController(
    IDeviceConfigurationProvider configurationProvider,
    MediaPathResolver mediaPathResolver,
    LibraryService libraryService,
    ServerAddressResolver serverAddressResolver,
    LibraryScanService scanService) : ControllerBase
{
    [HttpGet("/")]
    public async Task<ContentResult> Get(CancellationToken cancellationToken)
    {
        var configuration = configurationProvider.Current;
        var mediaRoot = mediaPathResolver.EnsureMediaDirectoryExists();
        var statistics = await libraryService.GetStatisticsAsync(cancellationToken);
        var addresses = await serverAddressResolver.ResolveAsync(cancellationToken);
        var scanStatus = scanService.Current;

        return Content(
            BuildHtml(configuration, mediaRoot, statistics, addresses.AdvertisedBaseUri?.ToString() ?? "Unavailable", scanStatus),
            "text/html; charset=utf-8",
            Encoding.UTF8);
    }

    private static string BuildHtml(
        RuntimeDeviceConfiguration configuration,
        string mediaRoot,
        LibraryStatistics statistics,
        string advertisedBaseUrl,
        LibraryScanStatus scanStatus)
    {
        var encoder = HtmlEncoder.Default;
        var safeFriendlyName = encoder.Encode(configuration.FriendlyName);
        var safeMediaRoot = encoder.Encode(mediaRoot);
        var safeAdvertisedBaseUrl = encoder.Encode(advertisedBaseUrl);
        var safeDeviceUuid = encoder.Encode(configuration.DeviceUuid);
        var safeScanMessage = encoder.Encode(FormatScanStatus(scanStatus));
        var statusClassName = scanStatus.IsInProgress ? "status status-active" : "status";

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Phylet Music Server</title>
  <style>
    :root {
      color-scheme: light;
      --bg: oklch(0.985 0.004 285);
      --paper: rgba(255, 255, 255, 0.86);
      --ink: oklch(0.24 0.015 285);
      --muted: oklch(0.52 0.018 285);
      --accent: oklch(0.5586 0.2565 279);
      --accent-soft: oklch(0.94 0.03 285);
      --warm: oklch(0.71 0.15 75);
      --warm-soft: oklch(0.96 0.04 85);
      --line: rgba(35, 27, 54, 0.1);
      --shadow: 0 24px 60px rgba(34, 24, 58, 0.12);
    }

    * { box-sizing: border-box; }

    body {
      margin: 0;
      min-height: 100vh;
      font-family: ui-sans-serif, system-ui, sans-serif, Apple Color Emoji, Segoe UI Emoji, Segoe UI Symbol, Noto Color Emoji;
      color: var(--ink);
      background:
        radial-gradient(circle at top left, oklch(0.92 0.06 279 / 0.55), transparent 30%),
        radial-gradient(circle at right 20%, oklch(0.96 0.015 285 / 0.9), transparent 28%),
        linear-gradient(135deg, #ffffff 0%, #f6f6fb 48%, #ececf4 100%);
      display: grid;
      place-items: center;
      padding: 28px;
    }

    main {
      width: min(980px, 100%);
      background: var(--paper);
      border: 1px solid var(--line);
      border-radius: 28px;
      box-shadow: var(--shadow);
      overflow: hidden;
      backdrop-filter: blur(14px);
    }

    .hero {
      padding: 44px 44px 28px;
      border-bottom: 1px solid var(--line);
      background:
        linear-gradient(135deg, rgba(255,255,255,0.7), rgba(255,255,255,0.24)),
        linear-gradient(90deg, oklch(0.5586 0.2565 279 / 0.14), transparent 45%);
    }

    .eyebrow {
      margin: 0 0 10px;
      font-size: 12px;
      letter-spacing: 0.24em;
      text-transform: uppercase;
      color: var(--accent);
    }

    h1 {
      margin: 0;
      font-size: clamp(2.3rem, 5vw, 4.4rem);
      line-height: 0.95;
      letter-spacing: -0.05em;
    }

    .tagline {
      margin: 14px 0 0;
      max-width: 42rem;
      color: var(--muted);
      font-size: 1.05rem;
      line-height: 1.6;
    }

    .body {
      padding: 28px 44px 44px;
      display: grid;
      gap: 18px;
    }

    .status {
      display: inline-flex;
      align-items: center;
      gap: 10px;
      width: fit-content;
      max-width: 100%;
      padding: 10px 14px;
      border-radius: 999px;
      border: 1px solid var(--line);
      background: rgba(255,255,255,0.75);
      color: var(--muted);
      font-size: 0.95rem;
    }

    .status::before {
      content: "";
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: color-mix(in oklab, var(--muted) 40%, white);
      flex: none;
    }

    .status-active {
      background: var(--warm-soft);
      color: var(--ink);
      border-color: color-mix(in oklab, var(--warm) 25%, white);
    }

    .status-active::before {
      background: var(--warm);
      box-shadow: 0 0 0 8px color-mix(in oklab, var(--warm) 18%, transparent);
    }

    .stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
      gap: 14px;
    }

    .card, .meta {
      border: 1px solid var(--line);
      border-radius: 20px;
      background: rgba(255,255,255,0.6);
    }

    .card {
      padding: 18px 18px 16px;
    }

    .value {
      margin: 0;
      font-size: 1.9rem;
      line-height: 1;
    }

    .label {
      margin: 8px 0 0;
      color: var(--muted);
      font-size: 0.92rem;
    }

    .meta {
      padding: 20px 22px;
      display: grid;
      gap: 14px;
    }

    .row {
      display: grid;
      gap: 5px;
    }

    .row strong {
      font-size: 0.82rem;
      letter-spacing: 0.08em;
      text-transform: uppercase;
      color: var(--muted);
    }

    code {
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
      font-size: 0.95rem;
      word-break: break-all;
      color: var(--ink);
      background: var(--accent-soft);
      padding: 2px 6px;
      border-radius: 8px;
    }

    @media (max-width: 640px) {
      .hero, .body {
        padding-left: 20px;
        padding-right: 20px;
      }
    }
  </style>
</head>
<body>
  <main>
    <section class="hero">
      <p class="eyebrow">Phylet Music Server</p>
      <h1>{{safeFriendlyName}}</h1>
      <p class="tagline">DLNA music library online, indexed, and ready for browse and playback.</p>
    </section>
    <section class="body">
      <div class="{{statusClassName}}">{{safeScanMessage}}</div>
      <div class="stats">
        <article class="card">
          <p class="value">{{FormatNumber(statistics.ArtistCount)}}</p>
          <p class="label">Artists</p>
        </article>
        <article class="card">
          <p class="value">{{FormatNumber(statistics.AlbumCount)}}</p>
          <p class="label">Albums</p>
        </article>
        <article class="card">
          <p class="value">{{FormatNumber(statistics.TrackCount)}}</p>
          <p class="label">Tracks</p>
        </article>
        <article class="card">
          <p class="value">{{FormatBytes(statistics.TotalAudioBytes)}}</p>
          <p class="label">Library Size</p>
        </article>
      </div>
      <section class="meta">
        <div class="row">
          <strong>Configured Folder</strong>
          <code>{{safeMediaRoot}}</code>
        </div>
        <div class="row">
          <strong>Advertised Base URL</strong>
          <code>{{safeAdvertisedBaseUrl}}</code>
        </div>
        <div class="row">
          <strong>Device UUID</strong>
          <code>{{safeDeviceUuid}}</code>
        </div>
        <div class="row">
          <strong>Last Scan</strong>
          <div>{{FormatTimestamp(statistics.LastScanUtc)}}</div>
        </div>
      </section>
    </section>
  </main>
</body>
</html>
""";
    }

    private static string FormatNumber(int value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var format = unitIndex == 0 ? "0" : "0.0";
        return $"{value.ToString(format, CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }

    private static string FormatTimestamp(DateTime? value) =>
        value.HasValue
            ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
            : "No completed scan yet";

    private static string FormatScanStatus(LibraryScanStatus status)
    {
        if (status.IsInProgress)
        {
            return status.StartedUtc.HasValue
            ? $"Library scan in progress since {FormatTimestamp(status.StartedUtc)}"
                : "Library scan in progress";
        }

        if (status.IsQueued)
        {
            return "Library scan is queued";
        }

        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            return $"Last scan failed: {status.LastError}";
        }

        return status.LastCompletedUtc.HasValue
            ? $"Library scan completed at {FormatTimestamp(status.LastCompletedUtc)}"
            : "Library scan is queued after startup";
    }
}
