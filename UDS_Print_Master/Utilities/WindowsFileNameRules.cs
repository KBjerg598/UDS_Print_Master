using System.IO;

namespace UDS_Print_Master.Utilities;

public static class WindowsFileNameRules
{
    public const int MaximumBaseNameLength = 120;

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly HashSet<char> InvalidCharacters = [.. Path.GetInvalidFileNameChars()];

    public static string? GetValidationError(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Введите имя документа.";
        }

        if (name.Length > MaximumBaseNameLength)
        {
            return $"Имя не должно быть длиннее {MaximumBaseNameLength} символов.";
        }

        var invalidCharacter = name
            .Where(InvalidCharacters.Contains)
            .Select(character => (char?)character)
            .FirstOrDefault();
        if (invalidCharacter is char invalid)
        {
            return char.IsControl(invalid)
                ? "Имя содержит недопустимый управляющий символ."
                : $"Символ «{invalid}» нельзя использовать в имени файла.";
        }

        if (name.StartsWith(' ') || name.EndsWith(' ') || name.EndsWith('.'))
        {
            return "Имя файла не может начинаться с пробела или заканчиваться пробелом либо точкой.";
        }

        var deviceName = name.Split('.', 2)[0].TrimEnd(' ', '.');
        if (ReservedDeviceNames.Contains(deviceName))
        {
            return $"Имя «{deviceName}» зарезервировано Windows.";
        }

        return null;
    }

    public static string Sanitize(string? name, string fallback = "Документ")
    {
        var source = string.IsNullOrWhiteSpace(name) ? fallback : name;
        var safeCharacters = source
            .Select(character => InvalidCharacters.Contains(character) ? '_' : character)
            .ToArray();
        var safeName = new string(safeCharacters).Trim().TrimEnd('.');

        if (safeName.Length > MaximumBaseNameLength)
        {
            safeName = safeName[..MaximumBaseNameLength].TrimEnd(' ', '.');
        }

        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = fallback;
        }

        var deviceName = safeName.Split('.', 2)[0];
        if (ReservedDeviceNames.Contains(deviceName))
        {
            safeName = $"_{safeName}";
        }

        return safeName.Length <= MaximumBaseNameLength
            ? safeName
            : safeName[..MaximumBaseNameLength].TrimEnd(' ', '.');
    }
}
