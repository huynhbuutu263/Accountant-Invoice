using System.IO.Compression;
using InvoiceAutomation.Core;
using Microsoft.Extensions.Logging;

namespace InvoiceAutomation.Services;

public sealed class FileProcessor : IFileProcessor
{
    private readonly ILogger<FileProcessor> _logger;

    public FileProcessor(ILogger<FileProcessor> logger) => _logger = logger;

    public Task<IReadOnlyList<string>> ExtractZipAsync(string zipPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(zipPath))
        {
            var errXml = DownloadErrorInvoiceXml.PathForZipSave(zipPath);
            if (File.Exists(errXml))
            {
                _logger.LogWarning("Zip not found; download error invoice at {Path}", errXml);
                return Task.FromResult<IReadOnlyList<string>>(new[] { errXml });
            }

            throw new FileNotFoundException(zipPath);
        }

        var destDir = Path.Combine(Path.GetDirectoryName(zipPath) ?? ".", Path.GetFileNameWithoutExtension(zipPath));
        Directory.CreateDirectory(destDir);

        var extracted = new List<string>();
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(name) || name.EndsWith('/'))
                    continue;
                if (name.Contains("..", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Skipping zip entry with '..': {Name}", name);
                    continue;
                }

                var targetPath = Path.GetFullPath(Path.Combine(destDir, name));
                if (!targetPath.StartsWith(Path.GetFullPath(destDir), StringComparison.OrdinalIgnoreCase))
                    continue;

                var parent = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                entry.ExtractToFile(targetPath, overwrite: true);
                extracted.Add(targetPath);
                _logger.LogInformation("Extracted {Path} ({Size} bytes)", targetPath, entry.Length);
            }
        }

        File.Delete(zipPath);
        return Task.FromResult<IReadOnlyList<string>>(extracted);
    }

    public string? RelocateToInvoicePath(
        string extractedFolderPath,
        string downloadsRoot,
        string? rowFallbackRelativePath,
        string? mstOverride = null,
        string invoiceKind = InvoiceKinds.Purchase)
    {
        if (!Directory.Exists(extractedFolderPath))
            return rowFallbackRelativePath;

        if (string.IsNullOrWhiteSpace(downloadsRoot))
            downloadsRoot = extractedFolderPath;

        var xmlPath = FindInvoiceXmlFile(extractedFolderPath);
        string? targetRelative = null;

        if (xmlPath is not null &&
            InvoicePathBuilder.TryBuildFromXmlFile(xmlPath, mstOverride, out var fromXml, invoiceKind))
        {
            targetRelative = fromXml;
            _logger.LogInformation("Invoice path from XML: {Path}", targetRelative);
        }
        else if (!string.IsNullOrWhiteSpace(rowFallbackRelativePath))
        {
            targetRelative = rowFallbackRelativePath;
            _logger.LogInformation("Invoice path from row fallback: {Path}", targetRelative);
        }

        if (string.IsNullOrWhiteSpace(targetRelative))
        {
            _logger.LogWarning("Could not resolve invoice path from XML or row for {Folder}", extractedFolderPath);
            return null;
        }

        var sourceDir = Path.GetFullPath(extractedFolderPath);
        var targetDir = Path.GetFullPath(Path.Combine(downloadsRoot, targetRelative));
        if (string.Equals(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase))
            return targetRelative;

        Directory.CreateDirectory(targetDir);
        CopyDirectoryContents(sourceDir, targetDir);
        TryDeleteDirectoryRecursive(sourceDir);
        TryDeleteEmptyStagingParents(sourceDir, Path.GetFullPath(downloadsRoot));

        _logger.LogInformation("Moved invoice files to {Path}", targetDir);
        return targetRelative;
    }

    public async Task<IReadOnlyList<string>> FinalizeStagingFolderAsync(
        string stagingFolder,
        string downloadsRoot,
        string? mstOverride = null,
        string invoiceKind = InvoiceKinds.Purchase,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<string>();
        if (!Directory.Exists(stagingFolder))
        {
            _logger.LogWarning("Staging folder not found: {Path}", stagingFolder);
            return results;
        }

        foreach (var zipPath in Directory.GetFiles(stagingFolder, "*.zip").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ExtractZipAsync(zipPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract staging zip {Path}", zipPath);
            }
        }

        foreach (var dir in Directory.GetDirectories(stagingFolder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowKey = Path.GetFileName(dir);
            var rowFallback = StagingPaths.ReadRowPathSidecar(stagingFolder, rowKey);

            try
            {
                var finalPath = RelocateToInvoicePath(dir, downloadsRoot, rowFallback, mstOverride, invoiceKind);
                if (!string.IsNullOrWhiteSpace(finalPath))
                    results.Add(finalPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to relocate staging folder {Path}", dir);
            }

            StagingPaths.DeleteRowPathSidecar(stagingFolder, rowKey);
        }

        if (Directory.Exists(stagingFolder) && !Directory.EnumerateFileSystemEntries(stagingFolder).Any())
            TryDeleteDirectoryRecursive(stagingFolder);

        _logger.LogInformation("Finalized {Count} invoice(s) from staging {Path}", results.Count, stagingFolder);
        return results;
    }

    private static string? FindInvoiceXmlFile(string folder)
    {
        var preferred = Path.Combine(folder, "invoice.xml");
        if (File.Exists(preferred))
            return preferred;

        return Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static void CopyDirectoryContents(string sourceDir, string targetDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(targetDir, relative);
            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private void TryDeleteDirectoryRecursive(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete temp folder {Path}", path);
        }
    }

    private static void TryDeleteEmptyStagingParents(string sourceDir, string downloadsRoot)
    {
        var current = Path.GetDirectoryName(sourceDir);
        while (!string.IsNullOrEmpty(current) &&
               !string.Equals(current, downloadsRoot, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any())
                    break;
                Directory.Delete(current);
            }
            catch
            {
                break;
            }

            current = Path.GetDirectoryName(current);
        }
    }

    public static bool LooksLikePdf(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[5];
            return fs.Read(buf, 0, 5) == 5 && buf[0] == (byte)'%' && buf[1] == (byte)'P' && buf[2] == (byte)'D' && buf[3] == (byte)'F';
        }
        catch
        {
            return false;
        }
    }

    public static bool LooksLikeZip(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[4];
            if (fs.Read(buf, 0, 4) != 4)
                return false;

            return buf[0] == (byte)'P' && buf[1] == (byte)'K'
                   && ((buf[2] == 3 && buf[3] == 4) || (buf[2] == 5 && buf[3] == 6) || (buf[2] == 7 && buf[3] == 8));
        }
        catch
        {
            return false;
        }
    }

    public static bool LooksLikeXml(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var line = reader.ReadLine() ?? "";
            return line.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
