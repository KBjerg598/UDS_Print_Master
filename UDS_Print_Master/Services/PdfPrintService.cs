using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace UDS_Print_Master.Services;

internal readonly record struct PdfRenderProgress(int CurrentPage, int TotalPages);

internal sealed record PreparedPdfPage(string ImagePath, double Width, double Height);

internal sealed class PreparedPdfDocument : IDisposable
{
    public PreparedPdfDocument(string workingFolder, IReadOnlyList<PreparedPdfPage> pages)
    {
        WorkingFolder = workingFolder;
        Pages = pages;
    }

    public string WorkingFolder { get; }

    public IReadOnlyList<PreparedPdfPage> Pages { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(WorkingFolder))
            {
                Directory.Delete(WorkingFolder, recursive: true);
            }
        }
        catch
        {
            // The print spooler can briefly retain a rendered page; stale folders are harmless.
        }
    }
}

internal static class PdfPrintService
{
    private const double DeviceIndependentPixelsPerInch = 96d;
    private const double RenderDpi = 300d;
    private const uint MaximumPageDimension = 10000;

    public static async Task<PreparedPdfDocument> PrepareAsync(
        string pdfPath,
        IProgress<PdfRenderProgress>? progress = null)
    {
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("PDF-файл для печати не найден.", pdfPath);
        }

        var printFolder = Path.Combine(Path.GetTempPath(), "UISI Print Master", "PdfPrint");
        DeleteStaleWorkingFolders(printFolder);
        var workingFolder = Path.Combine(printFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingFolder);

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(pdfPath);
            var document = await PdfDocument.LoadFromFileAsync(storageFile);
            if (document.PageCount == 0)
            {
                throw new InvalidOperationException("PDF-файл не содержит страниц.");
            }

            var pages = new List<PreparedPdfPage>(checked((int)document.PageCount));
            for (uint pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
            {
                using var page = document.GetPage(pageIndex);
                var imagePath = Path.Combine(workingFolder, $"page-{pageIndex + 1:D5}.png");
                await RenderPageAsync(page, imagePath);
                pages.Add(new PreparedPdfPage(imagePath, page.Size.Width, page.Size.Height));
                progress?.Report(new PdfRenderProgress(checked((int)pageIndex + 1), checked((int)document.PageCount)));
            }

            return new PreparedPdfDocument(workingFolder, pages);
        }
        catch
        {
            DeleteWorkingFolder(workingFolder);
            throw;
        }
    }

    private static async Task RenderPageAsync(PdfPage page, string imagePath)
    {
        var scale = RenderDpi / DeviceIndependentPixelsPerInch;
        var scaledWidth = page.Size.Width * scale;
        var scaledHeight = page.Size.Height * scale;
        var dimensionScale = Math.Min(1, MaximumPageDimension / Math.Max(scaledWidth, scaledHeight));
        var width = Math.Max(1u, (uint)Math.Ceiling(scaledWidth * dimensionScale));
        var height = Math.Max(1u, (uint)Math.Ceiling(scaledHeight * dimensionScale));
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = width,
            DestinationHeight = height,
            BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId
        };

        using var randomAccessStream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(randomAccessStream, options);
        randomAccessStream.Seek(0);

        await using var source = randomAccessStream.AsStreamForRead();
        await using var destination = new FileStream(
            imagePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        await source.CopyToAsync(destination);
    }

    private static void DeleteWorkingFolder(string workingFolder)
    {
        try
        {
            if (Directory.Exists(workingFolder))
            {
                Directory.Delete(workingFolder, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup after a failed PDF preparation.
        }
    }

    private static void DeleteStaleWorkingFolders(string printFolder)
    {
        if (!Directory.Exists(printFolder))
        {
            return;
        }

        var threshold = DateTime.UtcNow.AddDays(-1);
        foreach (var folder in Directory.EnumerateDirectories(printFolder))
        {
            try
            {
                if (Directory.GetCreationTimeUtc(folder) < threshold)
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
                // Another application instance may still be using this folder.
            }
        }
    }
}

internal sealed class PreparedPdfPaginator : DocumentPaginator
{
    private readonly IReadOnlyList<PreparedPdfPage> _pages;
    private Size _pageSize;

    public PreparedPdfPaginator(IReadOnlyList<PreparedPdfPage> pages, Size pageSize)
    {
        _pages = pages;
        _pageSize = pageSize;
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => _pages.Count;

    public override Size PageSize
    {
        get => _pageSize;
        set => _pageSize = value;
    }

    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= _pages.Count)
        {
            return DocumentPage.Missing;
        }

        var preparedPage = _pages[pageNumber];
        var bitmap = System.Windows.Media.Imaging.BitmapFrame.Create(
            new Uri(preparedPage.ImagePath, UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            Width = _pageSize.Width,
            Height = _pageSize.Height,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var page = new FixedPage
        {
            Width = _pageSize.Width,
            Height = _pageSize.Height,
            Background = Brushes.White
        };
        page.Children.Add(image);
        page.Measure(_pageSize);
        page.Arrange(new Rect(_pageSize));
        page.UpdateLayout();

        var pageBounds = new Rect(_pageSize);
        return new DocumentPage(page, _pageSize, pageBounds, pageBounds);
    }
}
