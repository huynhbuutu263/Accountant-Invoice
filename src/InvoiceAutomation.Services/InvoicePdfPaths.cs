namespace InvoiceAutomation.Services;

/// <summary>PDF output paths next to e-invoice XML (GDT folders use generic <c>invoice.xml</c>).</summary>
public static class InvoicePdfPaths
{
    public static string BuildPdfPath(string xmlFilePath, string pdfSubfolder = "pdf")
    {
        var xmlDir = Path.GetDirectoryName(xmlFilePath) ?? ".";
        var pdfDir = Path.Combine(xmlDir, pdfSubfolder);
        return Path.Combine(pdfDir, ResolvePdfBaseName(xmlFilePath) + ".pdf");
    }

    /// <summary>
    /// When XML is <c>invoice.xml</c>, name PDF after the parent folder
    /// (e.g. <c>27-12-2025_1_76713_TEN NCC.pdf</c>).
    /// </summary>
    public static string ResolvePdfBaseName(string xmlFilePath)
    {
        var xmlBase = Path.GetFileNameWithoutExtension(xmlFilePath);
        if (!xmlBase.Equals("invoice", StringComparison.OrdinalIgnoreCase))
            return xmlBase;

        var parentFolder = Path.GetFileName(Path.GetDirectoryName(xmlFilePath) ?? "");
        return string.IsNullOrWhiteSpace(parentFolder) ? xmlBase : parentFolder;
    }

    public static bool HasDownloadedFile(string xmlFilePath, string pdfSubfolder = "pdf")
    {
        var pdfPath = BuildPdfPath(xmlFilePath, pdfSubfolder);
        var pdfDir = Path.GetDirectoryName(pdfPath) ?? ".";
        if (File.Exists(pdfPath))
            return true;

        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        return Directory.Exists(pdfDir)
               && (File.Exists(Path.Combine(pdfDir, baseName + ".zip"))
                   || Directory.EnumerateFiles(pdfDir, baseName + ".*").Any());
    }
}
