using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UDS_Print_Master.Models;

public sealed class PrintTemplate : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "Новый шаблон";
    private string _fontFamily = "Segoe UI";
    private double _fontSize = 12;
    private double _marginLeft = 15;
    private double _marginTop = 15;
    private double _marginRight = 15;
    private double _marginBottom = 15;
    private string _pageOrientation = "Portrait";
    private string _duplexMode = "OneSided";
    private int _copies = 1;
    private double _lineSpacing = 1.25;
    private bool _includeFileNameHeader = true;
    private bool _includePrintedAtFooter;
    private bool _wordWrap = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
    }

    public double MarginLeft
    {
        get => _marginLeft;
        set => SetProperty(ref _marginLeft, value);
    }

    public double MarginTop
    {
        get => _marginTop;
        set => SetProperty(ref _marginTop, value);
    }

    public double MarginRight
    {
        get => _marginRight;
        set => SetProperty(ref _marginRight, value);
    }

    public double MarginBottom
    {
        get => _marginBottom;
        set => SetProperty(ref _marginBottom, value);
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

    public int Copies
    {
        get => _copies;
        set => SetProperty(ref _copies, value);
    }

    public double LineSpacing
    {
        get => _lineSpacing;
        set => SetProperty(ref _lineSpacing, value);
    }

    public bool IncludeFileNameHeader
    {
        get => _includeFileNameHeader;
        set => SetProperty(ref _includeFileNameHeader, value);
    }

    public bool IncludePrintedAtFooter
    {
        get => _includePrintedAtFooter;
        set => SetProperty(ref _includePrintedAtFooter, value);
    }

    public bool WordWrap
    {
        get => _wordWrap;
        set => SetProperty(ref _wordWrap, value);
    }

    public PrintTemplate CloneAs(string name)
    {
        return new PrintTemplate
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            FontFamily = FontFamily,
            FontSize = FontSize,
            MarginLeft = MarginLeft,
            MarginTop = MarginTop,
            MarginRight = MarginRight,
            MarginBottom = MarginBottom,
            PageOrientation = PageOrientation,
            DuplexMode = DuplexMode,
            Copies = Copies,
            LineSpacing = LineSpacing,
            IncludeFileNameHeader = IncludeFileNameHeader,
            IncludePrintedAtFooter = IncludePrintedAtFooter,
            WordWrap = WordWrap
        };
    }

    public void CopyFrom(PrintTemplate source)
    {
        Id = source.Id;
        Name = source.Name;
        FontFamily = source.FontFamily;
        FontSize = source.FontSize;
        MarginLeft = source.MarginLeft;
        MarginTop = source.MarginTop;
        MarginRight = source.MarginRight;
        MarginBottom = source.MarginBottom;
        PageOrientation = source.PageOrientation;
        DuplexMode = source.DuplexMode;
        Copies = source.Copies;
        LineSpacing = source.LineSpacing;
        IncludeFileNameHeader = source.IncludeFileNameHeader;
        IncludePrintedAtFooter = source.IncludePrintedAtFooter;
        WordWrap = source.WordWrap;
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
