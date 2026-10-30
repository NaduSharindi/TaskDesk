using System.Xml;
using TaskDesk.Common;

namespace TaskDesk.Tests.Domain;

public sealed class CsvTests
{
    [Theory]
    [InlineData("Plain text", "Plain text")]
    [InlineData("Has, comma", "\"Has, comma\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("+94771234567", "'+94771234567")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData(null, "")]
    public void Values_are_quoted_and_formula_injection_is_neutralised(string? input, string expected) =>
        Assert.Equal(expected, Csv.Escape(input));
}

public sealed class SafeXmlTests
{
    [Fact]
    public void Xxe_payloads_are_rejected()
    {
        const string xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///c:/windows/win.ini"> ]>
            <foo>&xxe;</foo>
            """;

        Assert.Throws<XmlException>(() => SafeXml.Parse(xxe));
    }

    [Fact]
    public void Normal_xml_parses() => Assert.Equal("a", SafeXml.Parse("<a>1</a>").Root!.Name.LocalName);
}
