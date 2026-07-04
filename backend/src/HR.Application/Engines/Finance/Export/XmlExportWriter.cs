using System.Text;
using System.Xml;

namespace HR.Application.Engines.Finance.Export;

/// <summary>UTF-8 XML writer over the format-agnostic dataset — one &lt;Row&gt; per record with a child
/// element per column. Column keys are already safe identifiers; any stray character is sanitized.</summary>
public sealed class XmlExportWriter : IExportWriter
{
    public ExportFormat Format => ExportFormat.Xml;
    public string ContentType => "application/xml";
    public string Extension => "xml";

    public byte[] Write(TabularDataset data, ExportWriteOptions? options = null)
    {
        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using (var w = XmlWriter.Create(ms, settings))
        {
            w.WriteStartDocument();
            w.WriteStartElement("Export");
            w.WriteAttributeString("title", data.Title);
            foreach (var row in data.Rows)
            {
                w.WriteStartElement("Row");
                foreach (var col in data.Columns)
                {
                    w.WriteStartElement(SafeName(col.Key));
                    w.WriteString(ExportValue.Format(row.TryGetValue(col.Key, out var v) ? v : null));
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        return ms.ToArray();
    }

    private static string SafeName(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Field";
        var sb = new StringBuilder();
        foreach (var ch in key)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        var name = sb.ToString();
        return char.IsLetter(name[0]) || name[0] == '_' ? name : "_" + name;
    }
}
