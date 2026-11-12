using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
namespace RCHub.App;
internal static class SheetPreview
{
    public static List<string[]> Read(string path)
    {
        using var zip=ZipFile.OpenRead(path);
        XDocument? Xml(string entry)
        {
            var part=zip.GetEntry(entry);
            if(part==null||part.Length>2_000_000)return null;
            using var stream=part.Open();
            using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2_000_000});
            return XDocument.Load(reader);
        }
        XNamespace n="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var strings=Xml("xl/sharedStrings.xml")?.Descendants(n+"si").Select(x=>string.Concat(x.Descendants(n+"t").Select(t=>t.Value))).ToArray()??[];
        var sheet=Xml("xl/worksheets/sheet1.xml");
        var rows=new List<string[]>();
        foreach(var row in sheet?.Descendants(n+"row").Take(7)??Enumerable.Empty<XElement>())
        {
            var values=new string[3];
            foreach(var cell in row.Elements(n+"c")){
                var reference=(string?)cell.Attribute("r")??"";
                if(reference.Length<2 || reference[0] is <'A' or >'C' || !char.IsDigit(reference[1]))continue;
                var value=cell.Element(n+"v")?.Value??string.Concat(cell.Descendants(n+"t").Select(t=>t.Value));
                if((string?)cell.Attribute("t")=="s" && int.TryParse(value,out var i) && i>=0 && i<strings.Length)value=strings[i];
                values[reference[0]-'A']=value.Length>40?value[..40]+"…":value;
            }
            if(values.Any(v=>!string.IsNullOrEmpty(v)))rows.Add(values);
        }
        return rows;
    }
}
