using System.Drawing.Printing;
using System.IO;
using System.Runtime.InteropServices;

namespace UDS_Print_Master.Services;

internal readonly record struct PdfPrintProgress(int CurrentPage, int TotalPages);

internal sealed record PdfPrintSettings(
    string PrinterName,
    string JobName,
    int Copies,
    bool Landscape,
    bool Duplex);

internal static class PdfPrintService
{
    public static Task PrintAsync(
        string pdfPath,
        PdfPrintSettings settings,
        IProgress<PdfPrintProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Print(pdfPath, settings, progress, cancellationToken),
            cancellationToken);
    }

    internal static void Print(
        string pdfPath,
        PdfPrintSettings settings,
        IProgress<PdfPrintProgress>? progress = null,
        CancellationToken cancellationToken = default,
        PrintController? printController = null)
    {
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("PDF-файл для печати не найден.", pdfPath);
        }

        using var document = new PdfiumPrintDocument(pdfPath, progress, cancellationToken)
        {
            DocumentName = settings.JobName,
            OriginAtMargins = false,
            PrintController = printController ?? new StandardPrintController()
        };

        document.PrinterSettings.PrinterName = settings.PrinterName;
        if (!document.PrinterSettings.IsValid)
        {
            throw new InvalidOperationException($"Принтер \"{settings.PrinterName}\" недоступен.");
        }

        document.PrinterSettings.Copies = checked((short)Math.Clamp(settings.Copies, 1, 99));
        document.PrinterSettings.Collate = true;
        if (settings.Duplex && !document.PrinterSettings.CanDuplex)
        {
            throw new InvalidOperationException("Выбранный принтер не поддерживает двустороннюю печать.");
        }

        document.PrinterSettings.Duplex = settings.Duplex
            ? Duplex.Vertical
            : Duplex.Simplex;
        document.DefaultPageSettings.Landscape = settings.Landscape;
        document.Print();
    }
}

internal sealed class PdfiumPrintDocument : PrintDocument
{
    private readonly IProgress<PdfPrintProgress>? _progress;
    private readonly CancellationToken _cancellationToken;
    private IntPtr _document;
    private int _pageIndex;

    public PdfiumPrintDocument(
        string pdfPath,
        IProgress<PdfPrintProgress>? progress,
        CancellationToken cancellationToken)
    {
        PdfiumNative.EnsureInitialized();
        _document = PdfiumNative.FPDF_LoadDocument(pdfPath, null);
        if (_document == IntPtr.Zero)
        {
            throw new InvalidOperationException(PdfiumNative.GetLoadErrorMessage());
        }

        PageCount = PdfiumNative.FPDF_GetPageCount(_document);
        if (PageCount <= 0)
        {
            PdfiumNative.FPDF_CloseDocument(_document);
            _document = IntPtr.Zero;
            throw new InvalidOperationException("PDF-файл не содержит страниц.");
        }

        _progress = progress;
        _cancellationToken = cancellationToken;
    }

    public int PageCount { get; }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        _pageIndex = 0;
        _cancellationToken.ThrowIfCancellationRequested();
        base.OnBeginPrint(e);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var page = PdfiumNative.FPDF_LoadPage(_document, _pageIndex);
        if (page == IntPtr.Zero)
        {
            throw new InvalidOperationException($"PDFium не смог открыть страницу {_pageIndex + 1}.");
        }

        try
        {
            RenderPage(e, page);
        }
        finally
        {
            PdfiumNative.FPDF_ClosePage(page);
        }

        _pageIndex++;
        _progress?.Report(new PdfPrintProgress(_pageIndex, PageCount));
        e.HasMorePages = _pageIndex < PageCount;
        base.OnPrintPage(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (_document != IntPtr.Zero)
        {
            PdfiumNative.FPDF_CloseDocument(_document);
            _document = IntPtr.Zero;
        }

        base.Dispose(disposing);
    }

    private static void RenderPage(PrintPageEventArgs e, IntPtr page)
    {
        var graphics = e.Graphics
                       ?? throw new InvalidOperationException("Драйвер принтера не предоставил поверхность печати.");
        var hdc = graphics.GetHdc();
        try
        {
            var targetWidth = PdfiumNative.GetDeviceCaps(hdc, PdfiumNative.HorzRes);
            var targetHeight = PdfiumNative.GetDeviceCaps(hdc, PdfiumNative.VertRes);
            if (targetWidth <= 0 || targetHeight <= 0)
            {
                throw new InvalidOperationException("Драйвер принтера вернул некорректный размер страницы.");
            }

            var sourceWidth = PdfiumNative.FPDF_GetPageWidthF(page);
            var sourceHeight = PdfiumNative.FPDF_GetPageHeightF(page);
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                throw new InvalidOperationException("PDF-страница имеет некорректный размер.");
            }

            var targetIsLandscape = targetWidth > targetHeight;
            var sourceIsLandscape = sourceWidth > sourceHeight;
            var rotation = targetIsLandscape == sourceIsLandscape ? 0 : 1;
            var rotatedWidth = rotation == 0 ? sourceWidth : sourceHeight;
            var rotatedHeight = rotation == 0 ? sourceHeight : sourceWidth;
            var scale = Math.Min(targetWidth / rotatedWidth, targetHeight / rotatedHeight);
            var renderWidth = Math.Max(1, (int)Math.Round(rotatedWidth * scale));
            var renderHeight = Math.Max(1, (int)Math.Round(rotatedHeight * scale));
            var renderX = (targetWidth - renderWidth) / 2;
            var renderY = (targetHeight - renderHeight) / 2;

            PdfiumNative.FPDF_RenderPage(
                hdc,
                page,
                renderX,
                renderY,
                renderWidth,
                renderHeight,
                rotation,
                PdfiumNative.PrintingFlags);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }
}

internal static class PdfiumNative
{
    internal const int HorzRes = 8;
    internal const int VertRes = 10;
    internal const int PrintingFlags = RenderAnnotations | RenderForPrinting;
    private const int PdfPrintModeEmf = 0;
    private const int RenderAnnotations = 0x01;
    private const int RenderForPrinting = 0x800;
    private static readonly object InitializationLock = new();
    private static bool _initialized;

    internal static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (InitializationLock)
        {
            if (_initialized)
            {
                return;
            }

            FPDF_InitLibrary();
            FPDF_SetPrintMode(PdfPrintModeEmf);
            _initialized = true;
        }
    }

    internal static string GetLoadErrorMessage()
    {
        return FPDF_GetLastError() switch
        {
            1 => "Неизвестная ошибка PDFium при открытии документа.",
            2 => "PDF-файл поврежден или имеет неподдерживаемый формат.",
            3 => "PDF-файл защищен паролем.",
            4 => "PDF-файл использует неподдерживаемый механизм защиты.",
            5 => "Не удалось открыть страницу PDF-документа.",
            6 => "Ошибка чтения или создания PDF-файла.",
            _ => "PDFium не смог открыть документ."
        };
    }

    [DllImport("gdi32.dll")]
    internal static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FPDF_InitLibrary();

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FPDF_SetPrintMode(int mode);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr FPDF_LoadDocument(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filePath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint FPDF_GetLastError();

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int FPDF_GetPageCount(IntPtr document);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    internal static extern float FPDF_GetPageWidthF(IntPtr page);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    internal static extern float FPDF_GetPageHeightF(IntPtr page);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FPDF_RenderPage(
        IntPtr hdc,
        IntPtr page,
        int startX,
        int startY,
        int sizeX,
        int sizeY,
        int rotate,
        int flags);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FPDF_ClosePage(IntPtr page);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FPDF_CloseDocument(IntPtr document);
}
