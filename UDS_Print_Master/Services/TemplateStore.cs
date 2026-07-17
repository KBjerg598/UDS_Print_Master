using System.IO;
using System.Text.Json;
using UDS_Print_Master.Models;

namespace UDS_Print_Master.Services;

public static class TemplateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static List<PrintTemplate> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return CreateDefaults();
            }

            var json = File.ReadAllText(StorePath);
            var templates = JsonSerializer.Deserialize<List<PrintTemplate>>(json, SerializerOptions);

            return templates is { Count: > 0 } ? templates : CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public static void Save(IEnumerable<PrintTemplate> templates)
    {
        Directory.CreateDirectory(StoreDirectory);
        var json = JsonSerializer.Serialize(templates, SerializerOptions);
        File.WriteAllText(StorePath, json);
    }

    private static string StoreDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UDS_Print_Master");

    private static string StorePath => Path.Combine(StoreDirectory, "print-templates.json");

    private static List<PrintTemplate> CreateDefaults()
    {
        return
        [
            new PrintTemplate
            {
                Name = "Стандартный текст",
                FontFamily = "Segoe UI",
                FontSize = 12,
                MarginLeft = 18,
                MarginTop = 18,
                MarginRight = 18,
                MarginBottom = 18,
                DuplexMode = "OneSided",
                LineSpacing = 1.25,
                IncludeFileNameHeader = true,
                IncludePrintedAtFooter = true,
                WordWrap = true
            },
            new PrintTemplate
            {
                Name = "Технический лог",
                FontFamily = "Consolas",
                FontSize = 10,
                MarginLeft = 12,
                MarginTop = 12,
                MarginRight = 12,
                MarginBottom = 12,
                DuplexMode = "OneSided",
                LineSpacing = 1.1,
                IncludeFileNameHeader = true,
                IncludePrintedAtFooter = false,
                WordWrap = true
            },
            new PrintTemplate
            {
                Name = "Компактная печать",
                FontFamily = "Arial",
                FontSize = 9,
                MarginLeft = 8,
                MarginTop = 8,
                MarginRight = 8,
                MarginBottom = 8,
                DuplexMode = "OneSided",
                LineSpacing = 1,
                IncludeFileNameHeader = false,
                IncludePrintedAtFooter = false,
                WordWrap = true
            }
        ];
    }
}
