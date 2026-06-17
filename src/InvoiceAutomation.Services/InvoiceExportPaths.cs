using System.Xml.Linq;
using InvoiceAutomation.Core;

namespace InvoiceAutomation.Services;

/// <summary>
/// Export / sắp xếp hóa đơn theo đường dẫn suy ra từ XML (mua vào / bán ra).
/// Cấu trúc: <c>{root}\{MST}\{MM-yyyy}\{dd-MM-yyyy}_{maHD}_{ten}\invoice.xml</c> và <c>pdf\</c>.
/// </summary>
public static class InvoiceExportPaths
{
    public readonly record struct ExportResult(int Relocated, int Exported, int Skipped);

    public static bool TryResolveInvoiceFolder(
        string xmlFilePath,
        string invoiceKind,
        out string relativePath)
    {
        relativePath = "";
        if (string.IsNullOrWhiteSpace(invoiceKind))
            invoiceKind = InvoiceKinds.Purchase;

        try
        {
            var doc = XDocument.Load(xmlFilePath);
            if (InvoicePathBuilder.IsErrorInvoiceDocument(doc))
                return false;

            return InvoicePathBuilder.TryBuildFromDocument(doc, mstOverride: null, out relativePath, invoiceKind);
        }
        catch
        {
            return false;
        }
    }

    public static ExportResult ExportAllDownloads(
        string invoiceDataRoot,
        string exportRoot,
        string invoiceKind = InvoiceKinds.Purchase,
        string pdfSubfolder = "pdf")
    {
        if (string.IsNullOrWhiteSpace(invoiceDataRoot) || !Directory.Exists(invoiceDataRoot))
            return default;
        if (string.IsNullOrWhiteSpace(exportRoot))
            return default;

        var dataRoot = Path.GetFullPath(invoiceDataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var exportRootFull = Path.GetFullPath(exportRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Directory.CreateDirectory(exportRootFull);

        var relocated = 0;
        var exported = 0;
        var skipped = 0;

        foreach (var xmlPath in Directory.EnumerateFiles(dataRoot, "invoice.xml", SearchOption.AllDirectories).ToList())
        {
            if (!TryResolveInvoiceFolder(xmlPath, invoiceKind, out var relativePath))
            {
                skipped++;
                continue;
            }

            var correctDataDir = Path.GetFullPath(Path.Combine(dataRoot, relativePath));
            var currentXml = xmlPath;

            if (!DirectoryEquals(Path.GetDirectoryName(xmlPath), correctDataDir))
            {
                MoveInvoiceFiles(xmlPath, correctDataDir, pdfSubfolder, dataRoot);
                currentXml = Path.Combine(correctDataDir, Path.GetFileName(xmlPath));
                relocated++;
            }

            var exportDir = Path.GetFullPath(Path.Combine(exportRootFull, relativePath));
            CopyInvoiceFiles(currentXml, exportDir, pdfSubfolder);
            exported++;
        }

        return new ExportResult(relocated, exported, skipped);
    }

    public static void MoveInvoiceFiles(
        string xmlFilePath,
        string targetDir,
        string pdfSubfolder,
        string? cleanupStopRoot = null)
    {
        if (!File.Exists(xmlFilePath))
            throw new FileNotFoundException("Invoice XML not found.", xmlFilePath);

        Directory.CreateDirectory(targetDir);
        var sourceDir = Path.GetDirectoryName(xmlFilePath)
            ?? throw new ArgumentException("Invalid XML path.", nameof(xmlFilePath));

        var destXml = Path.Combine(targetDir, Path.GetFileName(xmlFilePath));
        MoveFile(destXml, xmlFilePath);

        MovePdfFolder(
            Path.Combine(sourceDir, pdfSubfolder),
            Path.Combine(targetDir, pdfSubfolder));

        if (!string.IsNullOrWhiteSpace(cleanupStopRoot))
            TryCleanupEmptyParents(sourceDir, Path.GetFullPath(cleanupStopRoot));
    }

    public static void CopyInvoiceFiles(string xmlFilePath, string targetDir, string pdfSubfolder)
    {
        if (!File.Exists(xmlFilePath))
            throw new FileNotFoundException("Invoice XML not found.", xmlFilePath);

        Directory.CreateDirectory(targetDir);
        var sourceDir = Path.GetDirectoryName(xmlFilePath)
            ?? throw new ArgumentException("Invalid XML path.", nameof(xmlFilePath));

        var destXml = Path.Combine(targetDir, Path.GetFileName(xmlFilePath));
        File.Copy(xmlFilePath, destXml, overwrite: true);

        CopyPdfFolder(
            Path.Combine(sourceDir, pdfSubfolder),
            Path.Combine(targetDir, pdfSubfolder));
    }

    private static void MovePdfFolder(string sourcePdfDir, string targetPdfDir)
    {
        if (!Directory.Exists(sourcePdfDir))
            return;

        Directory.CreateDirectory(targetPdfDir);
        foreach (var file in Directory.EnumerateFiles(sourcePdfDir))
        {
            var dest = Path.Combine(targetPdfDir, Path.GetFileName(file));
            MoveFile(dest, file);
        }

        TryDeleteIfEmpty(sourcePdfDir);
    }

    private static void CopyPdfFolder(string sourcePdfDir, string targetPdfDir)
    {
        if (!Directory.Exists(sourcePdfDir))
            return;

        Directory.CreateDirectory(targetPdfDir);
        foreach (var file in Directory.EnumerateFiles(sourcePdfDir))
        {
            var dest = Path.Combine(targetPdfDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void MoveFile(string dest, string source)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            return;

        var parent = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        if (File.Exists(dest))
            File.Delete(dest);

        try
        {
            File.Move(source, dest);
        }
        catch (IOException)
        {
            File.Copy(source, dest, overwrite: true);
            File.Delete(source);
        }
    }

    private static bool DirectoryEquals(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        return string.Equals(
            Path.GetFullPath(a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Path.GetFullPath(b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void TryCleanupEmptyParents(string dir, string stopRoot)
    {
        var current = dir;
        while (!string.IsNullOrEmpty(current)
               && !DirectoryEquals(current, stopRoot)
               && current.StartsWith(stopRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryDeleteIfEmpty(current))
                break;
            current = Path.GetDirectoryName(current);
        }
    }

    private static bool TryDeleteIfEmpty(string dir)
    {
        try
        {
            if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any())
                return false;
            Directory.Delete(dir);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
