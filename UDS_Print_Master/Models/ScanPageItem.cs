using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace UDS_Print_Master.Models;

public sealed class ScanPageItem : INotifyPropertyChanged
{
    private bool _isDocumentStart;
    private int _pageNumber;
    private int _documentNumber;
    private int _pageInDocument;

    public ScanPageItem(string imagePath, BitmapSource thumbnail)
    {
        ImagePath = imagePath;
        Thumbnail = thumbnail;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ImagePath { get; }

    public BitmapSource Thumbnail { get; }

    public bool IsDocumentStart
    {
        get => _isDocumentStart;
        set => SetProperty(ref _isDocumentStart, value);
    }

    public int PageNumber
    {
        get => _pageNumber;
        set
        {
            if (SetProperty(ref _pageNumber, value))
            {
                OnPropertyChanged(nameof(PageLabel));
            }
        }
    }

    public int DocumentNumber
    {
        get => _documentNumber;
        set
        {
            if (SetProperty(ref _documentNumber, value))
            {
                OnPropertyChanged(nameof(DocumentLabel));
            }
        }
    }

    public int PageInDocument
    {
        get => _pageInDocument;
        set
        {
            if (SetProperty(ref _pageInDocument, value))
            {
                OnPropertyChanged(nameof(PageLabel));
            }
        }
    }

    public string DocumentLabel => $"Документ {DocumentNumber}";

    public string PageLabel => $"Страница {PageInDocument}";

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
