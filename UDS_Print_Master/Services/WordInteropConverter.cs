using System.IO;
using System.Printing;
using System.Runtime.InteropServices;

namespace UDS_Print_Master.Services;

internal sealed record WordPrintSettings(
    string PrinterName,
    int Copies,
    bool Landscape,
    bool Duplex,
    bool Color = true,
    int? FromPage = null,
    int? ToPage = null,
    string? PrintToFilePath = null);

internal static class WordInteropConverter
{
    private static readonly SemaphoreSlim AutomationLock = new(1, 1);
    private const int WdAlertsNone = 0;
    private const int WdDoNotSaveChanges = 0;
    private const int WdExportFormatPdf = 17;
    private const int WdExportOptimizeForPrint = 0;
    private const int WdExportAllDocument = 0;
    private const int WdExportDocumentContent = 0;
    private const int WdExportCreateHeadingBookmarks = 1;
    private const int WdPrintAllDocument = 0;
    private const int WdPrintFromTo = 3;
    private const int MsoAutomationSecurityForceDisable = 3;

    public static async Task ConvertToPdfAsync(
        string sourcePath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        EnsureSourceExists(sourcePath);

        await AutomationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RunOnStaThreadAsync(
                    () => ConvertToPdf(sourcePath, outputPath),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            AutomationLock.Release();
        }
    }

    public static async Task PrintAsync(
        string sourcePath,
        WordPrintSettings settings,
        CancellationToken cancellationToken = default)
    {
        EnsureSourceExists(sourcePath);

        await AutomationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RunOnStaThreadAsync(
                    () => PrintDocument(sourcePath, settings),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            AutomationLock.Release();
        }
    }

