using Codeuctivity.OpenXmlPowerTools.OpenXMLWordprocessingMLToHtmlConverter;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace UDS_Print_Master.Services;

internal sealed record DocxHtmlDocument(
    string Html,
    double PageWidthInches,
    double PageHeightInches);

internal static class DocxLayoutConverter
{
    private const double TwipsPerInch = 1440d;
    private const uint DefaultPageWidthTwips = 11906;
    private const uint DefaultPageHeightTwips = 16838;
    private const int DefaultMarginTwips = 1440;

    public static async Task<DocxHtmlDocument> ConvertToHtmlAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("DOCX-файл не найден.", path);
        }

        var source = await File.ReadAllBytesAsync(path);
        return await Task.Run(() => ConvertToHtml(source, Path.GetFileName(path)));
    }

    private static DocxHtmlDocument ConvertToHtml(byte[] source, string pageTitle)
    {
        using var stream = new MemoryStream(source.Length + 4096);
        stream.Write(source);
        stream.Position = 0;
        using var document = WordprocessingDocument.Open(stream, true);
        var layout = ReadPageLayout(document);
        var settings = new WmlToHtmlConverterSettings(
            pageTitle,
            new ImageHandler(),
            new TextDummyHandler(),
            new SymbolHandler(),
            new BreakHandler(),
            new FontHandler(),
            fabricateCssClasses: true,
            generalCss: "span { white-space: pre-wrap; }",
            additionalCss: BuildPrintCss(layout),
            cssClassPrefix: "pt-");
        var htmlDocument = WmlToHtmlConverter.ConvertToHtml(document, settings);
        var html = htmlDocument.ToString(SaveOptions.DisableFormatting);

        return new DocxHtmlDocument(html, layout.WidthInches, layout.HeightInches);
    }

    private static PageLayout ReadPageLayout(WordprocessingDocument document)
    {
        var body = document.MainDocumentPart?.Document?.Body;
        var section = body?
            .Descendants<SectionProperties>()
            .LastOrDefault();
        var pageSize = section?.GetFirstChild<PageSize>();
        var pageMargin = section?.GetFirstChild<PageMargin>();

        var width = ToInches(pageSize?.Width?.Value ?? DefaultPageWidthTwips);
        var height = ToInches(pageSize?.Height?.Value ?? DefaultPageHeightTwips);

        return new PageLayout(
            width,
            height,
            ToInches(Math.Max(0, pageMargin?.Top?.Value ?? DefaultMarginTwips)),
            ToInches(pageMargin?.Right?.Value ?? DefaultMarginTwips),
            ToInches(Math.Max(0, pageMargin?.Bottom?.Value ?? DefaultMarginTwips)),
            ToInches(pageMargin?.Left?.Value ?? DefaultMarginTwips));
    }

    private static string BuildPrintCss(PageLayout layout)
    {
        static string Css(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        return $$"""
                 @page {
                     size: {{Css(layout.WidthInches)}}in {{Css(layout.HeightInches)}}in;
                     margin: {{Css(layout.MarginTopInches)}}in {{Css(layout.MarginRightInches)}}in {{Css(layout.MarginBottomInches)}}in {{Css(layout.MarginLeftInches)}}in;
                 }
                 html, body {
                     margin: 0 !important;
                     padding: 0 !important;
                 }
                 body {
                     max-width: none !important;
                 }
                 """;
    }

    private static double ToInches(long twips)
    {
        return twips / TwipsPerInch;
    }

    private sealed record PageLayout(
        double WidthInches,
        double HeightInches,
        double MarginTopInches,
        double MarginRightInches,
        double MarginBottomInches,
        double MarginLeftInches);
}
