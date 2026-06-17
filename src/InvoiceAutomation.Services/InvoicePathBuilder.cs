using System.Globalization;
using System.Xml.Linq;
using InvoiceAutomation.Core;

namespace InvoiceAutomation.Services;

/// <summary>Builds invoice folder paths from XML content (preferred) or GDT table row cells (fallback).</summary>
public static class InvoicePathBuilder
{
    public static bool TryBuildFromXmlFile(
        string xmlFilePath,
        string? mstOverride,
        out string relativePath,
        string invoiceKind = InvoiceKinds.Purchase)
    {
        relativePath = "";
        try
        {
            var doc = XDocument.Load(xmlFilePath);
            return TryBuildFromDocument(doc, mstOverride, out relativePath, invoiceKind);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryBuildFromDocument(
        XDocument doc,
        string? mstOverride,
        out string relativePath,
        string invoiceKind = InvoiceKinds.Purchase)
    {
        relativePath = "";
        if (IsErrorInvoiceDocument(doc))
            return false;

        string? First(string localName) =>
            doc.Descendants().FirstOrDefault(x => x.Name.LocalName == localName)?.Value?.Trim();

        var parties = InvoicePartyResolver.Resolve(doc, invoiceKind, mstOverride);
        if (string.IsNullOrWhiteSpace(parties.OwnerMst) || string.IsNullOrWhiteSpace(parties.CounterpartyName))
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

        return TryBuildRelativePath(
            parties.OwnerMst, issueDate, invoiceNo, parties.CounterpartyName, out relativePath);
    }

    public static bool TryBuildRelativePath(
        string ownerMst,
        DateTime issueDate,
        string invoiceNo,
        string counterpartyName,
        out string relativePath)
    {
        relativePath = "";
        if (string.IsNullOrWhiteSpace(ownerMst) ||
            string.IsNullOrWhiteSpace(invoiceNo) ||
            string.IsNullOrWhiteSpace(counterpartyName))
            return false;

        var exportDay = GdtRowPathBuilder.SanitizePathSegment(issueDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture));
        var month = GdtRowPathBuilder.SanitizePathSegment(issueDate.ToString("MM-yyyy", CultureInfo.InvariantCulture));
        var maHoaDon = GdtRowPathBuilder.SanitizePathSegment(invoiceNo);
        var counterparty = GdtRowPathBuilder.SanitizePathSegment(counterpartyName);
        var fileName = string.Join('_', exportDay, maHoaDon, counterparty);
        relativePath = Path.Combine(GdtRowPathBuilder.SanitizePathSegment(ownerMst), month, fileName);
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
