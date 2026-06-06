using System.Globalization;
using System.Xml.Linq;
using InvoiceAutomation.Core;

namespace InvoiceAutomation.Services;

/// <summary>Builds invoice folder paths from XML content (preferred) or GDT table row cells (fallback).</summary>
public static class InvoicePathBuilder
{
    public static bool TryBuildFromXmlFile(string xmlFilePath, string? buyerMstOverride, out string relativePath)
    {
        relativePath = "";
        try
        {
            var doc = XDocument.Load(xmlFilePath);
            return TryBuildFromDocument(doc, buyerMstOverride, out relativePath);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryBuildFromDocument(XDocument doc, string? buyerMstOverride, out string relativePath)
    {
        relativePath = "";
        if (IsErrorInvoiceDocument(doc))
            return false;

        string? First(string localName) =>
            doc.Descendants().FirstOrDefault(x => x.Name.LocalName == localName)?.Value?.Trim();

        string? FirstFromParty(string party, string localName) =>
            doc.Descendants().FirstOrDefault(x => x.Name.LocalName == party)
                ?.Elements().FirstOrDefault(x => x.Name.LocalName == localName)?.Value?.Trim();

        var buyerMst = FirstFromParty("NMua", "MST");
        if (string.IsNullOrWhiteSpace(buyerMst))
            buyerMst = buyerMstOverride;
        if (string.IsNullOrWhiteSpace(buyerMst))
            return false;

        var sellerName = FirstFromParty("NBan", "Ten");
        if (string.IsNullOrWhiteSpace(sellerName))
            sellerName = FirstFromParty("NBan", "MST") ?? First("Ten");
        if (string.IsNullOrWhiteSpace(sellerName))
            return false;

        var serial = First("KHMSHDon");
        var number = First("SHDon");
        var invoiceNo = !string.IsNullOrWhiteSpace(serial) && !string.IsNullOrWhiteSpace(number)
            ? $"{serial}_{number}"
            : number ?? serial ?? "";
        if (string.IsNullOrWhiteSpace(invoiceNo))
            return false;

        var dateRaw = First("NLap") ?? First("TNgay");
        if (string.IsNullOrWhiteSpace(dateRaw) ||
            !TryParseInvoiceDate(dateRaw, out var issueDate))
            return false;

        return TryBuildRelativePath(buyerMst, issueDate, invoiceNo, sellerName, out relativePath);
    }

    public static bool TryBuildRelativePath(
        string buyerMst,
        DateTime issueDate,
        string invoiceNo,
        string sellerName,
        out string relativePath)
    {
        relativePath = "";
        if (string.IsNullOrWhiteSpace(buyerMst) ||
            string.IsNullOrWhiteSpace(invoiceNo) ||
            string.IsNullOrWhiteSpace(sellerName))
            return false;

        var exportDay = GdtRowPathBuilder.SanitizePathSegment(issueDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture));
        var month = GdtRowPathBuilder.SanitizePathSegment(issueDate.ToString("MM-yyyy", CultureInfo.InvariantCulture));
        var maHoaDon = GdtRowPathBuilder.SanitizePathSegment(invoiceNo);
        var seller = GdtRowPathBuilder.SanitizePathSegment(sellerName);
        var fileName = string.Join('_', exportDay, maHoaDon, seller);
        relativePath = Path.Combine(GdtRowPathBuilder.SanitizePathSegment(buyerMst), month, fileName);
        return !string.IsNullOrWhiteSpace(relativePath);
    }

    public static bool IsErrorInvoiceDocument(XDocument doc)
    {
        var thDon = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "THDon")?.Value?.Trim();
        if (string.Equals(thDon, DownloadErrorInvoiceXml.ErrorMarker, StringComparison.OrdinalIgnoreCase))
            return true;

        return doc.Descendants()
            .Where(x => x.Name.LocalName == "TTin")
            .Any(t =>
            {
                var field = t.Elements().FirstOrDefault(x => x.Name.LocalName == "TTruong")?.Value?.Trim();
                return string.Equals(field, "Error", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static bool TryParseInvoiceDate(string raw, out DateTime date)
    {
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;
        if (DateTime.TryParse(raw, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out date))
            return true;

        var normalized = raw.Replace('/', '-');
        return DateTime.TryParseExact(
            normalized,
            ["dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}
