using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Printing;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UDS_Print_Master.Models;
using UDS_Print_Master.Services;
using UDS_Print_Master.Utilities;

namespace UDS_Print_Master;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".csv", ".json", ".xml", ".md", ".markdown", ".ini", ".cfg", ".config",
        ".sql", ".html", ".htm", ".css", ".js", ".ts", ".cs", ".xaml", ".yaml", ".yml"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".rtf"
    };

    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private static readonly HashSet<string> SupportedExtensions = new(
        TextExtensions.Concat(DocumentExtensions).Concat(PdfExtensions),
        StringComparer.OrdinalIgnoreCase);

    private static readonly char[] Windows1251Chars =
    [
        '\u0402', '\u0403', '\u201A', '\u0453', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u20AC', '\u2030', '\u0409', '\u2039', '\u040A', '\u040C', '\u040B', '\u040F',
        '\u0452', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\uFFFD', '\u2122', '\u0459', '\u203A', '\u045A', '\u045C', '\u045B', '\u045F',
        '\u00A0', '\u040E', '\u045E', '\u0408', '\u00A4', '\u0490', '\u00A6', '\u00A7',
        '\u0401', '\u00A9', '\u0404', '\u00AB', '\u00AC', '\u00AD', '\u00AE', '\u0407',
        '\u00B0', '\u00B1', '\u0406', '\u0456', '\u0491', '\u00B5', '\u00B6', '\u00B7',
        '\u0451', '\u2116', '\u0454', '\u00BB', '\u0458', '\u0405', '\u0455', '\u0457',
        '\u0410', '\u0411', '\u0412', '\u0413', '\u0414', '\u0415', '\u0416', '\u0417',
        '\u0418', '\u0419', '\u041A', '\u041B', '\u041C', '\u041D', '\u041E', '\u041F',
        '\u0420', '\u0421', '\u0422', '\u0423', '\u0424', '\u0425', '\u0426', '\u0427',
        '\u0428', '\u0429', '\u042A', '\u042B', '\u042C', '\u042D', '\u042E', '\u042F',
        '\u0430', '\u0431', '\u0432', '\u0433', '\u0434', '\u0435', '\u0436', '\u0437',
        '\u0438', '\u0439', '\u043A', '\u043B', '\u043C', '\u043D', '\u043E', '\u043F',
        '\u0440', '\u0441', '\u0442', '\u0443', '\u0444', '\u0445', '\u0446', '\u0447',
        '\u0448', '\u0449', '\u044A', '\u044B', '\u044C', '\u044D', '\u044E', '\u044F'
    ];

    private PrintSettings _printSettings = new();
    private string _selectedPrinterName = string.Empty;
    private string _currentPrintingFile = "Очередь не запущена";
    private string _overallStatus = "Добавьте файлы и выберите принтер.";
    private string _conversionStatus = "Готово к конвертации.";
    private double _progressValue;
    private double _conversionProgressValue;
    private bool _isPrinting;
    private bool _isConverting;
    private int _previewVersion;
    private bool _isPreviewDragging;
    private Point _previewDragStart;
    private double _previewDragStartHorizontalOffset;
    private double _previewDragStartVerticalOffset;
    private ScrollViewer? _previewScrollViewer;
    private bool _isPdfPreviewDragging;
    private Point _pdfPreviewDragStart;
    private readonly SemaphoreSlim _pdfConversionLock = new(1, 1);
    private readonly HashSet<string> _temporaryPdfFiles = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentPreviewPdfPath;
    private string? _currentPreviewSourcePath;
    private DateTime _currentPreviewSourceWriteTimeUtc;
    private readonly WiaScannerService _scannerService = new();
    private readonly PrinterWakeService _printerWakeService = new();
    private ScannerDeviceInfo? _selectedScanner;
    private ScannerCapabilities? _selectedScannerCapabilities;
    private ScanPageItem? _selectedScanPage;
    private BitmapSource? _selectedScanPreview;
    private double _scanPreviewZoom = 100;
    private bool _isScanPreviewExpanded;
    private bool _isScanPreviewDragging;
    private Point _scanPreviewDragStart;
    private double _scanPreviewDragStartHorizontalOffset;
    private double _scanPreviewDragStartVerticalOffset;
    private ScrollViewer? _activeScanPreviewScrollViewer;
    private string _selectedScanSource = "Flatbed";
    private string _selectedScanMode = "Simplex";
    private string _selectedScanColorMode = "Color";
    private int? _selectedScanDpi = 300;
    private string _scanStatus = "Выберите сканер и источник бумаги.";
    private string _scanBaseFileName = "Документ";
    private bool _isScanning;
    private bool _isSavingScans;
    private bool _isWakingScanner;
    private bool _suppressScannerCapabilityLoading;
    private bool _startNextScanAsNewDocument;
    private int _scannerCapabilitiesVersion;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _scannerWakeCancellation;

    public MainWindow()
    {
        InitializeComponent();

        Files.CollectionChanged += Files_CollectionChanged;
        ConversionFiles.CollectionChanged += ConversionFiles_CollectionChanged;
        ScanPages.CollectionChanged += ScanPages_CollectionChanged;
        PrintSettings = PrintSettingsStore.Load();
        DataContext = this;

        Loaded += MainWindow_Loaded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PrintFileItem> Files { get; } = [];

    public ObservableCollection<PrintFileItem> ConversionFiles { get; } = [];

    public ObservableCollection<string> PrinterNames { get; } = [];

    public ObservableCollection<ScannerDeviceInfo> Scanners { get; } = [];

    public ObservableCollection<int> AvailableScanDpi { get; } = [150, 200, 300];

    public ObservableCollection<ScanPageItem> ScanPages { get; } = [];

    public PrintSettings PrintSettings
    {
        get => _printSettings;
        set
        {
            if (ReferenceEquals(_printSettings, value))
            {
                return;
            }

            _printSettings.PropertyChanged -= PrintSettings_PropertyChanged;
            _printSettings = value;
            _printSettings.PropertyChanged += PrintSettings_PropertyChanged;

            OnPropertyChanged();
            if (IsLoaded)
            {
                UpdatePreview();
            }
        }
    }

    public string SelectedPrinterName
    {
        get => _selectedPrinterName;
        set
        {
            if (SetProperty(ref _selectedPrinterName, value))
            {
                RefreshAvailableScanDpi();
            }
        }
    }

    public string CurrentPrintingFile
    {
        get => _currentPrintingFile;
        set => SetProperty(ref _currentPrintingFile, value);
    }

    public string OverallStatus
    {
        get => _overallStatus;
        set => SetProperty(ref _overallStatus, value);
    }

    public string ConversionStatus
    {
        get => _conversionStatus;
        set => SetProperty(ref _conversionStatus, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public double ConversionProgressValue
    {
        get => _conversionProgressValue;
        set => SetProperty(ref _conversionProgressValue, value);
    }

    public ScannerDeviceInfo? SelectedScanner
    {
        get => _selectedScanner;
        set
        {
            if (!SetProperty(ref _selectedScanner, value))
            {
                return;
            }

            RefreshAvailableScanDpi();

            if (!_suppressScannerCapabilityLoading && IsLoaded && MainWorkspaceTabs.SelectedIndex == 2)
            {
                _ = LoadSelectedScannerCapabilitiesAsync();
            }

            OnPropertyChanged(nameof(CanStartScan));
        }
    }

    public ScanPageItem? SelectedScanPage
    {
        get => _selectedScanPage;
        set
        {
            if (SetProperty(ref _selectedScanPage, value))
            {
                SelectedScanPreview = value is null ? null : LoadScanBitmap(value.ImagePath, 2400);
                SetScanPreviewZoom(100);
                if (value is null)
                {
                    IsScanPreviewExpanded = false;
                }
            }
        }
    }

    public BitmapSource? SelectedScanPreview
    {
        get => _selectedScanPreview;
        private set
        {
            if (SetProperty(ref _selectedScanPreview, value))
            {
                OnPropertyChanged(nameof(CanExpandScanPreview));
            }
        }
    }

    public double ScanPreviewScale => _scanPreviewZoom / 100d;

    public string ScanPreviewZoomText => $"{_scanPreviewZoom:0}%";

    public bool CanExpandScanPreview => SelectedScanPreview is not null;

    public bool IsScanPreviewExpanded
    {
        get => _isScanPreviewExpanded;
        private set => SetProperty(ref _isScanPreviewExpanded, value);
    }

    public string SelectedScanSource
    {
        get => _selectedScanSource;
        set
        {
            if (SetProperty(ref _selectedScanSource, value))
            {
                RefreshAvailableScanDpi();
                OnPropertyChanged(nameof(CanStartScan));
            }
        }
    }

    public string SelectedScanMode
    {
        get => _selectedScanMode;
        set
        {
            if (SetProperty(ref _selectedScanMode, value))
            {
                OnPropertyChanged(nameof(CanStartScan));
            }
        }
    }

    public string SelectedScanColorMode
    {
        get => _selectedScanColorMode;
        set
        {
            if (SetProperty(ref _selectedScanColorMode, value))
            {
                RefreshAvailableScanDpi();
            }
        }
    }

    public int? SelectedScanDpi
    {
        get => _selectedScanDpi;
        set => SetProperty(ref _selectedScanDpi, value);
    }

    public string ScanStatus
    {
        get => _scanStatus;
        set => SetProperty(ref _scanStatus, value);
    }

    public string ScanBaseFileName
    {
        get => _scanBaseFileName;
        set
        {
            if (SetProperty(ref _scanBaseFileName, value))
            {
                RenumberScanPages();
            }
        }
    }

    public string ScanPagesSummary => ScanPages.Count == 0
        ? "Страницы ещё не отсканированы"
        : $"Страниц: {ScanPages.Count} · документов: {GetScanDocumentCount()}";

    public string ScannerCapabilityText
    {
        get
        {
            if (_selectedScannerCapabilities is null)
            {
                return SelectedScanner is null ? "Сканер не выбран" : "Получение возможностей устройства...";
            }

            var sources = new List<string>();
            if (_selectedScannerCapabilities.HasFlatbed)
            {
                sources.Add("планшет");
            }

            if (_selectedScannerCapabilities.HasFeeder)
            {
                sources.Add("ADF");
            }

            var duplex = _selectedScannerCapabilities.SupportsDuplex
                ? "двустороннее сканирование доступно"
                : "только одностороннее сканирование";
            var modeFiltered = _selectedScannerCapabilities.SupportedDpi.Except(AvailableScanDpi).Any();
            var dpiSource = modeFiltered
                ? "для выбранного источника и цвета"
                : _selectedScannerCapabilities.DpiReportedByDriver
                    ? "сообщены драйвером"
                    : "безопасный диапазон, проверяется при запуске";
            var dpiValues = string.Join(", ", AvailableScanDpi);
            return $"Источники: {string.Join(", ", sources)} · {duplex}\nDPI ({dpiSource}): {dpiValues}";
        }
    }

    private void RefreshAvailableScanDpi()
    {
        IEnumerable<int> values = _selectedScannerCapabilities?.SupportedDpi ?? [150, 200, 300];
        var deviceIdentity = $"{SelectedScanner?.Name} {SelectedPrinterName}";
        if (deviceIdentity.Contains("M521", StringComparison.OrdinalIgnoreCase)
            || deviceIdentity.Contains("M570", StringComparison.OrdinalIgnoreCase))
        {
            var maximumDpi = SelectedScanSource == "Feeder"
                ? 300
                : SelectedScanColorMode == "Color" ? 600 : 1200;
            values = values.Where(value => value <= maximumDpi);
        }

        var supportedValues = values.Distinct().OrderBy(value => value).ToList();
        if (supportedValues.Count == 0)
        {
            supportedValues.Add(300);
        }

        AvailableScanDpi.Clear();
        foreach (var dpi in supportedValues)
        {
            AvailableScanDpi.Add(dpi);
        }

        if (SelectedScanDpi is not int selectedDpi || !AvailableScanDpi.Contains(selectedDpi))
        {
            SelectedScanDpi = AvailableScanDpi.OrderBy(value => Math.Abs(value - 300)).First();
        }

        OnPropertyChanged(nameof(ScannerCapabilityText));
    }

    public bool CanPrint => Files.Count > 0 && !IsPrinting && !IsConverting && !IsScanning && !IsSavingScans;

    public bool CanConvert => ConversionFiles.Count > 0 && !IsConverting && !IsPrinting && !IsScanning && !IsSavingScans;

    public bool CanStartScan => SelectedScanner is not null
                                && _selectedScannerCapabilities is not null
                                && !IsScanning
                                && !IsSavingScans
                                && !IsWakingScanner
                                && !IsPrinting
                                && !IsConverting;

    public bool CanStopScan => IsScanning;

    public bool CanSaveScans => ScanPages.Count > 0 && !IsScanning && !IsSavingScans && !IsConverting;

    public bool CanEditScans => ScanPages.Count > 0 && !IsScanning && !IsSavingScans;

    public bool CanWakeScanner => !IsScanning && !IsSavingScans && !IsWakingScanner && !IsPrinting && !IsConverting;

    public bool IsScannerBusy => IsScanning || IsWakingScanner;

    public string FilesSummary => Files.Count == 0
        ? "Очередь пустая"
        : $"В очереди документов: {Files.Count}";

    public string ConversionSummary
    {
        get
        {
            var documentCount = ConversionFiles.Count;
            return documentCount == 0
                ? "Документы не добавлены"
                : $"Документов для PDF: {documentCount}";
        }
    }

    private bool IsPrinting
    {
        get => _isPrinting;
        set
        {
            if (SetProperty(ref _isPrinting, value))
            {
                OnPropertyChanged(nameof(CanPrint));
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(CanStartScan));
                OnPropertyChanged(nameof(CanWakeScanner));
            }
        }
    }

    private bool IsConverting
    {
        get => _isConverting;
        set
        {
            if (SetProperty(ref _isConverting, value))
            {
                OnPropertyChanged(nameof(CanPrint));
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(CanStartScan));
                OnPropertyChanged(nameof(CanSaveScans));
                OnPropertyChanged(nameof(CanWakeScanner));
            }
        }
    }

    private bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(CanStartScan));
                OnPropertyChanged(nameof(CanStopScan));
                OnPropertyChanged(nameof(CanSaveScans));
                OnPropertyChanged(nameof(CanEditScans));
                OnPropertyChanged(nameof(CanPrint));
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(CanWakeScanner));
                OnPropertyChanged(nameof(IsScannerBusy));
            }
        }
    }

    private bool IsSavingScans
    {
        get => _isSavingScans;
        set
        {
            if (SetProperty(ref _isSavingScans, value))
            {
                OnPropertyChanged(nameof(CanStartScan));
                OnPropertyChanged(nameof(CanSaveScans));
                OnPropertyChanged(nameof(CanEditScans));
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(CanPrint));
                OnPropertyChanged(nameof(CanWakeScanner));
            }
        }
    }

    private bool IsWakingScanner
    {
        get => _isWakingScanner;
        set
        {
            if (SetProperty(ref _isWakingScanner, value))
            {
                OnPropertyChanged(nameof(CanStartScan));
                OnPropertyChanged(nameof(CanWakeScanner));
                OnPropertyChanged(nameof(IsScannerBusy));
            }
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
        await LoadPrintersAsync();
        await RefreshScannersAsync(loadCapabilities: false);
    }

    private async void MainWorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainWorkspaceTabs || PrintSettingsPanel is null || WorkspacePanel is null || PrintPreviewPanel is null)
        {
            return;
        }

        var scanningWorkspace = MainWorkspaceTabs.SelectedIndex == 2;
        PrintSettingsPanel.Visibility = scanningWorkspace ? Visibility.Collapsed : Visibility.Visible;
        PrintPreviewPanel.Visibility = scanningWorkspace ? Visibility.Collapsed : Visibility.Visible;
        PrintQueueButton.Visibility = scanningWorkspace ? Visibility.Collapsed : Visibility.Visible;

        Grid.SetColumn(WorkspacePanel, scanningWorkspace ? 0 : 1);
        Grid.SetColumnSpan(WorkspacePanel, scanningWorkspace ? 3 : 1);
        WorkspacePanel.Margin = scanningWorkspace ? new Thickness(0) : new Thickness(0, 0, 16, 0);

        if (scanningWorkspace && IsLoaded)
        {
            await WakeScannerAndRefreshAsync();
        }
        else if (!scanningWorkspace)
        {
            _scannerWakeCancellation?.Cancel();
            IsScanPreviewExpanded = false;
        }
    }

    private async Task LoadPrintersAsync()
    {
        var previousStatus = OverallStatus;
        OverallStatus = "Поиск доступных принтеров...";

        try
        {
            var result = await Task.Run(() =>
            {
                using var localServer = new LocalPrintServer();
                var names = localServer
                    .GetPrintQueues()
                    .Select(queue => queue.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                var defaultPrinterName = LocalPrintServer.GetDefaultPrintQueue()?.Name;
                return (Names: names, DefaultPrinterName: defaultPrinterName);
            });

            PrinterNames.Clear();
            foreach (var printerName in result.Names)
            {
                PrinterNames.Add(printerName);
            }

            SelectedPrinterName = result.DefaultPrinterName
                                  ?? PrinterNames.FirstOrDefault()
                                  ?? string.Empty;
            OverallStatus = previousStatus;
        }
        catch
        {
            OverallStatus = "Принтеры пока не найдены. Обновите подключение и перезапустите программу.";
        }
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите документы для печати",
            Multiselect = true,
            Filter = "Поддерживаемые документы|*.txt;*.log;*.csv;*.json;*.xml;*.md;*.ini;*.cfg;*.config;*.sql;*.html;*.css;*.js;*.ts;*.cs;*.xaml;*.yaml;*.yml;*.rtf;*.docx;*.pdf|Текстовые файлы|*.txt;*.log;*.csv;*.json;*.xml;*.md;*.ini;*.cfg;*.config;*.sql;*.html;*.css;*.js;*.ts;*.cs;*.xaml;*.yaml;*.yml|Документы|*.docx;*.rtf|PDF|*.pdf|Все файлы|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private void AddConversionFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите документы для конвертации",
            Multiselect = true,
            Filter = "Поддерживаемые документы|*.txt;*.log;*.csv;*.json;*.xml;*.md;*.ini;*.cfg;*.config;*.sql;*.html;*.css;*.js;*.ts;*.cs;*.xaml;*.yaml;*.yml;*.rtf;*.docx|Текстовые файлы|*.txt;*.log;*.csv;*.json;*.xml;*.md;*.ini;*.cfg;*.config;*.sql;*.html;*.css;*.js;*.ts;*.cs;*.xaml;*.yaml;*.yml|Документы|*.docx;*.rtf|Все файлы|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            AddConversionFiles(dialog.FileNames);
        }
    }

    private void RemoveSelectedConversionFiles_Click(object sender, RoutedEventArgs e)
    {
        var selectedFiles = ConversionFilesGrid.SelectedItems.Cast<PrintFileItem>().ToList();
        foreach (var file in selectedFiles)
        {
            ConversionFiles.Remove(file);
        }
    }

    private void ClearConversionFiles_Click(object sender, RoutedEventArgs e)
    {
        if (ConversionFiles.Count == 0)
        {
            return;
        }

        ConversionFiles.Clear();
        ConversionProgressValue = 0;
        ConversionStatus = "Список конвертации очищен.";
    }

    private void RemoveSelectedFiles_Click(object sender, RoutedEventArgs e)
    {
        var selectedFiles = FilesGrid.SelectedItems.Cast<PrintFileItem>().ToList();
        foreach (var file in selectedFiles)
        {
            Files.Remove(file);
        }

        UpdatePreview();
    }

    private void ClearFiles_Click(object sender, RoutedEventArgs e)
    {
        if (Files.Count == 0)
        {
            return;
        }

        if (MessageBox.Show(this, "Очистить всю очередь документов?", "Очистка очереди", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Files.Clear();
        CurrentPrintingFile = "Очередь не запущена";
        ProgressValue = 0;
        OverallStatus = "Очередь очищена.";
        UpdatePreview();
    }

    private void ResetPrintSettings_Click(object sender, RoutedEventArgs e)
    {
        PrintSettings = new PrintSettings();
        OverallStatus = "Параметры печати сброшены.";
        UpdatePreview();
    }

    private async void PrintQueue_Click(object sender, RoutedEventArgs e)
    {
        if (IsPrinting)
        {
            return;
        }

        if (Files.Count == 0)
        {
            MessageBox.Show(this, "Добавьте документы для печати.", "Печать", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PrintDialog printDialog;
        try
        {
            PrintSettings.Normalize();
            PrintSettings.GetPageRange();
            PrintSettingsStore.Save(PrintSettings);
            printDialog = CreatePrintDialog(PrintSettings);
        }
        catch (Exception ex)
        {
            OverallStatus = $"Не удалось подготовить принтер: {ex.Message}";
            MessageBox.Show(this, OverallStatus, "Печать", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsPrinting = true;
        ProgressValue = 0;
        var printedCount = 0;
        var processedCount = 0;
        var errorCount = 0;
        var printErrors = new List<string>();

        try
        {
            foreach (var file in Files)
            {
                file.Status = "Печатается";
                CurrentPrintingFile = file.FileName;
                OverallStatus = $"Печатается {processedCount + 1} из {Files.Count}: {file.FileName}";
                await Dispatcher.Yield(DispatcherPriority.Background);

                try
                {
                    ApplyPrintSettingsToPrintTicket(printDialog, PrintSettings);
                    if (IsDocxFile(file))
                    {
                        await PrintDocxFileAsync(file, printDialog, PrintSettings);
                    }
                    else if (IsInternalPrintFile(file))
                    {
                        PrintTextFile(printDialog, file, PrintSettings);
                    }
                    else if (PdfExtensions.Contains(file.Extension))
                    {
                        await PrintPdfFileAsync(file.FullPath, printDialog, PrintSettings, file.FileName);
                    }
                    else
                    {
                        throw new NotSupportedException("Этот формат пока не поддерживается встроенной печатью.");
                    }

                    file.Status = "Готово";
                    printedCount++;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    file.Status = "Ошибка";
                    var errorMessage = ex.GetBaseException().Message;
                    printErrors.Add($"{file.FileName}: {errorMessage}");
                    OverallStatus = $"Не удалось напечатать {file.FileName}: {errorMessage}";
                }

                processedCount++;
                ProgressValue = Files.Count == 0 ? 0 : processedCount * 100d / Files.Count;
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }
        finally
        {
            IsPrinting = false;
            CurrentPrintingFile = "Очередь завершена";
            ProgressValue = Files.Count == 0 ? 0 : 100;
            OverallStatus = errorCount == 0
                ? $"Готово. Отправлено в печать документов: {printedCount}."
                : $"Готово с ошибками. Отправлено: {printedCount}, ошибок: {errorCount}.";
            UpdatePreview();

            if (printErrors.Count > 0)
            {
                MessageBox.Show(
                    this,
                    $"Не удалось напечатать:\n\n{string.Join("\n", printErrors)}",
                    "Ошибки печати",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            AddFiles(paths);
        }
    }

    private void Conversion_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Conversion_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            AddConversionFiles(paths);
        }

        e.Handled = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _scanCancellation?.Cancel();
        _scannerWakeCancellation?.Cancel();
        SavePrintSettings();
        CleanupTemporaryPdfFiles();
        CleanupScanPages();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            PdfPreviewBrowser.Dispose();
        }
        catch
        {
            // The preview may already be disposed during application shutdown.
        }

        try
        {
            ConversionPdfBrowser.Dispose();
        }
        catch
        {
            // The converter may already be disposed during application shutdown.
        }

        base.OnClosed(e);
    }

    private async void RefreshScanners_Click(object sender, RoutedEventArgs e)
    {
        await WakeScannerAndRefreshAsync();
    }

    private async Task<bool> RefreshScannersAsync(bool loadCapabilities = true)
    {
        var selectedDeviceId = SelectedScanner?.DeviceId;
        ScanStatus = "Поиск сканеров...";
        _selectedScannerCapabilities = null;
        OnPropertyChanged(nameof(ScannerCapabilityText));
        OnPropertyChanged(nameof(CanStartScan));

        try
        {
            var devices = await _scannerService.GetScannersAsync();
            Scanners.Clear();
            foreach (var device in devices)
            {
                Scanners.Add(device);
            }

            _suppressScannerCapabilityLoading = true;
            try
            {
                SelectedScanner = Scanners.FirstOrDefault(device =>
                                      string.Equals(device.DeviceId, selectedDeviceId, StringComparison.OrdinalIgnoreCase))
                                  ?? Scanners.FirstOrDefault();
            }
            finally
            {
                _suppressScannerCapabilityLoading = false;
            }

            if (SelectedScanner is null)
            {
                ScanStatus = "Сканеры не найдены. Проверьте подключение и драйвер WIA.";
                return false;
            }

            return !loadCapabilities || await LoadSelectedScannerCapabilitiesAsync();
        }
        catch (Exception ex)
        {
            Scanners.Clear();
            _suppressScannerCapabilityLoading = true;
            try
            {
                SelectedScanner = null;
            }
            finally
            {
                _suppressScannerCapabilityLoading = false;
            }

            ScanStatus = $"Не удалось получить список сканеров: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> LoadSelectedScannerCapabilitiesAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var version = ++_scannerCapabilitiesVersion;
        var scanner = SelectedScanner;
        _selectedScannerCapabilities = null;
        OnPropertyChanged(nameof(ScannerCapabilityText));
        OnPropertyChanged(nameof(CanStartScan));

        if (scanner is null)
        {
            return false;
        }

        ScanStatus = $"Проверка возможностей: {scanner.Name}";

        try
        {
            var capabilities = await _scannerService.GetCapabilitiesAsync(scanner.DeviceId)
                .WaitAsync(timeout ?? TimeSpan.FromSeconds(20), cancellationToken);
            if (version != _scannerCapabilitiesVersion || SelectedScanner?.DeviceId != scanner.DeviceId)
            {
                return false;
            }

            _selectedScannerCapabilities = capabilities;
            RefreshAvailableScanDpi();

            if (SelectedScanSource == "Feeder" && !capabilities.HasFeeder)
            {
                SelectedScanSource = "Flatbed";
            }
            else if (SelectedScanSource == "Flatbed" && !capabilities.HasFlatbed && capabilities.HasFeeder)
            {
                SelectedScanSource = "Feeder";
            }

            if (SelectedScanMode == "Duplex" && !capabilities.SupportsDuplex)
            {
                SelectedScanMode = "Simplex";
            }

            ScanStatus = "Сканер готов.";
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (version != _scannerCapabilitiesVersion)
            {
                return false;
            }

            ScanStatus = ex is TimeoutException
                ? "Сканер слишком долго не отвечает. Проверьте, включено ли устройство, и нажмите обновление."
                : $"Не удалось прочитать возможности сканера: {ex.Message}";
            return false;
        }
        finally
        {
            OnPropertyChanged(nameof(ScannerCapabilityText));
            OnPropertyChanged(nameof(CanStartScan));
        }
    }

    private async Task WakeScannerAndRefreshAsync()
    {
        if (IsWakingScanner || IsScanning || IsSavingScans || IsPrinting || IsConverting)
        {
            return;
        }

        _scannerWakeCancellation?.Cancel();
        _scannerWakeCancellation?.Dispose();
        _scannerWakeCancellation = new CancellationTokenSource();
        var cancellationToken = _scannerWakeCancellation.Token;
        IsWakingScanner = true;
        _selectedScannerCapabilities = null;
        OnPropertyChanged(nameof(ScannerCapabilityText));
        OnPropertyChanged(nameof(CanStartScan));

        try
        {
            ScanStatus = "Пробуждение принтера и сканера...";
            var wakeResult = await _printerWakeService.WakeAsync(SelectedPrinterName, SelectedScanner?.Name, cancellationToken);

            var initialDelay = wakeResult.NetworkTargetFound
                ? wakeResult.DeviceResponded ? TimeSpan.FromMilliseconds(700) : TimeSpan.FromSeconds(2.5)
                : TimeSpan.FromMilliseconds(500);
            await Task.Delay(initialDelay, cancellationToken);

            var scannerFound = await RefreshScannersAsync(loadCapabilities: false);
            if (scannerFound)
            {
                ScanStatus = wakeResult.ScannerAddressChanged
                    ? "Найден новый сетевой адрес устройства. Подключение сканера может занять до двух минут..."
                    : "Подключение к сканеру. После глубокого сна это может занять до двух минут...";

                var connectionTimer = Stopwatch.StartNew();
                var connectionTask = LoadSelectedScannerCapabilitiesAsync(TimeSpan.FromMinutes(2), cancellationToken);
                while (!connectionTask.IsCompleted)
                {
                    var completedTask = await Task.WhenAny(
                        connectionTask,
                        Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
                    if (completedTask == connectionTask)
                    {
                        break;
                    }

                    var elapsedSeconds = Math.Max(1, (int)connectionTimer.Elapsed.TotalSeconds);
                    ScanStatus = $"Сканер запускается и устанавливает служебное соединение... {elapsedSeconds} с из 120 с.";
                }

                if (await connectionTask)
                {
                    ScanStatus = wakeResult.NetworkTargetFound && !string.IsNullOrWhiteSpace(wakeResult.Address)
                        ? $"Сканер готов. Текущий сетевой адрес: {wakeResult.Address}."
                        : "Сканер готов.";
                    return;
                }
            }

            ScanStatus = wakeResult.NetworkTargetFound
                ? "Сканер не ответил за две минуты. Если устройство полностью выключено, включите его кнопкой питания."
                : "Сканер не ответил. Для сетевого пробуждения выберите соответствующий принтер или включите устройство кнопкой питания.";
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Подключение к сканеру остановлено.";
        }
        catch (Exception ex)
        {
            ScanStatus = $"Не удалось пробудить сканер: {ex.Message}";
        }
        finally
        {
            IsWakingScanner = false;
            _scannerWakeCancellation?.Dispose();
            _scannerWakeCancellation = null;
        }
    }

    private async void StartScan_Click(object sender, RoutedEventArgs e)
    {
        var scanner = SelectedScanner;
        var capabilities = _selectedScannerCapabilities;
        if (scanner is null || capabilities is null)
        {
            MessageBox.Show(this, "Выберите доступный сканер.", "Сканирование", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var useFeeder = SelectedScanSource == "Feeder";
        var duplex = SelectedScanMode == "Duplex";
        var selectedDpi = SelectedScanDpi ?? AvailableScanDpi.FirstOrDefault(300);

        if (!AvailableScanDpi.Contains(selectedDpi))
        {
            var supportedValues = string.Join(", ", AvailableScanDpi);
            MessageBox.Show(
                this,
                $"Сканер не поддерживает {selectedDpi} DPI в текущей конфигурации. Доступные значения: {supportedValues} DPI.",
                "Разрешение сканирования",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (useFeeder && !capabilities.HasFeeder)
        {
            MessageBox.Show(this, "Выбранный сканер не поддерживает автоподатчик (ADF). Выберите планшет.", "Сканирование", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!useFeeder && !capabilities.HasFlatbed)
        {
            MessageBox.Show(this, "Выбранный сканер не поддерживает планшетное сканирование. Выберите автоподатчик (ADF).", "Сканирование", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (duplex && !useFeeder)
        {
            MessageBox.Show(this, "Двустороннее сканирование доступно только через автоподатчик (ADF).", "Сканирование", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (duplex && !capabilities.SupportsDuplex)
        {
            MessageBox.Show(this, "Этот сканер не поддерживает двустороннее сканирование. Выберите односторонний режим.", "Сканирование", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tempFolder = Path.Combine(Path.GetTempPath(), "UISI Print Master", "Scans");
        var request = new ScanRequest(
            scanner.DeviceId,
            useFeeder,
            duplex,
            selectedDpi,
            SelectedScanColorMode,
            tempFolder);

        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        ScanStatus = useFeeder ? "Сканирование пачки из автоподатчика..." : "Сканирование листа на планшете...";

        var receivedPages = 0;
        var progress = new Progress<string>(path =>
        {
            try
            {
                AddScannedPage(path);
                receivedPages++;
                ScanStatus = $"Получено страниц: {receivedPages}. Сканирование продолжается...";
            }
            catch (Exception ex)
            {
                DeleteFileIfExists(path);
                ScanStatus = $"Не удалось показать страницу: {ex.Message}";
            }
        });

        try
        {
            await _scannerService.ScanAsync(request, progress, _scanCancellation.Token);
            await Dispatcher.Yield(DispatcherPriority.Background);
            ScanStatus = receivedPages == 0
                ? "Сканирование завершено, но страницы не получены."
                : $"Сканирование завершено. Добавлено страниц: {receivedPages}.";
        }
        catch (OperationCanceledException)
        {
            ScanStatus = $"Сканирование остановлено. Получено страниц: {receivedPages}.";
        }
        catch (Exception ex)
        {
            ScanStatus = $"Ошибка сканирования: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Сканирование", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsScanning = false;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    private void StopScan_Click(object sender, RoutedEventArgs e)
    {
        ScanStatus = "Остановка после текущей страницы...";
        _scanCancellation?.Cancel();
    }

    private void AddScannedPage(string imagePath)
    {
        var thumbnail = LoadScanBitmap(imagePath, 220);
        var item = new ScanPageItem(imagePath, thumbnail)
        {
            IsDocumentStart = ScanPages.Count == 0 || _startNextScanAsNewDocument
        };

        _startNextScanAsNewDocument = false;
        ScanPages.Add(item);
        RenumberScanPages();
        SelectedScanPage = item;
        ScansList.ScrollIntoView(item);
    }

    private static BitmapSource LoadScanBitmap(string path, int decodePixelWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.DecodePixelWidth = decodePixelWidth;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void SplitScanDocument_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedScanPage is null)
        {
            MessageBox.Show(this, "Выберите страницу, после которой должен начаться новый документ.", "Разделение документов", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedIndex = ScanPages.IndexOf(SelectedScanPage);
        if (selectedIndex == ScanPages.Count - 1)
        {
            _startNextScanAsNewDocument = true;
            ScanStatus = "Следующая отсканированная страница начнёт новый PDF-документ.";
            return;
        }

        ScanPages[selectedIndex + 1].IsDocumentStart = true;
        RenumberScanPages();
        ScanStatus = $"После страницы {selectedIndex + 1} установлен разделитель документов.";
    }

    private void JoinScanDocument_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedScanPage is null)
        {
            MessageBox.Show(this, "Выберите первую страницу документа, который нужно объединить с предыдущим.", "Объединение документов", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedIndex = ScanPages.IndexOf(SelectedScanPage);
        if (selectedIndex <= 0 || !SelectedScanPage.IsDocumentStart)
        {
            MessageBox.Show(this, "Выбранная страница не является началом отдельного документа.", "Объединение документов", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedScanPage.IsDocumentStart = false;
        RenumberScanPages();
        ScanStatus = "Документ объединён с предыдущим.";
    }

    private void DeleteSelectedScans_Click(object sender, RoutedEventArgs e)
    {
        var selectedPages = ScansList.SelectedItems.Cast<ScanPageItem>().ToList();
        if (selectedPages.Count == 0 && SelectedScanPage is not null)
        {
            selectedPages.Add(SelectedScanPage);
        }

        var selectedSet = selectedPages.ToHashSet();
        foreach (var documentStart in selectedPages.Where(page => page.IsDocumentStart))
        {
            var nextIndex = ScanPages.IndexOf(documentStart) + 1;
            while (nextIndex < ScanPages.Count && selectedSet.Contains(ScanPages[nextIndex]))
            {
                nextIndex++;
            }

            if (nextIndex < ScanPages.Count && !ScanPages[nextIndex].IsDocumentStart)
            {
                ScanPages[nextIndex].IsDocumentStart = true;
            }
        }

        foreach (var page in selectedPages)
        {
            ScanPages.Remove(page);
            DeleteFileIfExists(page.ImagePath);
        }

        _startNextScanAsNewDocument = false;
        RenumberScanPages();
        SelectedScanPage = ScanPages.FirstOrDefault();
        ScanStatus = selectedPages.Count == 0 ? "Выберите страницы для удаления." : $"Удалено страниц: {selectedPages.Count}.";
    }

    private void ClearScans_Click(object sender, RoutedEventArgs e)
    {
        if (ScanPages.Count == 0)
        {
            return;
        }

        if (MessageBox.Show(this, "Удалить все отсканированные страницы из текущей сессии?", "Очистка сканов", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        CleanupScanPages();
        ScanStatus = "Область сканов очищена.";
    }

    private async void SaveScans_Click(object sender, RoutedEventArgs e)
    {
        if (ScanPages.Count == 0)
        {
            MessageBox.Show(this, "Сначала отсканируйте хотя бы одну страницу.", "Сохранение сканов", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var groups = BuildScanDocumentGroups();
        var defaultBaseName = string.IsNullOrWhiteSpace(ScanBaseFileName) ? "Документ" : ScanBaseFileName.Trim();
        for (var index = 0; index < groups.Count; index++)
        {
            var documentName = groups[index][0].DocumentName;
            var validationError = WindowsFileNameRules.GetValidationError(documentName);
            if (validationError is null)
            {
                continue;
            }

            SelectedScanPage = groups[index][0];
            ScansList.ScrollIntoView(SelectedScanPage);
            ScanStatus = $"Исправьте имя документа {index + 1}: {validationError}";
            MessageBox.Show(
                this,
                $"Документ {index + 1}: {validationError}\n\nНельзя использовать символы \\ / : * ? \" < > |, системные имена CON, PRN, AUX, NUL, COM1–COM9 и LPT1–LPT9.",
                "Имя PDF-файла",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для сохранения отсканированных PDF",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        IsSavingScans = true;
        var savedCount = 0;

        try
        {
            for (var index = 0; index < groups.Count; index++)
            {
                ScanStatus = $"Сохранение документа {index + 1} из {groups.Count}...";
                var documentName = GetScanDocumentName(groups[index], index, defaultBaseName);
                var requestedName = $"{MakeSafeFileName(documentName)}.pdf";
                var outputPath = CreatePermanentPdfPath(requestedName, dialog.FolderName);

                try
                {
                    await SaveScanGroupAsPdfAsync(groups[index], outputPath);
                    savedCount++;
                }
                catch
                {
                    DeleteFileIfExists(outputPath);
                    throw;
                }
            }

            ScanStatus = $"Готово. Сохранено PDF-документов: {savedCount}. Папка: {dialog.FolderName}";
        }
        catch (Exception ex)
        {
            ScanStatus = $"Не удалось сохранить сканы: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Сохранение сканов", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsSavingScans = false;
        }
    }

    private static string GetScanDocumentName(IReadOnlyList<ScanPageItem> group, int index, string defaultBaseName)
    {
        return string.IsNullOrWhiteSpace(group[0].DocumentName)
            ? $"{defaultBaseName} {index + 1}"
            : group[0].DocumentName;
    }

    private List<List<ScanPageItem>> BuildScanDocumentGroups()
    {
        var groups = new List<List<ScanPageItem>>();
        foreach (var page in ScanPages)
        {
            if (groups.Count == 0 || page.IsDocumentStart)
            {
                groups.Add([]);
            }

            groups[^1].Add(page);
        }

        return groups;
    }

    private async Task SaveScanGroupAsPdfAsync(IReadOnlyList<ScanPageItem> pages, string outputPath)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "UISI Print Master", "ScanPdf");
        Directory.CreateDirectory(tempFolder);
        var htmlPath = Path.Combine(tempFolder, $"scan-{Guid.NewGuid():N}.html");

        try
        {
            await File.WriteAllTextAsync(htmlPath, BuildScanPdfHtml(pages), Encoding.UTF8);
            await ConversionPdfBrowser.EnsureCoreWebView2Async();
            await NavigateWebViewToUriAsync(ConversionPdfBrowser, new Uri(htmlPath).AbsoluteUri);
            await WaitForWebImagesAsync(ConversionPdfBrowser);

            var coreWebView = ConversionPdfBrowser.CoreWebView2
                              ?? throw new InvalidOperationException("Модуль создания PDF не инициализирован.");
            var printSettings = coreWebView.Environment.CreatePrintSettings();
            printSettings.Orientation = CoreWebView2PrintOrientation.Portrait;
            printSettings.ShouldPrintHeaderAndFooter = false;
            printSettings.ShouldPrintBackgrounds = true;
            printSettings.PagesPerSide = 1;
            printSettings.ScaleFactor = 1;
            printSettings.PageWidth = 8.27;
            printSettings.PageHeight = 11.69;
            printSettings.MarginTop = 0;
            printSettings.MarginBottom = 0;
            printSettings.MarginLeft = 0;
            printSettings.MarginRight = 0;

            var success = await coreWebView.PrintToPdfAsync(outputPath, printSettings);
            if (!success || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                throw new InvalidOperationException("Не удалось создать PDF из отсканированных страниц.");
            }
        }
        finally
        {
            DeleteFileIfExists(htmlPath);
        }
    }

    private static string BuildScanPdfHtml(IReadOnlyList<ScanPageItem> pages)
    {
        var pageMarkup = string.Join(Environment.NewLine, pages.Select(page =>
        {
            var source = System.Net.WebUtility.HtmlEncode(new Uri(page.ImagePath).AbsoluteUri);
            return $"<section class=\"page\"><img src=\"{source}\" alt=\"\"></section>";
        }));

        return $$"""
                <!doctype html>
                <html>
                <head>
                    <meta charset="utf-8">
                    <style>
                        @page { size: A4 portrait; margin: 0; }
                        html, body { margin: 0; padding: 0; background: white; }
                        .page {
                            width: 210mm;
                            height: 297mm;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            overflow: hidden;
                            break-after: page;
                            page-break-after: always;
                        }
                        .page:last-child { break-after: auto; page-break-after: auto; }
                        img { display: block; width: 100%; height: 100%; object-fit: cover; }
                    </style>
                </head>
                <body>{{pageMarkup}}</body>
                </html>
                """;
    }

    private async Task NavigateWebViewToUriAsync(WebView2 browser, string uri)
    {
        var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                navigation.TrySetResult();
            }
            else
            {
                navigation.TrySetException(new InvalidOperationException($"Не удалось подготовить документ для PDF: {e.WebErrorStatus}."));
            }
        }

        browser.NavigationCompleted += OnNavigationCompleted;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var _ = timeout.Token.Register(() => navigation.TrySetException(new TimeoutException("Подготовка документа к PDF заняла слишком много времени.")));

        try
        {
            browser.Source = new Uri(uri);
            await navigation.Task;
        }
        finally
        {
            browser.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    private static async Task WaitForWebImagesAsync(WebView2 browser)
    {
        var coreWebView = browser.CoreWebView2
                          ?? throw new InvalidOperationException("Модуль создания PDF не инициализирован.");
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            var result = await coreWebView.ExecuteScriptAsync(
                "document.readyState === 'complete' && (!document.fonts || document.fonts.status === 'loaded') && Array.from(document.images).every(image => image.complete && image.naturalWidth > 0)");
            if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Не всё содержимое документа успело подготовиться к сохранению PDF.");
    }

    private void RenumberScanPages()
    {
        if (ScanPages.Count == 0)
        {
            OnPropertyChanged(nameof(ScanPagesSummary));
            return;
        }

        ScanPages[0].IsDocumentStart = true;
        var documentNumber = 0;
        var pageInDocument = 0;

        for (var index = 0; index < ScanPages.Count; index++)
        {
            var page = ScanPages[index];
            if (page.IsDocumentStart)
            {
                documentNumber++;
                pageInDocument = 0;
                var baseName = string.IsNullOrWhiteSpace(ScanBaseFileName) ? "Документ" : ScanBaseFileName.Trim();
                page.SetDefaultDocumentName($"{baseName} {documentNumber}");
            }

            pageInDocument++;
            page.PageNumber = index + 1;
            page.DocumentNumber = documentNumber;
            page.PageInDocument = pageInDocument;
        }

        OnPropertyChanged(nameof(ScanPagesSummary));
    }

    private int GetScanDocumentCount()
    {
        return ScanPages.Count == 0 ? 0 : Math.Max(1, ScanPages.Count(page => page.IsDocumentStart));
    }

    private void CleanupScanPages()
    {
        foreach (var page in ScanPages.ToList())
        {
            DeleteFileIfExists(page.ImagePath);
        }

        ScanPages.Clear();
        SelectedScanPage = null;
        _startNextScanAsNewDocument = false;
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var existingPaths = Files.Select(file => file.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedCount = 0;
        var skippedCount = 0;

        foreach (var path in ExpandDroppedPaths(paths))
        {
            if (!File.Exists(path) || existingPaths.Contains(path) || !IsSupportedFile(path))
            {
                skippedCount++;
                continue;
            }

            try
            {
                Files.Add(new PrintFileItem(path));
                existingPaths.Add(path);
                addedCount++;
            }
            catch
            {
                skippedCount++;
            }
        }

        OverallStatus = addedCount > 0
            ? $"Добавлено документов: {addedCount}."
            : "Подходящие новые документы не найдены.";

        if (skippedCount > 0 && addedCount > 0)
        {
            OverallStatus += $" Пропущено: {skippedCount}.";
        }

        UpdatePreview();
    }

    private void AddConversionFiles(IEnumerable<string> paths)
    {
        var existingPaths = ConversionFiles.Select(file => file.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedCount = 0;
        var skippedCount = 0;

        foreach (var path in ExpandDroppedPaths(paths))
        {
            if (!File.Exists(path) || existingPaths.Contains(path) || !IsConvertibleToPdfFile(path))
            {
                skippedCount++;
                continue;
            }

            try
            {
                var item = new PrintFileItem(path);
                item.Status = "Ожидает конвертации";
                ConversionFiles.Add(item);
                existingPaths.Add(path);
                addedCount++;
            }
            catch
            {
                skippedCount++;
            }
        }

        ConversionStatus = addedCount > 0
            ? $"Добавлено в конвертацию: {addedCount}."
            : "Подходящие документы не найдены.";

        if (skippedCount > 0 && addedCount > 0)
        {
            ConversionStatus += $" Пропущено: {skippedCount}.";
        }
    }

    private static IEnumerable<string> ExpandDroppedPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                yield return path;
                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path).Where(IsSupportedFile))
            {
                yield return file;
            }
        }
    }

    private static bool IsSupportedFile(string path)
    {
        return SupportedExtensions.Contains(Path.GetExtension(path));
    }

    private static bool IsInternalPrintFile(PrintFileItem file)
    {
        return TextExtensions.Contains(file.Extension)
               || string.Equals(file.Extension, ".rtf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDocxFile(PrintFileItem file)
    {
        return string.Equals(file.Extension, ".docx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDocumentFile(PrintFileItem file)
    {
        return DocumentExtensions.Contains(file.Extension);
    }

    private static bool IsConvertibleToPdfFile(string path)
    {
        var extension = Path.GetExtension(path);
        return TextExtensions.Contains(extension) || DocumentExtensions.Contains(extension);
    }

    private PrintDialog CreatePrintDialog(PrintSettings settings)
    {
        var dialog = new PrintDialog();

        try
        {
            var server = new LocalPrintServer();
            dialog.PrintQueue = !string.IsNullOrWhiteSpace(SelectedPrinterName)
                ? server.GetPrintQueue(SelectedPrinterName)
                : LocalPrintServer.GetDefaultPrintQueue();
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(SelectedPrinterName))
            {
                throw new InvalidOperationException($"принтер \"{SelectedPrinterName}\" недоступен или удален.", ex);
            }

            throw new InvalidOperationException("принтер по умолчанию недоступен.", ex);
        }

        ApplyPrintSettingsToPrintTicket(dialog, settings);
        return dialog;
    }

    private static void ApplyPrintSettingsToPrintTicket(PrintDialog dialog, PrintSettings settings)
    {
        dialog.PrintTicket ??= new PrintTicket();
        ApplyPrintSettingsToPrintTicket(dialog.PrintTicket, settings);
    }

    private static void ApplyPrintSettingsToPrintTicket(PrintTicket printTicket, PrintSettings settings)
    {
        printTicket.PageOrientation = settings.PageOrientation == "Landscape"
            ? PageOrientation.Landscape
            : PageOrientation.Portrait;
        printTicket.Duplexing = settings.DuplexMode switch
        {
            "TwoSidedLongEdge" => Duplexing.TwoSidedLongEdge,
            _ => Duplexing.OneSided
        };
        printTicket.OutputColor = settings.ColorMode == "Monochrome"
            ? OutputColor.Monochrome
            : OutputColor.Color;
        printTicket.CopyCount = Math.Clamp(settings.Copies, 1, 99);
    }

    private static void PrintTextFile(PrintDialog dialog, PrintFileItem file, PrintSettings settings)
    {
        var pageSize = GetPrintablePageSize(dialog);
        var document = BuildTextDocument(
            file.FileName,
            ReadPrintableDocumentText(file),
            settings,
            pageSize.Width,
            pageSize.Height);

        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        var range = settings.GetPageRange();
        dialog.PrintDocument(
            new PageRangeDocumentPaginator(paginator, range.FromPage, range.ToPage),
            file.FileName);
    }

    private static Size GetPrintablePageSize(PrintDialog dialog)
    {
        static bool IsUsable(double? value)
        {
            return value is > 0 && double.IsFinite(value.Value);
        }

        var width = dialog.PrintableAreaWidth;
        var height = dialog.PrintableAreaHeight;
        if (IsUsable(width) && IsUsable(height))
        {
            return new Size(width, height);
        }

        var mediaWidth = dialog.PrintTicket?.PageMediaSize?.Width;
        var mediaHeight = dialog.PrintTicket?.PageMediaSize?.Height;
        if (IsUsable(mediaWidth) && IsUsable(mediaHeight))
        {
            return new Size(mediaWidth!.Value, mediaHeight!.Value);
        }

        const double a4Width = 793.7;
        const double a4Height = 1122.5;
        return dialog.PrintTicket?.PageOrientation == PageOrientation.Landscape
            ? new Size(a4Height, a4Width)
            : new Size(a4Width, a4Height);
    }

    private async Task PrintDocxFileAsync(PrintFileItem file, PrintDialog dialog, PrintSettings printSettings)
    {
        ApplyPrintSettingsToPrintTicket(dialog, printSettings);
        var printerName = dialog.PrintQueue?.Name;
        if (string.IsNullOrWhiteSpace(printerName))
        {
            throw new InvalidOperationException("Не удалось определить выбранный принтер.");
        }

        var range = printSettings.GetPageRange();
        var settings = new WordPrintSettings(
            printerName,
            Math.Clamp(printSettings.Copies, 1, 99),
            printSettings.PageOrientation == "Landscape",
            printSettings.DuplexMode == "TwoSidedLongEdge",
            printSettings.ColorMode != "Monochrome",
            range.FromPage,
            range.ToPage);

        OverallStatus = $"Microsoft Word отправляет {file.FileName} одним заданием...";
        await Dispatcher.Yield(DispatcherPriority.Background);
        await WordInteropConverter.PrintAsync(file.FullPath, settings);
    }

    private async Task PrintPdfFileAsync(
        string pdfPath,
        PrintDialog dialog,
        PrintSettings printSettings,
        string? printJobName = null)
    {
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("PDF-файл для печати не найден.", pdfPath);
        }

        ApplyPrintSettingsToPrintTicket(dialog, printSettings);
        var displayName = string.IsNullOrWhiteSpace(printJobName)
            ? Path.GetFileName(pdfPath)
            : printJobName;
        var printerName = dialog.PrintQueue?.FullName;
        if (string.IsNullOrWhiteSpace(printerName))
        {
            throw new InvalidOperationException("Не удалось определить выбранный принтер.");
        }

        var progress = new Progress<PdfPrintProgress>(value =>
        {
            OverallStatus = $"Печать {displayName}: страница {value.CurrentPage} из {value.TotalPages}";
        });

        var range = printSettings.GetPageRange();
        var settings = new PdfPrintSettings(
            printerName,
            displayName,
            Math.Clamp(printSettings.Copies, 1, 99),
            printSettings.PageOrientation == "Landscape",
            printSettings.DuplexMode == "TwoSidedLongEdge",
            printSettings.ColorMode != "Monochrome",
            range.FromPage,
            range.ToPage,
            printSettings.PdfPageSizing == "ActualSize");

        OverallStatus = $"Отправка {displayName} на принтер одним заданием...";
        await Dispatcher.Yield(DispatcherPriority.Background);
        await PdfPrintService.PrintAsync(pdfPath, settings, progress);
    }

    private async void UpdatePreview()
    {
        if (!IsLoaded)
        {
            return;
        }

        var previewVersion = ++_previewVersion;
        var selectedFile = FilesGrid.SelectedItem as PrintFileItem;
        var settings = PrintSettings;

        HidePreviewSurfaces();

        if (selectedFile is null)
        {
            ShowFlowPreview(BuildTextDocument("Пример документа.txt", CreateDefaultPreviewText(), settings, GetPreviewPageWidth(settings), GetPreviewPageHeight(settings)));
            return;
        }

        try
        {
            if (PdfExtensions.Contains(selectedFile.Extension))
            {
                ClearPreviewPdf();
                await ShowPdfPreviewAsync(selectedFile.FullPath, previewVersion);
                return;
            }

            if (IsDocxFile(selectedFile))
            {
                if (TryGetCurrentDocxPreview(selectedFile, out var previewPdfPath))
                {
                    await ShowPdfPreviewAsync(previewPdfPath, previewVersion);
                    return;
                }

                ShowPreviewMessage("Подготавливается предпросмотр Word-документа...");
                await Task.Delay(180);
                if (previewVersion != _previewVersion)
                {
                    return;
                }

                var temporaryPdfPath = CreateTemporaryPdfPath(selectedFile, "DocxPreview");

                try
                {
                    await ConvertDocxFileToPdfAsync(selectedFile.FullPath, temporaryPdfPath);
                    if (previewVersion != _previewVersion)
                    {
                        DeleteTemporaryPdf(temporaryPdfPath);
                        return;
                    }

                    ClearPreviewPdf();
                    _currentPreviewPdfPath = temporaryPdfPath;
                    _currentPreviewSourcePath = selectedFile.FullPath;
                    _currentPreviewSourceWriteTimeUtc = File.GetLastWriteTimeUtc(selectedFile.FullPath);
                    await ShowPdfPreviewAsync(temporaryPdfPath, previewVersion);
                }
                catch
                {
                    DeleteTemporaryPdf(temporaryPdfPath);
                    throw;
                }

                return;
            }

            var document = BuildTextDocument(selectedFile.FileName, ReadPrintableDocumentText(selectedFile), settings, GetPreviewPageWidth(settings), GetPreviewPageHeight(settings));

            ShowFlowPreview(document);
        }
        catch (Exception ex)
        {
            ShowPreviewMessage($"Не удалось построить предпросмотр: {ex.Message}");
        }
    }

    private async Task ShowPdfPreviewAsync(string pdfPath, int previewVersion)
    {
        FlowPreviewHost.Visibility = Visibility.Collapsed;
        PreviewMessageHost.Visibility = Visibility.Collapsed;
        PdfPreviewHost.Visibility = Visibility.Visible;

        try
        {
            await PdfPreviewBrowser.EnsureCoreWebView2Async();
            if (previewVersion != _previewVersion)
            {
                return;
            }

            PdfPreviewBrowser.Source = new Uri(pdfPath);
        }
        catch (Exception ex)
        {
            ShowPreviewMessage($"PDF-предпросмотр недоступен: {ex.Message}");
        }
    }

    private void ShowFlowPreview(FlowDocument document)
    {
        ClearPreviewPdf();
        PdfPreviewHost.Visibility = Visibility.Collapsed;
        PreviewMessageHost.Visibility = Visibility.Collapsed;
        FlowPreviewHost.Visibility = Visibility.Visible;
        PrintPreviewViewer.Visibility = Visibility.Visible;
        PrintPreviewViewer.Document = document;
        _previewScrollViewer = null;
    }

    private void ShowPreviewMessage(string message)
    {
        ClearPreviewPdf();
        PdfPreviewHost.Visibility = Visibility.Collapsed;
        FlowPreviewHost.Visibility = Visibility.Collapsed;
        PreviewMessageHost.Visibility = Visibility.Visible;
        PreviewMessage.Text = message;
    }

    private void HidePreviewSurfaces()
    {
        PdfPreviewHost.Visibility = Visibility.Collapsed;
        PreviewMessageHost.Visibility = Visibility.Collapsed;
        FlowPreviewHost.Visibility = Visibility.Collapsed;
        StopPreviewDrag();
        StopPdfPreviewDrag();
    }

    private async void ConvertSelectedFiles_Click(object sender, RoutedEventArgs e)
    {
        var selectedFiles = ConversionFilesGrid.SelectedItems
            .Cast<PrintFileItem>()
            .ToList();

        if (selectedFiles.Count == 0)
        {
            MessageBox.Show(this, "Выберите один или несколько файлов в списке конвертации.", "Конвертация", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var outputFolder = PromptForConversionFolder();
        if (outputFolder is null)
        {
            return;
        }

        await ConvertFilesToPdfAsync(selectedFiles, outputFolder);
    }

    private async void ConvertAllWordFiles_Click(object sender, RoutedEventArgs e)
    {
        var filesToConvert = ConversionFiles.ToList();
        if (filesToConvert.Count == 0)
        {
            MessageBox.Show(this, "Добавьте документы в список конвертации.", "Конвертация", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var outputFolder = PromptForConversionFolder();
        if (outputFolder is null)
        {
            return;
        }

        await ConvertFilesToPdfAsync(filesToConvert, outputFolder);
    }

    private string? PromptForConversionFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для сохранения PDF",
            Multiselect = false
        };

        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private async Task ConvertFilesToPdfAsync(IReadOnlyList<PrintFileItem> filesToConvert, string outputFolder)
    {
        if (IsConverting || filesToConvert.Count == 0)
        {
            return;
        }

        IsConverting = true;
        ConversionProgressValue = 0;
        var completedCount = 0;
        var convertedCount = 0;
        var errorCount = 0;

        try
        {
            foreach (var file in filesToConvert)
            {
                file.Status = "Конвертация";
                ConversionStatus = $"Конвертация {completedCount + 1} из {filesToConvert.Count}: {file.FileName}";
                await Dispatcher.Yield(DispatcherPriority.Background);

                try
                {
                    var outputPath = CreatePermanentPdfPath(file.FileName, outputFolder);
                    try
                    {
                        await ConvertPrintableFileToPdfAsync(file, outputPath);
                    }
                    catch
                    {
                        DeleteFileIfExists(outputPath);
                        throw;
                    }

                    file.Status = "PDF готов";
                    convertedCount++;
                }
                catch (Exception ex)
                {
                    file.Status = "Ошибка";
                    errorCount++;
                    ConversionStatus = $"Не удалось конвертировать {file.FileName}: {ex.Message}";
                }

                completedCount++;
                ConversionProgressValue = completedCount * 100d / filesToConvert.Count;
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }
        finally
        {
            IsConverting = false;
            ConversionProgressValue = filesToConvert.Count == 0 ? 0 : 100;
            ConversionStatus = errorCount == 0
                ? $"Готово. Создано PDF: {convertedCount}. Папка: {outputFolder}"
                : $"Готово с ошибками. Создано PDF: {convertedCount}, ошибок: {errorCount}. Папка: {outputFolder}";
        }
    }

    private async Task ConvertPrintableFileToPdfAsync(PrintFileItem file, string outputPath)
    {
        if (IsDocxFile(file))
        {
            await ConvertDocxFileToPdfAsync(file.FullPath, outputPath);
            return;
        }

        var html = BuildPdfConversionHtml(file.FileName, ReadPrintableDocumentText(file));

        await _pdfConversionLock.WaitAsync();
        try
        {
            await ConversionPdfBrowser.EnsureCoreWebView2Async();
            await NavigateWebViewToHtmlAsync(ConversionPdfBrowser, html);

            var coreWebView = ConversionPdfBrowser.CoreWebView2
                              ?? throw new InvalidOperationException("PDF-конвертер WebView2 не инициализирован.");
            var printSettings = coreWebView.Environment.CreatePrintSettings();
            ApplyTextConversionPdfSettings(printSettings);

            var success = await coreWebView.PrintToPdfAsync(outputPath, printSettings);
            EnsurePdfWasCreated(success, outputPath);
        }
        finally
        {
            _pdfConversionLock.Release();
        }
    }

    private async Task ConvertDocxFileToPdfAsync(string docxPath, string outputPath)
    {
        await WordInteropConverter.ConvertToPdfAsync(docxPath, outputPath);
        EnsurePdfWasCreated(success: true, outputPath);
    }

    private async Task NavigateWebViewToHtmlAsync(WebView2 browser, string html)
    {
        var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                navigation.TrySetResult();
                return;
            }

            navigation.TrySetException(new InvalidOperationException($"Не удалось подготовить документ для PDF: {e.WebErrorStatus}."));
        }

        browser.NavigationCompleted += OnNavigationCompleted;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var _ = timeout.Token.Register(() => navigation.TrySetException(new TimeoutException("Документ слишком долго готовился к PDF-конвертации.")));

        try
        {
            browser.NavigateToString(html);
            await navigation.Task;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        finally
        {
            browser.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    private static string BuildPdfConversionHtml(string fileName, string text)
    {
        var title = System.Net.WebUtility.HtmlEncode(fileName);
        var body = System.Net.WebUtility.HtmlEncode(text);

        return $$"""
                <!doctype html>
                <html>
                <head>
                    <meta charset="utf-8">
                    <title>{{title}}</title>
                    <style>
                        @page { size: A4 portrait; margin: 15mm; }
                        body {
                            margin: 0;
                            color: #111827;
                            font-family: "Segoe UI", Arial, sans-serif;
                            font-size: 11pt;
                            line-height: 1.25;
                            white-space: pre-wrap;
                            overflow-wrap: anywhere;
                        }
                    </style>
                </head>
                <body>{{body}}</body>
                </html>
                """;
    }

    private static void ApplyTextConversionPdfSettings(CoreWebView2PrintSettings printSettings)
    {
        printSettings.Orientation = CoreWebView2PrintOrientation.Portrait;
        printSettings.ShouldPrintHeaderAndFooter = false;
        printSettings.ShouldPrintBackgrounds = true;
        printSettings.PagesPerSide = 1;
        printSettings.ScaleFactor = 1;
        printSettings.PageWidth = 8.27;
        printSettings.PageHeight = 11.69;
        printSettings.MarginTop = MmToInches(15);
        printSettings.MarginBottom = MmToInches(15);
        printSettings.MarginLeft = MmToInches(15);
        printSettings.MarginRight = MmToInches(15);
    }

    private static void EnsurePdfWasCreated(bool success, string outputPath)
    {
        if (!success || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            throw new InvalidOperationException("Не удалось создать PDF-файл.");
        }
    }

    private string CreateTemporaryPdfPath(PrintFileItem file, string scope)
    {
        var folder = Path.Combine(Path.GetTempPath(), "UISI Print Master", scope);
        Directory.CreateDirectory(folder);

        var fileName = MakeSafeFileName(Path.GetFileNameWithoutExtension(file.FileName));
        var path = Path.Combine(folder, $"{fileName}-{Guid.NewGuid():N}.pdf");
        _temporaryPdfFiles.Add(path);
        return path;
    }

    private static string CreatePermanentPdfPath(string sourceFileName, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);

        var fileName = MakeSafeFileName(Path.GetFileNameWithoutExtension(sourceFileName));
        var candidate = Path.Combine(outputFolder, $"{fileName}.pdf");

        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var index = 2; index < 1000; index++)
        {
            candidate = Path.Combine(outputFolder, $"{fileName} ({index}).pdf");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(outputFolder, $"{fileName}-{Guid.NewGuid():N}.pdf");
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup for partial conversion output.
        }
    }

    private void ClearPreviewPdf()
    {
        var previewPdfPath = _currentPreviewPdfPath;
        _currentPreviewPdfPath = null;
        _currentPreviewSourcePath = null;
        _currentPreviewSourceWriteTimeUtc = default;

        if (previewPdfPath is null)
        {
            return;
        }

        try
        {
            PdfPreviewBrowser.CoreWebView2?.Navigate("about:blank");
            PdfPreviewBrowser.Source = null;
        }
        catch
        {
            // WebView2 may not be initialized yet.
        }

        DeleteTemporaryPdf(previewPdfPath);
    }

    private bool TryGetCurrentDocxPreview(PrintFileItem file, out string pdfPath)
    {
        if (_currentPreviewPdfPath is not null
            && File.Exists(_currentPreviewPdfPath)
            && string.Equals(_currentPreviewSourcePath, file.FullPath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(file.FullPath)
            && File.GetLastWriteTimeUtc(file.FullPath) == _currentPreviewSourceWriteTimeUtc)
        {
            pdfPath = _currentPreviewPdfPath;
            return true;
        }

        pdfPath = string.Empty;
        return false;
    }

    private void DeleteTemporaryPdf(string path)
    {
        _temporaryPdfFiles.Remove(path);

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // WebView2 or a PDF handler can keep the file for a short time; cleanup retries on exit.
            _temporaryPdfFiles.Add(path);
        }
    }

    private void ScheduleTemporaryPdfCleanup(string path)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(5));
            try
            {
                await Dispatcher.InvokeAsync(() => DeleteTemporaryPdf(path));
            }
            catch
            {
                // The application may already be shutting down.
            }
        });
    }

    private void CleanupTemporaryPdfFiles()
    {
        ClearPreviewPdf();

        foreach (var path in _temporaryPdfFiles.ToList())
        {
            DeleteTemporaryPdf(path);
        }
    }

    private static string MakeSafeFileName(string fileName)
    {
        return WindowsFileNameRules.Sanitize(fileName, "document");
    }

    private void ScanPreviewZoomOut_Click(object sender, RoutedEventArgs e)
    {
        SetScanPreviewZoom(_scanPreviewZoom - 10);
    }

    private void ScanPreviewZoomIn_Click(object sender, RoutedEventArgs e)
    {
        SetScanPreviewZoom(_scanPreviewZoom + 10);
    }

    private void ResetScanPreviewZoom_Click(object sender, RoutedEventArgs e)
    {
        SetScanPreviewZoom(100);
        ScrollScanPreviewToOrigin();
    }

    private void ExpandScanPreview_Click(object sender, RoutedEventArgs e)
    {
        OpenExpandedScanPreview();
    }

    private void CloseScanPreview_Click(object sender, RoutedEventArgs e)
    {
        CloseExpandedScanPreview();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsScanPreviewExpanded)
        {
            CloseExpandedScanPreview();
            e.Handled = true;
        }
    }

    private void OpenExpandedScanPreview()
    {
        if (SelectedScanPreview is null)
        {
            return;
        }

        StopScanPreviewDrag();
        IsScanPreviewExpanded = true;
        SetScanPreviewZoom(100);
        Dispatcher.BeginInvoke(ScrollScanPreviewToOrigin, DispatcherPriority.Loaded);
    }

    private void CloseExpandedScanPreview()
    {
        StopScanPreviewDrag();
        IsScanPreviewExpanded = false;
        SetScanPreviewZoom(100);
        Dispatcher.BeginInvoke(ScrollScanPreviewToOrigin, DispatcherPriority.Loaded);
        ScansList.Focus();
    }

    private void SetScanPreviewZoom(double zoom)
    {
        var normalizedZoom = Math.Clamp(Math.Round(zoom / 10) * 10, 50, 400);
        if (Math.Abs(_scanPreviewZoom - normalizedZoom) < 0.1)
        {
            return;
        }

        _scanPreviewZoom = normalizedZoom;
        OnPropertyChanged(nameof(ScanPreviewScale));
        OnPropertyChanged(nameof(ScanPreviewZoomText));
    }

    private void ScanPreview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        SetScanPreviewZoom(_scanPreviewZoom + (e.Delta > 0 ? 10 : -10));
        e.Handled = true;
    }

    private void ScanPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || SelectedScanPreview is null)
        {
            return;
        }

        if (IsFromScrollControl(e.OriginalSource, scrollViewer))
        {
            return;
        }

        if (e.ClickCount == 2 && !IsScanPreviewExpanded)
        {
            OpenExpandedScanPreview();
            e.Handled = true;
            return;
        }

        _isScanPreviewDragging = true;
        _activeScanPreviewScrollViewer = scrollViewer;
        _scanPreviewDragStart = e.GetPosition(scrollViewer);
        _scanPreviewDragStartHorizontalOffset = scrollViewer.HorizontalOffset;
        _scanPreviewDragStartVerticalOffset = scrollViewer.VerticalOffset;
        scrollViewer.Cursor = Cursors.SizeAll;
        scrollViewer.CaptureMouse();
        e.Handled = true;
    }

    private void ScanPreview_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isScanPreviewDragging || _activeScanPreviewScrollViewer is null)
        {
            return;
        }

        var currentPosition = e.GetPosition(_activeScanPreviewScrollViewer);
        var delta = currentPosition - _scanPreviewDragStart;
        _activeScanPreviewScrollViewer.ScrollToHorizontalOffset(_scanPreviewDragStartHorizontalOffset - delta.X);
        _activeScanPreviewScrollViewer.ScrollToVerticalOffset(_scanPreviewDragStartVerticalOffset - delta.Y);
        e.Handled = true;
    }

    private void ScanPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isScanPreviewDragging)
        {
            StopScanPreviewDrag();
            e.Handled = true;
        }
    }

    private void ScanPreview_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopScanPreviewDrag();
        }
    }

    private void StopScanPreviewDrag()
    {
        if (_activeScanPreviewScrollViewer is not null)
        {
            _activeScanPreviewScrollViewer.Cursor = Cursors.Hand;
            if (_activeScanPreviewScrollViewer.IsMouseCaptured)
            {
                _activeScanPreviewScrollViewer.ReleaseMouseCapture();
            }
        }

        _activeScanPreviewScrollViewer = null;
        _isScanPreviewDragging = false;
    }

    private void ScrollScanPreviewToOrigin()
    {
        var scrollViewer = IsScanPreviewExpanded
            ? ExpandedScanPreviewScrollViewer
            : ScanPreviewScrollViewer;
        scrollViewer.ScrollToHorizontalOffset(0);
        scrollViewer.ScrollToVerticalOffset(0);
    }

    private void PreviewDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _previewScrollViewer ??= FindVisualChild<ScrollViewer>(PrintPreviewViewer);
        if (_previewScrollViewer is null)
        {
            return;
        }

        if (IsFromScrollControl(e.OriginalSource, FlowPreviewHost))
        {
            return;
        }

        _isPreviewDragging = true;
        _previewDragStart = e.GetPosition(FlowPreviewHost);
        _previewDragStartHorizontalOffset = _previewScrollViewer.HorizontalOffset;
        _previewDragStartVerticalOffset = _previewScrollViewer.VerticalOffset;
        FlowPreviewHost.Cursor = Cursors.SizeAll;
        FlowPreviewHost.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewDrag_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPreviewDragging || _previewScrollViewer is null)
        {
            return;
        }

        var currentPosition = e.GetPosition(FlowPreviewHost);
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
        FlowPreviewHost.Cursor = Cursors.Hand;
        if (FlowPreviewHost.IsMouseCaptured)
        {
            FlowPreviewHost.ReleaseMouseCapture();
        }
    }

    private void PdfPreviewDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PdfPreviewBrowser.CoreWebView2 is null)
        {
            return;
        }

        _isPdfPreviewDragging = true;
        _pdfPreviewDragStart = e.GetPosition(PdfPreviewHost);
        PdfPreviewHost.Cursor = Cursors.SizeAll;
        PdfPreviewBrowser.Cursor = Cursors.SizeAll;
        PdfPreviewHost.CaptureMouse();
        e.Handled = true;
    }

    private void PdfPreviewDrag_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPdfPreviewDragging || PdfPreviewBrowser.CoreWebView2 is null)
        {
            return;
        }

        var currentPosition = e.GetPosition(PdfPreviewHost);
        var delta = currentPosition - _pdfPreviewDragStart;
        _pdfPreviewDragStart = currentPosition;

        if (Math.Abs(delta.X) < 0.5 && Math.Abs(delta.Y) < 0.5)
        {
            return;
        }

        var scrollX = (-delta.X).ToString("0.###", CultureInfo.InvariantCulture);
        var scrollY = (-delta.Y).ToString("0.###", CultureInfo.InvariantCulture);
        _ = PdfPreviewBrowser.CoreWebView2.ExecuteScriptAsync($"window.scrollBy({scrollX}, {scrollY});");
        e.Handled = true;
    }

    private void PdfPreviewDrag_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopPdfPreviewDrag();
        e.Handled = true;
    }

    private void PdfPreviewDrag_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopPdfPreviewDrag();
        }
    }

    private void StopPdfPreviewDrag()
    {
        if (!_isPdfPreviewDragging)
        {
            return;
        }

        _isPdfPreviewDragging = false;
        PdfPreviewHost.Cursor = Cursors.Hand;
        PdfPreviewBrowser.Cursor = Cursors.Hand;
        if (PdfPreviewHost.IsMouseCaptured)
        {
            PdfPreviewHost.ReleaseMouseCapture();
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

    private static FlowDocument BuildTextDocument(string fileName, string text, PrintSettings settings, double pageWidth, double pageHeight)
    {
        _ = fileName;

        if (settings.PageOrientation == "Landscape" && pageHeight > pageWidth)
        {
            (pageWidth, pageHeight) = (pageHeight, pageWidth);
        }
        else if (settings.PageOrientation != "Landscape" && pageWidth > pageHeight)
        {
            (pageWidth, pageHeight) = (pageHeight, pageWidth);
        }

        const double fontSize = 11;
        var document = new FlowDocument
        {
            PageWidth = pageWidth,
            MinPageWidth = pageWidth,
            MaxPageWidth = pageWidth,
            PageHeight = pageHeight,
            PagePadding = new Thickness(
                MmToDiu(15),
                MmToDiu(15),
                MmToDiu(15),
                MmToDiu(15)),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = fontSize,
            TextAlignment = TextAlignment.Left,
            Background = Brushes.White
        };

        document.Blocks.Add(new Paragraph(new Run(text))
        {
            Margin = new Thickness(0),
            LineHeight = fontSize * 1.25
        });

        return document;
    }

    private static double GetPreviewPageWidth(PrintSettings settings)
    {
        return settings.PageOrientation == "Landscape" ? 980d : 700d;
    }

    private static double GetPreviewPageHeight(PrintSettings settings)
    {
        return settings.PageOrientation == "Landscape" ? 700d : 980d;
    }

    private static string ReadPrintableDocumentText(PrintFileItem file)
    {
        if (string.Equals(file.Extension, ".rtf", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentTextReader.ReadText(file.FullPath, file.Extension);
        }

        if (TextExtensions.Contains(file.Extension))
        {
            return ReadTextFile(file.FullPath);
        }

        throw new NotSupportedException("Этот формат пока не поддерживается встроенной обработкой.");
    }

    private static string ReadTextFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return DecodeWindows1251(bytes);
        }
    }

    private static string DecodeWindows1251(byte[] bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            chars[i] = value < 128 ? (char)value : Windows1251Chars[value - 128];
        }

        return new string(chars);
    }

    private static double MmToDiu(double millimeters)
    {
        if (!double.IsFinite(millimeters))
        {
            millimeters = 15;
        }

        return Math.Clamp(millimeters, 0, 100) / 25.4 * 96;
    }

    private static double MmToInches(double millimeters)
    {
        if (!double.IsFinite(millimeters))
        {
            millimeters = 15;
        }

        return Math.Clamp(millimeters, 0, 100) / 25.4;
    }

    private static string CreateDefaultPreviewText()
    {
        return "UISI Print Master" + Environment.NewLine +
               Environment.NewLine +
               "Это пример документа для предпросмотра печати." + Environment.NewLine +
               Environment.NewLine +
               "Добавьте файл в очередь или выберите документ, чтобы увидеть его содержимое.";
    }

    private void SavePrintSettings()
    {
        try
        {
            PrintSettingsStore.Save(PrintSettings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось сохранить параметры печати: {ex.Message}", "Параметры печати", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Files_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FilesSummary));
        OnPropertyChanged(nameof(CanPrint));
    }

    private void ConversionFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ConversionSummary));
        OnPropertyChanged(nameof(CanConvert));
    }

    private void ScanPages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ScanPagesSummary));
        OnPropertyChanged(nameof(CanSaveScans));
        OnPropertyChanged(nameof(CanEditScans));
    }

    private void PrintSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsLoaded &&
            e.PropertyName == nameof(PrintSettings.PageOrientation))
        {
            UpdatePreview();
        }
    }

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
