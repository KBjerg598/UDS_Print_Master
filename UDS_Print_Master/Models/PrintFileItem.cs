using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace UDS_Print_Master.Models;

public sealed class PrintFileItem : INotifyPropertyChanged
{
    private string _status = "Ожидает";

    public PrintFileItem(string fullPath)
    {
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath);
        Extension = Path.GetExtension(fullPath).ToLowerInvariant();
        SizeLabel = FormatSize(new FileInfo(fullPath).Length);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FullPath { get; }

    public string FileName { get; }

    public string Extension { get; }

    public string SizeLabel { get; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} Б";
        }

        var kilobytes = bytes / 1024d;
        if (kilobytes < 1024)
        {
            return $"{kilobytes:0.#} КБ";
        }

        return $"{kilobytes / 1024d:0.##} МБ";
    }

    private void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
