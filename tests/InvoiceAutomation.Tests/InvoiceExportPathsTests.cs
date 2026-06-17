using InvoiceAutomation.Core;
using InvoiceAutomation.Services;

namespace InvoiceAutomation.Tests;

public sealed class InvoiceExportPathsTests
{
    private const string SampleXml = """
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

    [Fact]
    public void Export_uses_purchase_radio_for_folder_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), "export-test-" + Guid.NewGuid().ToString("N"));
        var exportRoot = Path.Combine(root, "export");
        var wrongDir = Path.Combine(root, "wrong-mst", "06-2025", "old-folder");
        Directory.CreateDirectory(wrongDir);
        Directory.CreateDirectory(Path.Combine(wrongDir, "pdf"));

        var xmlPath = Path.Combine(wrongDir, "invoice.xml");
        File.WriteAllText(xmlPath, SampleXml);
        File.WriteAllText(Path.Combine(wrongDir, "pdf", "invoice.pdf"), "%PDF-1.4 test");

        var result = InvoiceExportPaths.ExportAllDownloads(root, exportRoot, InvoiceKinds.Purchase, "pdf");

        var expectedRelative = Path.Combine("0317045289", "06-2025", "15-06-2025_C25TAA_419454_GRAB");
        var expectedDataDir = Path.Combine(root, expectedRelative);
        var expectedExportDir = Path.Combine(exportRoot, expectedRelative);

        Assert.Equal(1, result.Relocated);
        Assert.Equal(1, result.Exported);
        Assert.True(File.Exists(Path.Combine(expectedDataDir, "invoice.xml")));
        Assert.True(File.Exists(Path.Combine(expectedDataDir, "pdf", "invoice.pdf")));
        Assert.True(File.Exists(Path.Combine(expectedExportDir, "invoice.xml")));
        Assert.True(File.Exists(Path.Combine(expectedExportDir, "pdf", "invoice.pdf")));

        try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Export_uses_sales_radio_for_folder_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), "export-sales-" + Guid.NewGuid().ToString("N"));
        var exportRoot = Path.Combine(root, "export");
        var wrongDir = Path.Combine(root, "0317045289", "06-2025", "old-folder");
        Directory.CreateDirectory(wrongDir);

        var xmlPath = Path.Combine(wrongDir, "invoice.xml");
        File.WriteAllText(xmlPath, SampleXml);

        var result = InvoiceExportPaths.ExportAllDownloads(root, exportRoot, InvoiceKinds.Sales, "pdf");

        var expectedRelative = Path.Combine("0312345678", "06-2025", "15-06-2025_C25TAA_419454_0317045289");
        Assert.Equal(1, result.Relocated);
        Assert.True(File.Exists(Path.Combine(root, expectedRelative, "invoice.xml")));

        try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
    }
}
