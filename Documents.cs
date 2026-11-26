using System.IO.Compression;
using System.Xml.Linq;

namespace Warehouse;

// Стандартные XML-форматы Office: библиотеки и установленный Office не нужны.
public static class Documents
{
    public static void Word(string path, string title, List<string[]> rows)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XElement Paragraph(string text) => new(w + "p", new XElement(w + "r", new XElement(w + "t", text)));
        var body = new XElement(w + "body", title.Split('\n').Select(Paragraph));
        var table = new XElement(w + "tbl",
            new XElement(w + "tblPr", new XElement(w + "tblBorders", new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(side => new XElement(w + side, new XAttribute(w + "val", "single"), new XAttribute(w + "sz", "4"))))));
        foreach (var row in rows) table.Add(new XElement(w + "tr", row.Select(cell => new XElement(w + "tc", Paragraph(cell)))));
        body.Add(table);
        body.Add(new XElement(w + "sectPr", new XElement(w + "pgSz", new XAttribute(w + "w", "16838"), new XAttribute(w + "h", "11906"), new XAttribute(w + "orient", "landscape"))));
        using var file = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        void Add(string name, XDocument xml) { using var stream = zip.CreateEntry(name).Open(); xml.Save(stream); }
        XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
        Add("[Content_Types].xml", new XDocument(new XElement(types + "Types",
            new XElement(types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(types + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")))));
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        Add("_rels/.rels", new XDocument(new XElement(rel + "Relationships", new XElement(rel + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "word/document.xml")))));
        Add("word/document.xml", new XDocument(new XElement(w + "document", body)));
    }

    public static void Excel(string path, string title, List<string[]> rows)
    {
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        XElement Row(IEnumerable<string> cells) => new(ss + "Row", cells.Select(value => new XElement(ss + "Cell", new XElement(ss + "Data", new XAttribute(ss + "Type", "String"), value))));
        var table = new XElement(ss + "Table", Row(new[] { title }));
        foreach (var row in rows) table.Add(Row(row));
        new XDocument(new XProcessingInstruction("mso-application", "progid=\"Excel.Sheet\""), new XElement(ss + "Workbook", new XAttribute(XNamespace.Xmlns + "ss", ss), new XElement(ss + "Worksheet", new XAttribute(ss + "Name", "Отчёт"), table))).Save(path);
    }
}
