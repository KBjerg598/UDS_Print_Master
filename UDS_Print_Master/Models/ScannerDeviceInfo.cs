namespace UDS_Print_Master.Models;

public sealed class ScannerDeviceInfo
{
    public ScannerDeviceInfo(string deviceId, string name)
    {
        DeviceId = deviceId;
        Name = name;
    }

    public string DeviceId { get; }

    public string Name { get; }
}

public sealed record ScannerCapabilities(
    bool HasFlatbed,
    bool HasFeeder,
    bool SupportsDuplex,
    IReadOnlyList<int> SupportedDpi);

public sealed record ScanRequest(
    string DeviceId,
    bool UseFeeder,
    bool Duplex,
    int Dpi,
    string ColorMode,
    string OutputFolder);
