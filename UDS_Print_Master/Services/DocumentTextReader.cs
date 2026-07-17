using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using Wp = DocumentFormat.OpenXml.Wordprocessing;

namespace UDS_Print_Master.Services;

public static class DocumentTextReader
{
    public static string ReadText(string path, string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".docx" => ReadDocxText(path),
            ".rtf" => ReadRtfText(path),
            _ => throw new NotSupportedException("Этот формат документа пока не поддерживается встроенным обработчиком.")
        };
    }

    private static string ReadDocxText(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var mainDocument = document.MainDocumentPart?.Document;
        var body = mainDocument?.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendDocxBlockText(body, builder);
        return builder.ToString().TrimEnd();
    }

    private static void AppendDocxBlockText(OpenXmlElement element, StringBuilder builder)
    {
        foreach (var child in element.Elements())
        {
            switch (child)
            {
                case Wp.Paragraph paragraph:
                    AppendParagraphText(paragraph, builder);
                    builder.AppendLine();
                    builder.AppendLine();
                    break;
                case Wp.Table table:
                    AppendTableText(table, builder);
                    builder.AppendLine();
                    break;
                default:
                    AppendDocxBlockText(child, builder);
                    break;
            }
        }
    }

    private static void AppendTableText(Wp.Table table, StringBuilder builder)
    {
        foreach (var row in table.Elements<Wp.TableRow>())
        {
            var cells = row.Elements<Wp.TableCell>()
                .Select(ReadCellText)
                .Where(text => !string.IsNullOrWhiteSpace(text));

            builder.AppendLine(string.Join("    ", cells));
        }
    }

    private static string ReadCellText(Wp.TableCell cell)
    {
        var builder = new StringBuilder();
        foreach (var paragraph in cell.Elements<Wp.Paragraph>())
        {
            AppendParagraphText(paragraph, builder);
            builder.Append(' ');
        }

        return builder.ToString().Trim();
    }

    private static void AppendParagraphText(Wp.Paragraph paragraph, StringBuilder builder)
    {
        foreach (var node in paragraph.Descendants())
        {
            switch (node)
            {
                case Wp.Text text:
                    builder.Append(text.Text);
                    break;
                case Wp.TabChar:
                    builder.Append('\t');
                    break;
                case Wp.Break:
                    builder.AppendLine();
                    break;
            }
        }
    }

    private static string ReadRtfText(string path)
    {
        var document = new FlowDocument();
        var range = new TextRange(document.ContentStart, document.ContentEnd);

        using var stream = File.OpenRead(path);
        range.Load(stream, DataFormats.Rtf);
        return range.Text.TrimEnd();
    }
}
