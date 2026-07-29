using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UDS_Print_Master.Models;

public sealed class PrintSettings : INotifyPropertyChanged
{
    private int _copies = 1;
    private string _pageOrientation = "Portrait";
    private string _duplexMode = "OneSided";
    private string _colorMode = "Color";
    private string _pageRangeMode = "All";
    private int _fromPage = 1;
    private int _toPage = 1;
    private string _pdfPageSizing = "Fit";

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Copies
    {
        get => _copies;
        set => SetProperty(ref _copies, value);
    }

    public string PageOrientation
    {
        get => _pageOrientation;
        set => SetProperty(ref _pageOrientation, value);
    }

    public string DuplexMode
    {
        get => _duplexMode;
        set => SetProperty(ref _duplexMode, value);
    }

    public string ColorMode
    {
        get => _colorMode;
        set => SetProperty(ref _colorMode, value);
    }

    public string PageRangeMode
    {
        get => _pageRangeMode;
        set
        {
            if (SetProperty(ref _pageRangeMode, value))
            {
                OnPropertyChanged(nameof(IsCustomPageRange));
            }
        }
    }

    public int FromPage
    {
        get => _fromPage;
        set => SetProperty(ref _fromPage, value);
    }

    public int ToPage
    {
        get => _toPage;
        set => SetProperty(ref _toPage, value);
    }

    public string PdfPageSizing
    {
        get => _pdfPageSizing;
        set => SetProperty(ref _pdfPageSizing, value);
    }

    public bool IsCustomPageRange => PageRangeMode == "Custom";

    public (int? FromPage, int? ToPage) GetPageRange()
    {
        if (!IsCustomPageRange)
        {
            return (null, null);
        }

        var fromPage = Math.Max(1, FromPage);
        var toPage = Math.Max(1, ToPage);
        if (fromPage > toPage)
        {
            throw new InvalidOperationException(
                "Начальная страница диапазона не может быть больше конечной.");
        }

        return (fromPage, toPage);
    }

    public void Normalize()
    {
        Copies = Math.Clamp(Copies, 1, 99);
        PageOrientation = PageOrientation == "Landscape"
            ? "Landscape"
            : "Portrait";
        DuplexMode = DuplexMode == "TwoSidedLongEdge"
            ? "TwoSidedLongEdge"
            : "OneSided";
        ColorMode = ColorMode == "Monochrome"
            ? "Monochrome"
            : "Color";
        PageRangeMode = PageRangeMode == "Custom"
            ? "Custom"
            : "All";
        FromPage = Math.Max(1, FromPage);
        ToPage = Math.Max(1, ToPage);
        PdfPageSizing = PdfPageSizing == "ActualSize"
            ? "ActualSize"
            : "Fit";
    }

    private bool SetProperty<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
