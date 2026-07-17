using Microsoft.Win32;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Printing;
using System.Text.RegularExpressions;

namespace UDS_Print_Master.Services;

public sealed class PrinterWakeService
{
    private const string PrintMonitorsRegistryPath = @"SYSTEM\CurrentControlSet\Control\Print\Monitors";
    private const string ImageDevicesRegistryPath = @"SYSTEM\CurrentControlSet\Enum\ROOT\IMAGE";

    private static readonly int[] StandardDevicePorts = [80, 443, 631, 8080, 9100];
    private static readonly int[] HpScannerPorts = [8289, 9220, 9221, 9222, 9280, 9290, 9291, 9292, 9200, 9500];

    public async Task<PrinterWakeResult> WakeAsync(string? printerName, string? scannerName, CancellationToken cancellationToken)
    {
        var target = await Task.Run(() => FindNetworkTarget(printerName, scannerName), cancellationToken);
        if (target is null)
        {
            await RefreshPrintQueueAsync(printerName, cancellationToken);
            return new PrinterWakeResult(false, false, null);
        }

        var networkPorts = StandardDevicePorts
            .Append(target.Port > 0 ? target.Port : 9100)
            .Concat(target.IsHpDevice ? HpScannerPorts : [])
            .Distinct();
        var responseTasks = networkPorts
            .Select(port => ProbeTcpPortAsync(target.Address, port, cancellationToken))
            .Append(ProbeHttpAsync(target.Address, "/", useHead: true, cancellationToken))
            .Append(ProbeHttpAsync(target.Address, "/eSCL/ScannerCapabilities", useHead: false, cancellationToken))
            .ToList();

        var signalTasks = new List<Task<bool>>
        {
            SendSnmpProbeAsync(target, cancellationToken),
            SendScannerDiscoveryProbesAsync(target.Address, cancellationToken)
        };

        if (!string.IsNullOrWhiteSpace(target.MacAddress))
        {
            signalTasks.Add(SendMagicPacketAsync(target.Address, target.MacAddress, cancellationToken));
        }

        await Task.WhenAll(signalTasks);
        var responses = await Task.WhenAll(responseTasks);
        await RefreshPrintQueueAsync(printerName, cancellationToken);
        return new PrinterWakeResult(
            true,
            responses.Any(response => response),
            target.Address,
            target.ScannerAddressChanged,
            target.IsHpDevice ? "HP WIA" : "WIA/eSCL/WSD");
    }

    private static PrinterNetworkTarget? FindNetworkTarget(string? printerName, string? scannerName)
    {
        var candidates = new List<PrinterPortCandidate>();
        var scannerIdentity = FindScannerNetworkIdentity(scannerName);

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            try
            {
                using var server = new LocalPrintServer();
                using var queue = server.GetPrintQueue(printerName);
                queue.Refresh();
                AddCandidate(candidates, queue.Name, queue.QueuePort?.Name);
            }
            catch
            {
                // The complete queue list below may still be available.
            }
        }

        try
        {
            using var server = new LocalPrintServer();
            foreach (var queue in server.GetPrintQueues())
            {
                try
                {
                    AddCandidate(candidates, queue.Name, queue.QueuePort?.Name);
                }
                finally
                {
                    queue.Dispose();
                }
            }
        }
        catch
        {
            // Direct lookup above is enough on restricted managed PCs.
        }

        foreach (var candidate in candidates
                     .Select(candidate => new
                     {
                         Candidate = candidate,
                         Score = ScoreCandidate(candidate.PrinterName, printerName, scannerName)
                     })
                     .Where(item => item.Score > 0)
                     .OrderByDescending(item => item.Score)
                     .Select(item => item.Candidate))
        {
            var target = FindTargetByPort(candidate.PortName, candidate.PrinterName);
            if (target is not null)
            {
                return EnrichTarget(target, printerName, scannerName, scannerIdentity);
            }
        }

