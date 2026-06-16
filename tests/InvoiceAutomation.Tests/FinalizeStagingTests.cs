using InvoiceAutomation.Core;
using InvoiceAutomation.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceAutomation.Tests;

public sealed class FinalizeStagingTests
{
    [Fact]
    public async Task FinalizeStaging_extracts_zips_and_relocates_from_xml()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(root, StagingPaths.DefaultFolderName);
        var downloadsRoot = root;
        Directory.CreateDirectory(staging);

        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <HDon>
              <DLHDon>
                <TTChung><NLap>2025-06-15</NLap></TTChung>
                <NDHDon>
                  <NBan><Ten>GRAB</Ten></NBan>
                  <NMua><MST>0317045289</MST></NMua>
                  <TTHDLQuan><SHDon>419454</SHDon></TTHDLQuan>
                </NDHDon>
              </DLHDon>
            </HDon>
            """;

        var zipPath = Path.Combine(staging, "1.zip");
        var extractDir = Path.Combine(staging, "1");
        Directory.CreateDirectory(extractDir);
        File.WriteAllText(Path.Combine(extractDir, "invoice.xml"), xml);
        System.IO.Compression.ZipFile.CreateFromDirectory(extractDir, zipPath);
        Directory.Delete(extractDir, recursive: true);

        StagingPaths.SaveRowPathSidecar(staging, "1", "fallback-should-not-be-used");

        var processor = new FileProcessor(NullLogger<FileProcessor>.Instance);
        var results = await processor.FinalizeStagingFolderAsync(staging, downloadsRoot);

        Assert.Single(results);
        Assert.Contains("0317045289", results[0]);
        Assert.Contains("GRAB", results[0]);
        Assert.True(Directory.Exists(Path.Combine(downloadsRoot, results[0], "invoice.xml")));
        Assert.False(Directory.Exists(staging));
    }
}
