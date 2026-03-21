using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Phylet.Services;

public sealed class ServerAddressResolver(
    IServer server,
    IHostApplicationLifetime hostApplicationLifetime,
    ILogger<ServerAddressResolver> logger)
{
    public async Task<ResolvedServerAddresses> ResolveAsync(CancellationToken cancellationToken)
    {
        await WaitForApplicationStartedAsync(cancellationToken);

        var listenAddresses = await WaitForListenAddressesAsync(cancellationToken);
        var advertisedBaseUri = ResolveAdvertisedBaseUri(listenAddresses);

        return new ResolvedServerAddresses(listenAddresses, advertisedBaseUri);
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken cancellationToken)
    {
        if (hostApplicationLifetime.ApplicationStarted.IsCancellationRequested)
        {
            return;
        }

        var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = hostApplicationLifetime.ApplicationStarted.Register(() => startedTcs.TrySetResult());
        using var cancellation = cancellationToken.Register(() => startedTcs.TrySetCanceled(cancellationToken));
        await startedTcs.Task;
    }

    private async Task<IReadOnlyList<Uri>> WaitForListenAddressesAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses
                .Select(address => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null)
                .OfType<Uri>()
                .Where(uri =>
                    uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (addresses is { Length: > 0 })
            {
                return addresses;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new InvalidOperationException("Server listen addresses were not available after startup.");
    }

    private Uri ResolveAdvertisedBaseUri(IReadOnlyList<Uri> listenAddresses)
    {
        var preferredListenAddress = ChoosePreferredListenAddress(listenAddresses);
        if (!NeedsLanHostSubstitution(preferredListenAddress.Host))
        {
            return NormalizeDefaultPort(preferredListenAddress);
        }

        var lanIp = TryGetPreferredOutboundIpv4Address();
        if (lanIp is null)
        {
            logger.LogWarning(
                "Server is listening on {ListenAddress}, but no preferred outbound LAN IPv4 was detected. DLNA advertisement will keep the unresolved host.",
                preferredListenAddress);
            return NormalizeDefaultPort(preferredListenAddress);
        }

        var builder = new UriBuilder(preferredListenAddress)
        {
            Host = lanIp.ToString()
        };

        return NormalizeDefaultPort(builder.Uri);
    }

    private static Uri ChoosePreferredListenAddress(IEnumerable<Uri> listenAddresses)
    {
        var addresses = listenAddresses.ToArray();

        return addresses.FirstOrDefault(uri =>
                   uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                   && !NeedsLanHostSubstitution(uri.Host))
               ?? addresses.FirstOrDefault(uri => uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
               ?? addresses.FirstOrDefault(uri => !NeedsLanHostSubstitution(uri.Host))
               ?? addresses[0];
    }

    private static bool NeedsLanHostSubstitution(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase)
            || host.Equals("[::]", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        return IPAddress.IsLoopback(address)
               || address.Equals(IPAddress.Any)
               || address.Equals(IPAddress.IPv6Any)
               || address.Equals(IPAddress.IPv6Loopback);
    }

    private static IPAddress? TryGetPreferredOutboundIpv4Address()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("239.255.255.250", 1900);

            if (socket.LocalEndPoint is IPEndPoint { Address: { } address }
                && address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(address)
                && !address.Equals(IPAddress.Any))
            {
                return address;
            }
        }
        catch
        {
            // Fall back to interface enumeration if the OS cannot resolve a route for the SSDP multicast target.
        }

        return TryGetFirstOperationalLanIpv4Address();
    }

    private static IPAddress? TryGetFirstOperationalLanIpv4Address()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var candidate = networkInterface.GetIPProperties()
                .UnicastAddresses
                .Select(address => address.Address)
                .FirstOrDefault(address =>
                    address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(address));

            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static Uri NormalizeDefaultPort(Uri uri)
    {
        if (!uri.IsDefaultPort)
        {
            return uri;
        }

        var builder = new UriBuilder(uri)
        {
            Port = -1
        };

        return builder.Uri;
    }
}

public sealed record ResolvedServerAddresses(IReadOnlyList<Uri> ListenAddresses, Uri AdvertisedBaseUri);
