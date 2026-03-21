using System.Net;
using System.Net.Sockets;
using System.Text;
using Phylet.Data.Configuration;

namespace Phylet.Services;

public sealed class SsdpService(
    IDeviceConfigurationProvider configurationProvider,
    ServerAddressResolver serverAddressResolver,
    ILogger<SsdpService> logger) : BackgroundService
{
    private const string MulticastAddress = "239.255.255.250";
    private const int MulticastPort = 1900;
    private const int CacheMaxAgeSeconds = 1800;

    private readonly string[] _notificationTypes =
    [
        "upnp:rootdevice",
        "urn:schemas-upnp-org:device:MediaServer:1",
        "urn:schemas-upnp-org:service:ContentDirectory:1",
        "urn:schemas-upnp-org:service:ConnectionManager:1"
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var resolvedAddresses = await serverAddressResolver.ResolveAsync(stoppingToken);
        var advertisedBaseUri = resolvedAddresses.AdvertisedBaseUri;
        logger.LogInformation(
            "SSDP advertised server address resolved. ListenAddresses={ListenAddresses}, Advertised={AdvertisedBaseUrl}",
            string.Join(", ", resolvedAddresses.ListenAddresses.Select(address => address.ToString())),
            advertisedBaseUri);

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
        udp.JoinMulticastGroup(IPAddress.Parse(MulticastAddress));
        udp.MulticastLoopback = false;
        logger.LogInformation(
            "SSDP listener started on UDP {BindAddress}:{Port}, multicast group {MulticastAddress}:{MulticastPort}, description at {DescriptionUrl}",
            IPAddress.Any,
            MulticastPort,
            MulticastAddress,
            MulticastPort,
            new Uri(advertisedBaseUri, "/description.xml"));

        await SendAliveNotificationsAsync(udp, advertisedBaseUri);
        using var announceTimer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        var tickTask = announceTimer.WaitForNextTickAsync(stoppingToken).AsTask();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var receiveTask = udp.ReceiveAsync(stoppingToken).AsTask();
                var completed = await Task.WhenAny(receiveTask, tickTask);

                if (completed == tickTask)
                {
                    if (await tickTask)
                    {
                        logger.LogInformation("SSDP periodic alive announce");
                        await SendAliveNotificationsAsync(udp, advertisedBaseUri);
                        tickTask = announceTimer.WaitForNextTickAsync(stoppingToken).AsTask();
                    }

                    continue;
                }

                UdpReceiveResult received;
                try
                {
                    received = await receiveTask;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error while receiving SSDP traffic");
                    continue;
                }

                await HandleSearchRequestAsync(udp, received, advertisedBaseUri);
            }
        }
        finally
        {
            await SendByebyeNotificationsAsync(udp, advertisedBaseUri);
        }
    }

    private async Task HandleSearchRequestAsync(UdpClient udp, UdpReceiveResult received, Uri advertisedBaseUri)
    {
        var request = Encoding.ASCII.GetString(received.Buffer);
        var requestLine = request.Split("\r\n", StringSplitOptions.None).FirstOrDefault() ?? "(empty)";
        logger.LogDebug("SSDP packet from {RemoteEndpoint}: {RequestLine}", received.RemoteEndPoint, requestLine);
        if (!IsSearchRequest(request))
        {
            return;
        }

        var st = ExtractHeader(request, "ST") ?? "ssdp:all";
        var man = ExtractHeader(request, "MAN") ?? string.Empty;
        var mx = ExtractHeader(request, "MX") ?? string.Empty;
        var responseTargets = GetSearchResponseTargets(st);
        logger.LogDebug(
            "SSDP M-SEARCH from {RemoteEndpoint} ST={SearchTarget} MAN={ManHeader} MX={MxHeader} -> {ResponseCount} response(s)",
            received.RemoteEndPoint,
            st,
            man,
            mx,
            responseTargets.Count);

        if (responseTargets.Count == 0)
        {
            logger.LogDebug("SSDP search target not supported: {SearchTarget}", st);
        }

        foreach (var target in responseTargets)
        {
            var response = BuildSearchResponse(target, advertisedBaseUri);
            var bytes = Encoding.ASCII.GetBytes(response);

            try
            {
                await udp.SendAsync(bytes, bytes.Length, received.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error while sending SSDP response to {RemoteEndpoint} ST={SearchTarget}", received.RemoteEndPoint, target);
            }
        }
    }

    private IReadOnlyList<string> GetSearchResponseTargets(string searchTarget)
    {
        var deviceUuid = configurationProvider.Current.DeviceUuid;

        if (searchTarget.Equals("ssdp:all", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "upnp:rootdevice", deviceUuid }
                .Concat(_notificationTypes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (searchTarget.Equals(deviceUuid, StringComparison.OrdinalIgnoreCase))
        {
            return [deviceUuid];
        }

        return _notificationTypes.Any(nt => nt.Equals(searchTarget, StringComparison.OrdinalIgnoreCase))
            ? [searchTarget]
            : [];
    }

    private static bool IsSearchRequest(string request) =>
        request.Contains("M-SEARCH * HTTP/1.1", StringComparison.OrdinalIgnoreCase)
        && request.Contains("MAN: \"ssdp:discover\"", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractHeader(string request, string headerName)
    {
        var lines = request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith(headerName + ":", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var idx = line.IndexOf(':');
            if (idx < 0 || idx + 1 >= line.Length)
            {
                continue;
            }

            return line[(idx + 1)..].Trim();
        }

        return null;
    }

    private string BuildSearchResponse(string searchTarget, Uri advertisedBaseUri)
    {
        var location = new Uri(advertisedBaseUri, "/description.xml").ToString();
        var usn = BuildUsn(searchTarget);

        return string.Join("\r\n",
            "HTTP/1.1 200 OK",
            $"CACHE-CONTROL: max-age={CacheMaxAgeSeconds}",
            "EXT:",
            $"LOCATION: {location}",
            "SERVER: Phylet/1.0 UPnP/1.0 DLNADOC/1.50",
            $"ST: {searchTarget}",
            $"USN: {usn}",
            "BOOTID.UPNP.ORG: 1",
            "CONFIGID.UPNP.ORG: 1",
            string.Empty,
            string.Empty);
    }

    private async Task SendAliveNotificationsAsync(UdpClient udp, Uri advertisedBaseUri)
    {
        foreach (var nt in _notificationTypes.Prepend(configurationProvider.Current.DeviceUuid))
        {
            var notify = BuildNotifyMessage(nt, "ssdp:alive", advertisedBaseUri);
            var bytes = Encoding.ASCII.GetBytes(notify);
            try
            {
                await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort));
                logger.LogDebug("SSDP NOTIFY alive sent NT={NotificationType}", nt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error while sending SSDP alive notification NT={NotificationType}", nt);
            }
        }
    }

    private async Task SendByebyeNotificationsAsync(UdpClient udp, Uri advertisedBaseUri)
    {
        foreach (var nt in _notificationTypes.Prepend(configurationProvider.Current.DeviceUuid))
        {
            var notify = BuildNotifyMessage(nt, "ssdp:byebye", advertisedBaseUri);
            var bytes = Encoding.ASCII.GetBytes(notify);
            try
            {
                await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort));
                logger.LogDebug("SSDP NOTIFY byebye sent NT={NotificationType}", nt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error while sending SSDP byebye notification NT={NotificationType}", nt);
            }
        }
    }

    private string BuildNotifyMessage(string notificationType, string nts, Uri advertisedBaseUri)
    {
        var location = new Uri(advertisedBaseUri, "/description.xml").ToString();
        var usn = BuildUsn(notificationType);

        return string.Join("\r\n",
            "NOTIFY * HTTP/1.1",
            $"HOST: {MulticastAddress}:{MulticastPort}",
            $"NT: {notificationType}",
            $"NTS: {nts}",
            "SERVER: Phylet/1.0 UPnP/1.0 DLNADOC/1.50",
            $"USN: {usn}",
            $"CACHE-CONTROL: max-age={CacheMaxAgeSeconds}",
            $"LOCATION: {location}",
            "BOOTID.UPNP.ORG: 1",
            "CONFIGID.UPNP.ORG: 1",
            string.Empty,
            string.Empty);
    }

    private string BuildUsn(string notificationType) =>
        notificationType.Equals(configurationProvider.Current.DeviceUuid, StringComparison.OrdinalIgnoreCase)
            ? configurationProvider.Current.DeviceUuid
            : $"{configurationProvider.Current.DeviceUuid}::{notificationType}";
}