        var fallbackAddress = FirstNonEmpty(
            ExtractAddress(printerName),
            ExtractAddress(scannerName),
            scannerIdentity?.HostName,
            scannerIdentity?.Address);
        return string.IsNullOrWhiteSpace(fallbackAddress)
            ? null
            : EnrichTarget(
                new PrinterNetworkTarget(fallbackAddress, scannerIdentity?.MacAddress, "public", 9100, false, false),
                printerName,
                scannerName,
                scannerIdentity);
    }

    private static PrinterNetworkTarget EnrichTarget(
        PrinterNetworkTarget target,
        string? printerName,
        string? scannerName,
        ScannerNetworkIdentity? scannerIdentity)
    {
        var targetMac = NormalizeMacAddress(target.MacAddress);
        var scannerMac = NormalizeMacAddress(scannerIdentity?.MacAddress);
        var sameDevice = !string.IsNullOrWhiteSpace(targetMac)
                         && string.Equals(targetMac, scannerMac, StringComparison.OrdinalIgnoreCase);
        var addressChanged = sameDevice
                             && !string.IsNullOrWhiteSpace(scannerIdentity?.Address)
                             && !string.Equals(target.Address, scannerIdentity.Address, StringComparison.OrdinalIgnoreCase);
        var isHp = LooksLikeHpDevice(printerName, scannerName, scannerIdentity?.Description, scannerIdentity?.Manufacturer);

        return target with
        {
            MacAddress = FirstNonEmpty(target.MacAddress, scannerIdentity?.MacAddress),
            IsHpDevice = isHp,
            ScannerAddressChanged = addressChanged
        };
    }

    private static ScannerNetworkIdentity? FindScannerNetworkIdentity(string? scannerName)
    {
        try
        {
            using var imageDevices = Registry.LocalMachine.OpenSubKey(ImageDevicesRegistryPath);
            if (imageDevices is null)
            {
                return null;
            }

            var identities = new List<ScannerNetworkIdentity>();
            foreach (var deviceKeyName in imageDevices.GetSubKeyNames())
            {
                using var device = imageDevices.OpenSubKey(deviceKeyName);
                using var parameters = device?.OpenSubKey("Device Parameters");
                if (device is null || parameters is null)
                {
                    continue;
                }

                var description = FirstNonEmpty(
                    device.GetValue("DeviceDesc") as string,
                    parameters.GetValue("FriendlyName") as string);
                var manufacturer = device.GetValue("Mfg") as string;
                var networkDeviceId = parameters.GetValue("NetworkDeviceID") as string;
                var address = FirstNonEmpty(
                    parameters.GetValue("PortID") as string,
                    ExtractAddress(networkDeviceId));
                var hostName = FirstNonEmpty(
                    parameters.GetValue("NetworkHostName") as string,
                    ExtractNetworkDeviceValue(networkDeviceId, "hostname"));
                var macAddress = FirstNonEmpty(
                    parameters.GetValue("MAC") as string,
                    ExtractNetworkDeviceValue(networkDeviceId, "macaddr"));

                if (string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(hostName))
                {
                    continue;
                }

                identities.Add(new ScannerNetworkIdentity(description, manufacturer, address, hostName, macAddress));
            }

            return identities
                       .Select(identity => new
                       {
                           Identity = identity,
                           Score = ScoreCandidate(identity.Description ?? string.Empty, null, scannerName)
                       })
                       .OrderByDescending(item => item.Score)
                       .FirstOrDefault(item => item.Score > 0)?.Identity
                   ?? (identities.Count == 1 ? identities[0] : null);
        }
        catch
        {
            // WIA still performs its own discovery if registry access is restricted.
            return null;
        }
    }

    private static PrinterNetworkTarget? FindTargetByPort(string portName, string printerName)
    {
        try
        {
            using var monitors = Registry.LocalMachine.OpenSubKey(PrintMonitorsRegistryPath);
            if (monitors is not null)
            {
                foreach (var monitorName in monitors.GetSubKeyNames())
                {
                    using var port = monitors.OpenSubKey($@"{monitorName}\Ports\{portName}");
                    if (port is null)
                    {
                        continue;
                    }

                    var address = FirstNonEmpty(
                        port.GetValue("IPAddress") as string,
                        port.GetValue("HostName") as string,
                        ExtractAddress(portName),
                        ExtractAddress(printerName));
                    if (string.IsNullOrWhiteSpace(address))
                    {
                        continue;
                    }

                    var macAddress = FirstNonEmpty(
                        port.GetValue("HWAddress") as string,
                        port.GetValue("DeviceGUID") as string);
                    var community = FirstNonEmpty(port.GetValue("SNMP Community") as string, "public") ?? "public";
                    var configuredPort = ReadRegistryInt(port, "PortNumber") ?? 9100;
                    return new PrinterNetworkTarget(address, macAddress, community, configuredPort, false, false);
                }
            }
        }
        catch
        {
            // Some managed PCs restrict printer monitor registry access.
        }

        var fallbackAddress = FirstNonEmpty(ExtractAddress(portName), ExtractAddress(printerName));
        return string.IsNullOrWhiteSpace(fallbackAddress)
            ? null
            : new PrinterNetworkTarget(fallbackAddress, null, "public", 9100, false, false);
    }

    private static void AddCandidate(ICollection<PrinterPortCandidate> candidates, string printerName, string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName)
            || candidates.Any(candidate => string.Equals(candidate.PortName, portName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidates.Add(new PrinterPortCandidate(printerName, portName));
    }

    private static int ScoreCandidate(string candidateName, string? selectedPrinterName, string? scannerName)
    {
        var score = string.Equals(candidateName, selectedPrinterName, StringComparison.OrdinalIgnoreCase) ? 25 : 0;
        var scannerTokens = TokenizeDeviceName(scannerName);
        var candidateTokens = TokenizeDeviceName(candidateName);
        score += scannerTokens.Intersect(candidateTokens, StringComparer.OrdinalIgnoreCase).Count() * 20;
        return score;
    }

    private static HashSet<string> TokenizeDeviceName(string? value)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "scanner", "wia", "printer", "series", "device", "friendly", "name", "pcl"
        };

        return Regex.Matches(value ?? string.Empty, @"[\p{L}\p{N}]+")
            .Select(match => match.Value)
            .Where(token => token.Length >= 2 && !ignored.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<bool> ProbeTcpPortAsync(string address, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = new TcpClient();
            await client.ConnectAsync(address, port, timeout.Token);
            return client.Connected;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task<bool> ProbeHttpAsync(
        string address,
        string path,
        bool useHead,
        CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                UseProxy = false
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(4)
            };
            using var request = new HttpRequestMessage(
                useHead ? HttpMethod.Head : HttpMethod.Get,
                $"http://{FormatHostForUri(address)}{path}");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static async Task<bool> SendSnmpProbeAsync(PrinterNetworkTarget target, CancellationToken cancellationToken)
    {
        try
        {
            var packet = BuildSnmpSystemDescriptionRequest(target.SnmpCommunity);
            using var udp = new UdpClient();
            await udp.SendAsync(packet, new IPEndPoint(await ResolveAddressAsync(target.Address, cancellationToken), 161), cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> SendMagicPacketAsync(string address, string macAddress, CancellationToken cancellationToken)
    {
        try
        {
            var mac = ParseMacAddress(macAddress);
            if (mac is null)
            {
                return false;
            }

            var packet = new byte[6 + 16 * mac.Length];
            Array.Fill(packet, (byte)0xFF, 0, 6);
            for (var index = 6; index < packet.Length; index += mac.Length)
            {
                Buffer.BlockCopy(mac, 0, packet, index, mac.Length);
            }

            var targetAddress = await ResolveAddressAsync(address, cancellationToken);
            using var udp = new UdpClient
            {
                EnableBroadcast = true
            };
            var wakeAddresses = new[] { targetAddress, IPAddress.Broadcast }
                .Concat(GetDirectedBroadcastAddresses(targetAddress))
                .Distinct()
                .ToList();
            foreach (var wakeAddress in wakeAddresses)
            {
                await udp.SendAsync(packet, new IPEndPoint(wakeAddress, 7), cancellationToken);
                await udp.SendAsync(packet, new IPEndPoint(wakeAddress, 9), cancellationToken);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<IPAddress> GetDirectedBroadcastAddresses(IPAddress targetAddress)
    {
        if (targetAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            yield break;
        }

        var targetBytes = targetAddress.GetAddressBytes();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
            {
                var localAddress = unicast.Address;
                var mask = unicast.IPv4Mask;
                if (localAddress.AddressFamily != AddressFamily.InterNetwork || mask is null)
                {
                    continue;
                }

                var localBytes = localAddress.GetAddressBytes();
                var maskBytes = mask.GetAddressBytes();
                var sameNetwork = true;
                var broadcastBytes = new byte[4];

                for (var index = 0; index < 4; index++)
                {
                    if ((localBytes[index] & maskBytes[index]) != (targetBytes[index] & maskBytes[index]))
                    {
                        sameNetwork = false;
                        break;
                    }

                    broadcastBytes[index] = (byte)(targetBytes[index] | ~maskBytes[index]);
                }

                if (sameNetwork)
                {
                    yield return new IPAddress(broadcastBytes);
                }
            }
        }
    }

    private static async Task<bool> SendScannerDiscoveryProbesAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            var targetAddress = await ResolveAddressAsync(address, cancellationToken);
            var discoveryPayload = System.Text.Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" " +
                "xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" " +
                "xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\">" +
                "<s:Header><a:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action>" +
                $"<a:MessageID>urn:uuid:{Guid.NewGuid()}</a:MessageID>" +
                "<a:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To></s:Header>" +
                "<s:Body><d:Probe/></s:Body></s:Envelope>");

            using var udp = new UdpClient();
            await udp.SendAsync(discoveryPayload, new IPEndPoint(targetAddress, 3702), cancellationToken);
            await udp.SendAsync(discoveryPayload, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702), cancellationToken);

            var mdnsProbe = BuildMdnsScannerQuery();
            await udp.SendAsync(mdnsProbe, new IPEndPoint(targetAddress, 5353), cancellationToken);
            await udp.SendAsync(mdnsProbe, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353), cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] BuildMdnsScannerQuery()
    {
        var services = new[] { "_uscan._tcp.local", "_scanner._tcp.local" };
        using var stream = new MemoryStream();
        WriteNetworkUInt16(stream, 0); // Transaction ID
        WriteNetworkUInt16(stream, 0); // Standard query
        WriteNetworkUInt16(stream, (ushort)services.Length);
        WriteNetworkUInt16(stream, 0);
        WriteNetworkUInt16(stream, 0);
        WriteNetworkUInt16(stream, 0);

        foreach (var service in services)
        {
            foreach (var label in service.Split('.'))
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes(label);
                stream.WriteByte((byte)bytes.Length);
                stream.Write(bytes);
            }

            stream.WriteByte(0);
            WriteNetworkUInt16(stream, 12); // PTR
            WriteNetworkUInt16(stream, 1);  // IN
        }

        return stream.ToArray();
    }

    private static void WriteNetworkUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static async Task RefreshPrintQueueAsync(string? printerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                using var server = new LocalPrintServer();
                using var queue = server.GetPrintQueue(printerName);
                queue.Refresh();
            }, cancellationToken);
        }
        catch
        {
            // WIA retries remain useful even if the print queue cannot be refreshed.
        }
    }

    private static byte[] BuildSnmpSystemDescriptionRequest(string community)
    {
        var communityBytes = System.Text.Encoding.ASCII.GetBytes(string.IsNullOrWhiteSpace(community) ? "public" : community);
        if (communityBytes.Length > 32)
        {
            communityBytes = communityBytes[..32];
        }

        var requestId = BitConverter.GetBytes(Environment.TickCount);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(requestId);
        }

        var variableBinding = new byte[]
        {
            0x30, 0x0C, 0x06, 0x08, 0x2B, 0x06, 0x01, 0x02, 0x01, 0x01, 0x01, 0x00, 0x05, 0x00
        };
        var variableBindingList = WrapAsn1(0x30, variableBinding);
        var pduContent = Combine(
            new byte[] { 0x02, 0x04 }, requestId,
            new byte[] { 0x02, 0x01, 0x00, 0x02, 0x01, 0x00 },
            variableBindingList);
        var pdu = WrapAsn1(0xA0, pduContent);
        var message = Combine(
            new byte[] { 0x02, 0x01, 0x00, 0x04, (byte)communityBytes.Length },
            communityBytes,
            pdu);
        return WrapAsn1(0x30, message);
    }

    private static byte[] WrapAsn1(byte tag, byte[] content)
    {
        if (content.Length > 127)
        {
            throw new InvalidOperationException("Слишком длинный SNMP-пакет.");
        }

        return Combine(new[] { tag, (byte)content.Length }, content);
    }

    private static byte[] Combine(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }

    private static async Task<IPAddress> ResolveAddressAsync(string address, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(address, out var parsed))
        {
            return parsed;
        }

        var addresses = await Dns.GetHostAddressesAsync(address, cancellationToken);
        return addresses.First(candidate => candidate.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);
    }

    private static byte[]? ParseMacAddress(string value)
    {
        var normalized = Regex.Replace(value, "[^0-9A-Fa-f]", string.Empty);
        if (normalized.Length != 12)
        {
            return null;
        }

        try
        {
            return Enumerable.Range(0, 6)
                .Select(index => Convert.ToByte(normalized.Substring(index * 2, 2), 16))
                .ToArray();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static int? ReadRegistryInt(RegistryKey key, string valueName)
    {
        try
        {
            return Convert.ToInt32(key.GetValue(valueName));
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var ipv4 = Regex.Match(value, @"(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?!\d)");
        return ipv4.Success ? ipv4.Value : null;
    }

    private static string? ExtractNetworkDeviceValue(string? networkDeviceId, string key)
    {
        if (string.IsNullOrWhiteSpace(networkDeviceId))
        {
            return null;
        }

        var match = Regex.Match(networkDeviceId, $@"\\{Regex.Escape(key)}:([^\\]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static bool LooksLikeHpDevice(params string?[] values)
    {
        var identity = string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        return Regex.IsMatch(identity, @"\bHP\b|Hewlett[ -]?Packard", RegexOptions.IgnoreCase);
    }

    private static string? NormalizeMacAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value, "[^0-9A-Fa-f]", string.Empty);
        return normalized.Length == 12 ? normalized.ToUpperInvariant() : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string FormatHostForUri(string address)
    {
        return address.Contains(':', StringComparison.Ordinal) && !address.StartsWith("[", StringComparison.Ordinal)
            ? $"[{address}]"
            : address;
    }

    private sealed record PrinterNetworkTarget(
        string Address,
        string? MacAddress,
        string SnmpCommunity,
        int Port,
        bool IsHpDevice,
        bool ScannerAddressChanged);

    private sealed record PrinterPortCandidate(string PrinterName, string PortName);

    private sealed record ScannerNetworkIdentity(
        string? Description,
        string? Manufacturer,
        string? Address,
        string? HostName,
        string? MacAddress);
}

public sealed record PrinterWakeResult(
    bool NetworkTargetFound,
    bool DeviceResponded,
    string? Address,
    bool ScannerAddressChanged = false,
    string? WakeMethod = null);
