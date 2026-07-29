using System.Windows;
using System.Windows.Documents;

namespace UDS_Print_Master.Utilities;

public sealed class PageRangeDocumentPaginator : DocumentPaginator
{
    private readonly DocumentPaginator _source;
    private readonly int _firstPageIndex;
    private readonly int _pageCount;

    public PageRangeDocumentPaginator(
        DocumentPaginator source,
        int? fromPage,
        int? toPage)
    {
        _source = source;
        _source.ComputePageCount();

        var totalPages = _source.PageCount;
        if (totalPages == 0)
        {
            _firstPageIndex = 0;
            _pageCount = 0;
            return;
        }

        var firstPage = fromPage ?? 1;
        if (firstPage > totalPages)
        {
            throw new InvalidOperationException(
                $"Документ содержит только {totalPages} стр.");
        }

        var lastPage = Math.Min(toPage ?? totalPages, totalPages);
        _firstPageIndex = firstPage - 1;
        _pageCount = Math.Max(0, lastPage - firstPage + 1);
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => _pageCount;

    public override Size PageSize
    {
        get => _source.PageSize;
        set => _source.PageSize = value;
    }

    public override IDocumentPaginatorSource? Source => _source.Source;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= _pageCount)
        {
            return DocumentPage.Missing;
        }

        return _source.GetPage(_firstPageIndex + pageNumber);
    }
}
