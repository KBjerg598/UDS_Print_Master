using System.IO;
using System.Text.Json;
using UDS_Print_Master.Models;

namespace UDS_Print_Master.Services;

public static class PrintSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static PrintSettings Load()
    {
        try
        {
            PrintSettings settings;
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                settings = JsonSerializer.Deserialize<PrintSettings>(
                               json,
                               SerializerOptions)
                           ?? new PrintSettings();
            }
            else
            {
                settings = LoadLegacySettings();
            }

            settings.Normalize();
            return settings;
        }
        catch
        {
            return new PrintSettings();
        }
    }

    public static void Save(PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Normalize();
        Directory.CreateDirectory(StoreDirectory);
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(StorePath, json);
    }

    private static PrintSettings LoadLegacySettings()
    {
        var settings = new PrintSettings();
        if (!File.Exists(LegacyStorePath))
        {
            return settings;
        }

        using var document = JsonDocument.Parse(
            File.ReadAllText(LegacyStorePath));
        var firstTemplate = document.RootElement.ValueKind ==
                            JsonValueKind.Array
            ? document.RootElement.EnumerateArray().FirstOrDefault()
            : default;
        if (firstTemplate.ValueKind != JsonValueKind.Object)
        {
            return settings;
        }

        if (firstTemplate.TryGetProperty(
                nameof(PrintSettings.Copies),
                out var copies) &&
            copies.TryGetInt32(out var copiesValue))
        {
            settings.Copies = copiesValue;
        }

        if (firstTemplate.TryGetProperty(
                nameof(PrintSettings.PageOrientation),
                out var orientation))
        {
            settings.PageOrientation =
                orientation.GetString() ?? settings.PageOrientation;
        }

        if (firstTemplate.TryGetProperty(
                nameof(PrintSettings.DuplexMode),
                out var duplex))
        {
            settings.DuplexMode =
                duplex.GetString() ?? settings.DuplexMode;
        }

        return settings;
    }

    private static string StoreDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "UDS_Print_Master");

    private static string StorePath =>
        Path.Combine(StoreDirectory, "print-settings.json");

    private static string LegacyStorePath =>
        Path.Combine(StoreDirectory, "print-templates.json");
}
