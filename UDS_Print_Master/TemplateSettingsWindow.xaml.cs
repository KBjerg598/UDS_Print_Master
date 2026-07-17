using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using UDS_Print_Master.Models;

namespace UDS_Print_Master;

public partial class TemplateSettingsWindow : Window, INotifyPropertyChanged
{
    private readonly string _previewText;
    private readonly string _previewFileName;
    private string _previewTitle;
    private bool _isPreviewDragging;
    private Point _previewDragStart;
    private double _previewDragStartHorizontalOffset;
    private double _previewDragStartVerticalOffset;
    private ScrollViewer? _previewScrollViewer;

    public TemplateSettingsWindow(
        PrintTemplate template,
        IEnumerable<string> fontFamilies,
        string previewText,
        string previewFileName)
    {
        PrintTemplate = template;
        FontFamilies = new ObservableCollection<string>(fontFamilies);
        _previewText = string.IsNullOrWhiteSpace(previewText) ? CreateDefaultPreviewText() : previewText;
        _previewFileName = string.IsNullOrWhiteSpace(previewFileName) ? "Пример файла.txt" : previewFileName;
        _previewTitle = $"Шаблон: {PrintTemplate.Name}. Файл: {_previewFileName}";

        InitializeComponent();
        DataContext = this;

        PrintTemplate.PropertyChanged += Template_PropertyChanged;
        Loaded += (_, _) => RefreshPreview();
        Closed += (_, _) => PrintTemplate.PropertyChanged -= Template_PropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PrintTemplate PrintTemplate { get; }

    public ObservableCollection<string> FontFamilies { get; }

    public string PreviewTitle
    {
        get => _previewTitle;
        private set
        {
            if (_previewTitle == value)
            {
                return;
            }

            _previewTitle = value;
            OnPropertyChanged();
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Template_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrintTemplate.Name))
        {
            PreviewTitle = $"Шаблон: {PrintTemplate.Name}. Файл: {_previewFileName}";
        }

        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (!IsLoaded)
        {
            return;
        }

        var fontSize = Math.Clamp(PrintTemplate.FontSize, 8, 72);
        var pageWidth = PrintTemplate.PageOrientation == "Landscape" ? 980d : 700d;
        var pageHeight = PrintTemplate.PageOrientation == "Landscape" ? 700d : 980d;

        var document = new FlowDocument
        {
            PageWidth = pageWidth,
            MinPageWidth = pageWidth,
            MaxPageWidth = pageWidth,
            PageHeight = pageHeight,
            PagePadding = new Thickness(
                MmToDiu(PrintTemplate.MarginLeft),
                MmToDiu(PrintTemplate.MarginTop),
                MmToDiu(PrintTemplate.MarginRight),
                MmToDiu(PrintTemplate.MarginBottom)),
            ColumnWidth = PrintTemplate.WordWrap ? double.PositiveInfinity : 100000,
            FontFamily = new FontFamily(string.IsNullOrWhiteSpace(PrintTemplate.FontFamily) ? "Segoe UI" : PrintTemplate.FontFamily),
            FontSize = fontSize,
            Background = Brushes.White,
            TextAlignment = TextAlignment.Left
        };

        if (PrintTemplate.IncludeFileNameHeader)
        {
            document.Blocks.Add(new Paragraph(new Run(_previewFileName))
            {
                FontSize = fontSize + 2,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });
        }

        document.Blocks.Add(new Paragraph(new Run(TrimPreviewText(_previewText)))
        {
            Margin = new Thickness(0),
            LineHeight = fontSize * Math.Clamp(PrintTemplate.LineSpacing, 1, 3)
        });

        if (PrintTemplate.IncludePrintedAtFooter)
        {
            document.Blocks.Add(new Paragraph(new Run($"Напечатано: {DateTime.Now:dd.MM.yyyy HH:mm}"))
            {
                Foreground = Brushes.Gray,
                FontSize = Math.Max(fontSize - 1, 8),
                Margin = new Thickness(0, 14, 0, 0)
            });
        }

        PreviewViewer.Document = document;
    }

    private void PreviewDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _previewScrollViewer ??= FindVisualChild<ScrollViewer>(PreviewViewer);
        if (_previewScrollViewer is null)
        {
            return;
        }

        if (IsFromScrollControl(e.OriginalSource, DetailFlowPreviewHost))
        {
            return;
        }

        _isPreviewDragging = true;
        _previewDragStart = e.GetPosition(DetailFlowPreviewHost);
        _previewDragStartHorizontalOffset = _previewScrollViewer.HorizontalOffset;
        _previewDragStartVerticalOffset = _previewScrollViewer.VerticalOffset;
        DetailFlowPreviewHost.Cursor = Cursors.SizeAll;
        DetailFlowPreviewHost.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewDrag_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPreviewDragging || _previewScrollViewer is null)
        {
            return;
        }

        var currentPosition = e.GetPosition(DetailFlowPreviewHost);
        var delta = currentPosition - _previewDragStart;
        _previewScrollViewer.ScrollToHorizontalOffset(_previewDragStartHorizontalOffset - delta.X);
        _previewScrollViewer.ScrollToVerticalOffset(_previewDragStartVerticalOffset - delta.Y);
        e.Handled = true;
    }

    private void PreviewDrag_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopPreviewDrag();
        e.Handled = true;
    }

    private void PreviewDrag_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopPreviewDrag();
        }
    }

    private void StopPreviewDrag()
    {
        if (!_isPreviewDragging)
        {
            return;
        }

        _isPreviewDragging = false;
        DetailFlowPreviewHost.Cursor = Cursors.Hand;
        if (DetailFlowPreviewHost.IsMouseCaptured)
        {
            DetailFlowPreviewHost.ReleaseMouseCapture();
        }
    }

    private static bool IsFromScrollControl(object originalSource, DependencyObject previewRoot)
    {
        if (originalSource is not DependencyObject current)
        {
            return false;
        }

        while (current != previewRoot)
        {
            if (current is ScrollBar or Thumb or RepeatButton)
            {
                return true;
            }

            var parent = GetSafeParent(current);
            if (parent is null)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        if (parent is not Visual and not System.Windows.Media.Media3D.Visual3D)
        {
            return null;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var nestedChild = FindVisualChild<T>(child);
            if (nestedChild is not null)
            {
                return nestedChild;
            }
        }

        return null;
    }

    private static DependencyObject? GetSafeParent(DependencyObject current)
    {
        if (current is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            var visualParent = VisualTreeHelper.GetParent(current);
            if (visualParent is not null)
            {
                return visualParent;
            }
        }

        return LogicalTreeHelper.GetParent(current);
    }

    private static string TrimPreviewText(string text)
    {
        const int maxPreviewLength = 4500;
        return text.Length <= maxPreviewLength
            ? text
            : text[..maxPreviewLength] + Environment.NewLine + Environment.NewLine + "... предпросмотр сокращен ...";
    }

    private static string CreateDefaultPreviewText()
    {
        return "UISI Print Master" + Environment.NewLine +
               Environment.NewLine +
               "Это пример печатного текста. Здесь видно, как будут выглядеть шрифт, поля страницы, межстрочный интервал, заголовок и перенос длинных строк." + Environment.NewLine +
               Environment.NewLine +
               "При изменении параметров слева предпросмотр обновляется сразу, поэтому шаблон можно настраивать наглядно.";
    }

    private static double MmToDiu(double millimeters)
    {
        if (!double.IsFinite(millimeters))
        {
            millimeters = 15;
        }

        return Math.Clamp(millimeters, 0, 100) / 25.4 * 96;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
