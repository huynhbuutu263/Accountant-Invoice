namespace InvoiceAutomation.Services;

/// <summary>
/// Copies PDF/ZIP to <c>{exportRoot}\{MST}\{MM-yyyy}\{file}</c> (no <c>pdf\</c>, no invoice subfolder).</summary>
public static class InvoiceExportPaths
{
    public static string BuildExportPath(
        string xmlFilePath,
        string downloadedFilePath,
        string invoiceDataRoot,
        string exportRoot)
    {
        var fileName = Path.GetFileName(downloadedFilePath);
        var downloadDir = Path.GetDirectoryName(downloadedFilePath);
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(downloadDir))
            throw new ArgumentException("Invalid downloaded file path.", nameof(downloadedFilePath));

        if (!string.IsNullOrWhiteSpace(invoiceDataRoot))
        {
            var dataRoot = Path.GetFullPath(invoiceDataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var fullDownloadDir = Path.GetFullPath(downloadDir);

            if (fullDownloadDir.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(dataRoot, fullDownloadDir);
                var segments = relative
                    .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

                if (segments.Count > 0
                    && segments[^1].Equals("pdf", StringComparison.OrdinalIgnoreCase))
                    segments.RemoveAt(segments.Count - 1);

                // Drop invoice folder — export into month folder only
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);

                return Path.Combine([exportRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), .. segments, fileName]);
            }
        }

        return BuildExportPathFromXmlFolder(xmlFilePath, exportRoot, fileName);
    }

    public static string CopyDownloadToExport(
        string xmlFilePath,
        string downloadedFilePath,
        string invoiceDataRoot,
        string exportRoot,
        bool overwrite = true)
    {
        if (string.IsNullOrWhiteSpace(exportRoot))
            throw new ArgumentException("Export root is required.", nameof(exportRoot));
        if (!File.Exists(downloadedFilePath))
            throw new FileNotFoundException("Downloaded file not found.", downloadedFilePath);

        var dest = BuildExportPath(xmlFilePath, downloadedFilePath, invoiceDataRoot, exportRoot);
        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        File.Copy(downloadedFilePath, dest, overwrite);
        return dest;
    }

    public static int ExportAllDownloads(string invoiceDataRoot, string exportRoot, string pdfSubfolder = "pdf")
    {
        if (string.IsNullOrWhiteSpace(invoiceDataRoot) || !Directory.Exists(invoiceDataRoot))
            return 0;
        if (string.IsNullOrWhiteSpace(exportRoot))
            return 0;

        var count = 0;
        foreach (var xmlPath in Directory.EnumerateFiles(invoiceDataRoot, "invoice.xml", SearchOption.AllDirectories))
        {
            var invoiceDir = Path.GetDirectoryName(xmlPath);
            if (string.IsNullOrWhiteSpace(invoiceDir))
                continue;

            var pdfDir = Path.Combine(invoiceDir, pdfSubfolder);
            if (!Directory.Exists(pdfDir))
                continue;

            foreach (var ext in new[] { "*.pdf", "*.zip" })
            {
                foreach (var downloaded in Directory.EnumerateFiles(pdfDir, ext))
                {
                    CopyDownloadToExport(xmlPath, downloaded, invoiceDataRoot, exportRoot);
                    count++;
                }
            }
        }

        return count;
    }

    private static string BuildExportPathFromXmlFolder(string xmlFilePath, string exportRoot, string fileName)
    {
        var invoiceDir = Path.GetDirectoryName(xmlFilePath)
            ?? throw new ArgumentException("Invalid XML path.", nameof(xmlFilePath));
        var monthDir = Path.GetDirectoryName(invoiceDir)
            ?? throw new ArgumentException("Invalid XML path.", nameof(xmlFilePath));
        var mstDir = Path.GetDirectoryName(monthDir)
            ?? throw new ArgumentException("Invalid XML path.", nameof(xmlFilePath));

        return Path.Combine(
            exportRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFileName(mstDir),
            Path.GetFileName(monthDir),
            fileName);
    }
}
