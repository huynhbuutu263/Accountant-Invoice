using System.Globalization;
using System.Xml.Linq;
using InvoiceAutomation.Core.Models;

namespace InvoiceAutomation.Services;

public sealed class InvoiceXmlParser
{
    private static readonly string[] DefaultLookupFields =
    [
        "Fkey", "MaTraCuu", "MaTCuu", "MaTC", "Mã tra cứu", "SearchKey", "Mã số bí mật",
        "Extra2", "TransactionID", "Sbl", "Key", "MaCQT"
    ];

    private static readonly string[] DefaultLinkFields =
        ["DCTC", "PortalLink", "Link", "LinkTraCuu", "UrlTraCuu", "Website"];

    public ParsedInvoiceXml Parse(string filePath, IssuerDefinition? issuer = null)
    {
        var doc = XDocument.Load(filePath);
        string[] lookupFields = issuer?.LookupFieldPriority is { Count: > 0 } lf
            ? [.. lf]
            : DefaultLookupFields;
        string[] linkFields = issuer?.DirectLinkFieldPriority is { Count: > 0 } df
            ? [.. df]
            : DefaultLinkFields;

        string? First(params string[] localNames)
        {
            foreach (var name in localNames)
            {
                var v = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }
            return null;
        }

        string? FirstFromTtKhac(params string[] tTruongNames)
        {
            foreach (var ttin in doc.Descendants().Where(x => x.Name.LocalName == "TTin"))
            {
                var fieldName = ttin.Elements().FirstOrDefault(x => x.Name.LocalName == "TTruong")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(fieldName))
                    continue;

                foreach (var name in tTruongNames)
                {
                    if (!string.Equals(fieldName, name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = ttin.Elements().FirstOrDefault(x => x.Name.LocalName == "DLieu")?.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            return null;
        }

        string? FirstFromEmbeddedDlieu(params string[] localNames)
        {
            foreach (var ttin in doc.Descendants().Where(x => x.Name.LocalName == "TTin"))
            {
                var dlieu = ttin.Elements().FirstOrDefault(x => x.Name.LocalName == "DLieu")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(dlieu) || !dlieu.Contains('<', StringComparison.Ordinal))
                    continue;

                try
                {
                    var inner = XDocument.Parse($"<root>{dlieu}</root>");
                    foreach (var name in localNames)
                    {
                        var v = inner.Descendants()
                            .FirstOrDefault(x => x.Name.LocalName == name)?.Value?.Trim();
                        if (!string.IsNullOrWhiteSpace(v))
                            return v;
                    }
                }
                catch (Exception)
                {
                    // DLieu may contain partial XML; skip invalid fragments.
                }
            }

            return null;
        }

        string? FirstLookup(params string[] names) =>
            FirstFromTtKhac(names) ?? FirstFromEmbeddedDlieu(names) ?? First(names);

        string? FirstLink(params string[] names)
        {
            var url = FirstFromEmbeddedDlieu(names) ?? FirstFromTtKhac(names) ?? First(names);
            return NormalizeLookupUrl(url);
        }

        string? FirstFromNb(string localName) =>
            doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "NBan")
                ?.Elements().FirstOrDefault(x => x.Name.LocalName == localName)?.Value?.Trim();

        DateTime? issueDate = null;
        var dateRaw = First("NLap", "TNgay");
        if (!string.IsNullOrWhiteSpace(dateRaw) &&
            DateTime.TryParse(dateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            issueDate = d;
        else if (!string.IsNullOrWhiteSpace(dateRaw) &&
                 DateTime.TryParse(dateRaw, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out var dVi))
            issueDate = dVi;

        return new ParsedInvoiceXml
        {
            FilePath = filePath,
            SellerMst = FirstFromNb("MST") ?? First("MST") ?? "",
            SellerName = FirstFromNb("Ten") ?? First("Ten") ?? "",
            InvoiceSerial = First("KHMSHDon") ?? "",
            InvoiceForm = First("KHHDon") ?? "",
            InvoiceNumber = First("SHDon") ?? "",
            IssueDate = issueDate,
            LookupCode = FirstLookup(lookupFields),
            LookupUrl = FirstLink(linkFields)
        };
    }

    private static string? NormalizeLookupUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        url = url.Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;

        if (!url.Contains("://", StringComparison.Ordinal) &&
            Uri.TryCreate("https://" + url, UriKind.Absolute, out _))
            return "https://" + url;

        return url;
    }
}
