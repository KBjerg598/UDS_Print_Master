using System.IO;
using System.Runtime.InteropServices;

namespace UDS_Print_Master.Services;

internal static class WordInteropConverter
{
    private static readonly SemaphoreSlim ConversionLock = new(1, 1);
    private const int WdAlertsNone = 0;
    private const int WdDoNotSaveChanges = 0;
    private const int WdExportFormatPdf = 17;
    private const int WdExportOptimizeForPrint = 0;
    private const int WdExportAllDocument = 0;
    private const int WdExportDocumentContent = 0;
    private const int WdExportCreateHeadingBookmarks = 1;
    private const int MsoAutomationSecurityForceDisable = 3;

    public static async Task ConvertToPdfAsync(
        string sourcePath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Word-документ не найден.", sourcePath);
        }

        await ConversionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RunOnStaThreadAsync(sourcePath, outputPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ConversionLock.Release();
        }
    }

    private static Task RunOnStaThreadAsync(
        string sourcePath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConvertToPdf(sourcePath, outputPath);
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
            Name = "UISI Word PDF converter"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void ConvertToPdf(string sourcePath, string outputPath)
    {
        var wordType = Type.GetTypeFromProgID("Word.Application")
                       ?? throw new InvalidOperationException(
                           "Microsoft Word не установлен. Для точной печати DOCX установите Word и повторите операцию.");
        object? wordObject = null;
        object? documentsObject = null;
        object? documentObject = null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
                                      ?? throw new InvalidOperationException("Не удалось определить папку временного PDF."));
            DeleteOutputIfExists(outputPath);

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
            dynamic document = documentObject;
            document.ExportAsFixedFormat(
                OutputFileName: Path.GetFullPath(outputPath),
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

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                throw new InvalidOperationException("Microsoft Word не создал PDF-файл.");
            }
        }
        catch (COMException ex)
        {
            DeleteOutputIfExists(outputPath);
            throw new InvalidOperationException(
                $"Microsoft Word не смог преобразовать документ: {ex.Message}",
                ex);
        }
        catch
        {
            DeleteOutputIfExists(outputPath);
            throw;
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
}
