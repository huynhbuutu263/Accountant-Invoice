using System.Xml.Linq;
using InvoiceAutomation.Services;

namespace InvoiceAutomation.Tests;

public sealed class InvoicePathBuilderTests
{
    [Fact]
    public void Builds_path_from_invoice_xml()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <HDon>
              <DLHDon>
                <TTChung><NLap>2025-06-15</NLap></TTChung>
                <NDHDon>
                  <NBan><Ten>GRAB</Ten><MST>0312345678</MST></NBan>
                  <NMua><MST>0317045289</MST></NMua>
                  <TTHDLQuan><KHMSHDon>C25TAA</KHMSHDon><SHDon>419454</SHDon></TTHDLQuan>
                </NDHDon>
              </DLHDon>
            </HDon>
            """;

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "invoice.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, xml);

        Assert.True(InvoicePathBuilder.TryBuildFromXmlFile(path, null, out var relativePath));
        Assert.Equal(
            Path.Combine("0317045289", "06-2025", "15-06-2025_C25TAA_419454_GRAB"),
            relativePath);
    }

    [Fact]
    public void Uses_buyer_mst_override_when_xml_missing_nmua()
    {
        var doc = XDocument.Parse("""
            <HDon>
              <DLHDon>
                <TTChung><NLap>15/06/2025</NLap></TTChung>
                <NDHDon>
                  <NBan><Ten>ACME Corp</Ten></NBan>
                  <TTHDLQuan><SHDon>HD-001</SHDon></TTHDLQuan>
                </NDHDon>
              </DLHDon>
            </HDon>
            """);

        Assert.True(InvoicePathBuilder.TryBuildFromDocument(doc, "0123456789", out var relativePath));
        Assert.Contains("0123456789", relativePath);
        Assert.Contains("HD-001", relativePath);
        Assert.Contains("ACME Corp", relativePath);
    }

    [Fact]
    public void Rejects_download_error_xml()
    {
        var doc = XDocument.Parse("""
            <HDon>
              <DLHDon>
                <TTChung><THDon>Download Error</THDon><NLap>2026-06-03</NLap></TTChung>
                <NDHDon>
                  <NBan><Ten>[Lỗi tải] timeout</Ten></NBan>
                  <TTHDLQuan><SHDon>ERROR</SHDon></TTHDLQuan>
                </NDHDon>
              </DLHDon>
            </HDon>
            """);

        Assert.False(InvoicePathBuilder.TryBuildFromDocument(doc, "0123456789", out _));
    }
}