    private static Task RunOnStaThreadAsync(Action operation, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation();
                completion.TrySetResult();
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "UISI Word automation"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void ConvertToPdf(string sourcePath, string outputPath)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)
                                  ?? throw new InvalidOperationException("Не удалось определить папку временного PDF."));
        DeleteOutputIfExists(fullOutputPath);

        try
        {
            RunWithDocument(sourcePath, (_, document) =>
            {
                document.ExportAsFixedFormat(
                    OutputFileName: fullOutputPath,
                    ExportFormat: WdExportFormatPdf,
                    OpenAfterExport: false,
                    OptimizeFor: WdExportOptimizeForPrint,
                    Range: WdExportAllDocument,
                    Item: WdExportDocumentContent,
                    IncludeDocProps: true,
                    KeepIRM: true,
                    CreateBookmarks: WdExportCreateHeadingBookmarks,
                    DocStructureTags: true,
                    BitmapMissingFonts: true,
                    UseISO19005_1: false);
            });

            if (!File.Exists(fullOutputPath) || new FileInfo(fullOutputPath).Length == 0)
            {
                throw new InvalidOperationException("Microsoft Word не создал PDF-файл.");
            }
        }
        catch
        {
            DeleteOutputIfExists(fullOutputPath);
            throw;
        }
    }

    private static void PrintDocument(string sourcePath, WordPrintSettings settings)
    {
        var copies = Math.Clamp(settings.Copies, 1, 99);
        var printToFilePath = string.IsNullOrWhiteSpace(settings.PrintToFilePath)
            ? null
            : Path.GetFullPath(settings.PrintToFilePath);

        if (printToFilePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(printToFilePath)
                                      ?? throw new InvalidOperationException("Не удалось определить папку файла печати."));
            DeleteOutputIfExists(printToFilePath);
        }

        using var printerPreferences = PrinterPreferenceScope.Apply(settings);
        RunWithDocument(sourcePath, (word, document) =>
        {
            SelectWordPrinter(word, settings.PrinterName);
            document.Activate();
            var customRange = settings.FromPage.HasValue;
            var printRange = customRange
                ? WdPrintFromTo
                : WdPrintAllDocument;
            var fromPage = settings.FromPage ?? 1;
            var toPage = settings.ToPage ?? fromPage;

            if (printToFilePath is null)
            {
                document.PrintOut(
                    Background: false,
                    Range: printRange,
                    From: fromPage.ToString(),
                    To: toPage.ToString(),
                    Copies: copies,
                    PrintToFile: false,
                    Collate: true);
            }
            else
            {
                document.PrintOut(
                    Background: false,
                    Range: printRange,
                    From: fromPage.ToString(),
                    To: toPage.ToString(),
                    OutputFileName: printToFilePath,
                    Copies: copies,
                    PrintToFile: true,
                    Collate: true);
            }
        });

        if (printToFilePath is not null &&
            (!File.Exists(printToFilePath) || new FileInfo(printToFilePath).Length == 0))
        {
            throw new InvalidOperationException("Microsoft Word не создал файл печати.");
        }
    }

    private static void RunWithDocument(string sourcePath, Action<dynamic, dynamic> operation)
    {
        var wordType = Type.GetTypeFromProgID("Word.Application")
                       ?? throw new InvalidOperationException(
                           "Microsoft Word не установлен. Для точной печати DOCX установите Word и повторите операцию.");
        object? wordObject = null;
        object? documentsObject = null;
        object? documentObject = null;

        try
        {
            wordObject = Activator.CreateInstance(wordType)
                         ?? throw new InvalidOperationException("Не удалось запустить Microsoft Word.");
            dynamic word = wordObject;
            word.Visible = false;
            word.ScreenUpdating = false;
            word.DisplayAlerts = WdAlertsNone;
            word.AutomationSecurity = MsoAutomationSecurityForceDisable;

            documentsObject = word.Documents;
            dynamic documents = documentsObject;
            documentObject = documents.Open(
                FileName: Path.GetFullPath(sourcePath),
                ConfirmConversions: false,
                ReadOnly: true,
                AddToRecentFiles: false,
                Revert: false,
                Visible: false,
                OpenAndRepair: true,
                NoEncodingDialog: true);

            operation(word, (dynamic)documentObject);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(
                $"Microsoft Word не смог обработать документ: {ex.Message}",
                ex);
        }
        finally
        {
            if (documentObject is not null)
            {
                try
                {
                    ((dynamic)documentObject).Close(WdDoNotSaveChanges);
                }
                catch
                {
                }
            }

            if (wordObject is not null)
            {
                try
                {
                    ((dynamic)wordObject).Quit(WdDoNotSaveChanges);
                }
                catch
                {
                }
            }

            ReleaseComObject(documentObject);
            ReleaseComObject(documentsObject);
            ReleaseComObject(wordObject);
        }
    }

    private static void SelectWordPrinter(dynamic word, string printerName)
    {
        var activePrinter = Convert.ToString(word.ActivePrinter) ?? string.Empty;
        if (activePrinter.StartsWith(printerName, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        object? wordBasicObject = null;
        try
        {
            wordBasicObject = word.WordBasic;
            ((dynamic)wordBasicObject).FilePrintSetup(
                Printer: printerName,
                DoNotSetAsSysDefault: 1);
        }
        finally
        {
            ReleaseComObject(wordBasicObject);
        }
    }

    private static void EnsureSourceExists(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Word-документ не найден.", sourcePath);
        }
    }

    private static void DeleteOutputIfExists(string outputPath)
    {
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private sealed class PrinterPreferenceScope : IDisposable
    {
        private readonly LocalPrintServer _server;
        private readonly PrintQueue _queue;
        private readonly PrintTicket _originalTicket;
        private bool _restoreRequired;

        private PrinterPreferenceScope(
            LocalPrintServer server,
            PrintQueue queue,
            PrintTicket originalTicket)
        {
            _server = server;
            _queue = queue;
            _originalTicket = originalTicket;
        }

        public static PrinterPreferenceScope Apply(WordPrintSettings settings)
        {
            var server = new LocalPrintServer();
            PrintQueue? queue = null;
            PrintTicket originalTicket;

            try
            {
                queue = server.GetPrintQueue(settings.PrinterName);
                queue.Refresh();
                originalTicket = CloneTicket(queue.UserPrintTicket ?? queue.DefaultPrintTicket);
            }
            catch
            {
                queue?.Dispose();
                server.Dispose();
                throw;
            }

            var scope = new PrinterPreferenceScope(server, queue, originalTicket);
            try
            {
                var deltaTicket = new PrintTicket
                {
                    CopyCount = 1,
                    PageOrientation = settings.Landscape
                        ? PageOrientation.Landscape
                        : PageOrientation.Portrait,
                    Duplexing = settings.Duplex
                        ? Duplexing.TwoSidedLongEdge
                        : Duplexing.OneSided,
                    OutputColor = settings.Color
                        ? OutputColor.Color
                        : OutputColor.Monochrome
                };
                var validatedTicket = queue
                    .MergeAndValidatePrintTicket(originalTicket, deltaTicket)
                    .ValidatedPrintTicket;

                if (settings.Duplex && validatedTicket.Duplexing != Duplexing.TwoSidedLongEdge)
                {
                    throw new InvalidOperationException(
                        "Выбранный принтер не поддерживает автоматическую двустороннюю печать с переворотом по длинному краю.");
                }

                scope._restoreRequired = true;
                queue.UserPrintTicket = validatedTicket;
                queue.Commit();
                return scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_restoreRequired)
                {
                    _queue.UserPrintTicket = CloneTicket(_originalTicket);
                    _queue.Commit();
                    _restoreRequired = false;
                }
            }
            finally
            {
                _queue.Dispose();
                _server.Dispose();
            }
        }

        private static PrintTicket CloneTicket(PrintTicket ticket)
        {
            using var stream = new MemoryStream();
            ticket.SaveTo(stream);
            stream.Position = 0;
            return new PrintTicket(stream);
        }
    }
}
