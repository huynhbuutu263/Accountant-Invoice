using InvoiceAutomation.Core;
using InvoiceAutomation.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvoiceAutomation.Services;

public sealed class IssuerPdfLookupService : IInvoicePdfLookupService
{
    private readonly ILogger<IssuerPdfLookupService> _logger;
    private readonly InvoiceXmlParser _parser = new();
    private readonly string _issuersConfigPath;

    public IssuerPdfLookupService(ILogger<IssuerPdfLookupService> logger, string issuersConfigPath)
    {
        _logger = logger;
        _issuersConfigPath = issuersConfigPath;
    }

    public bool HasTraCuuLookup(string xmlFilePath)
    {
        if (!File.Exists(xmlFilePath))
            return false;

        var (parsed, issuer) = ParseWithIssuer(xmlFilePath);
        if (!string.IsNullOrWhiteSpace(parsed.LookupUrl)
            && Uri.TryCreate(parsed.LookupUrl.Trim(), UriKind.Absolute, out _))
            return true;

        if (!string.IsNullOrWhiteSpace(parsed.LookupCode))
            return true;

        return !string.IsNullOrWhiteSpace(BuildDownloadUrl(parsed, issuer));
    }

    public string ResolveLookupUrl(string xmlFilePath)
    {
        var (parsed, issuer) = ParseWithIssuer(xmlFilePath);
        _logger.LogInformation(
            "Issuer lookup URL: issuer={Issuer}, MST={Mst}, lookupCode={Code}, link={Link}",
            issuer.Name, parsed.SellerMst, parsed.LookupCode, parsed.LookupUrl);

        var downloadUrl = BuildDownloadUrl(parsed, issuer);
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException(
                "Không có URL tra cứu. Cấu hình pdfUrlTemplate trong issuers.json " +
                "hoặc đảm bảo XML có Fkey/MaTraCuu/Link tra cứu.");
        }

        return downloadUrl;
    }

    public async Task<string> DownloadPdfAsync(
        string xmlFilePath,
        string? pdfOutputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(xmlFilePath))
            throw new FileNotFoundException("XML not found.", xmlFilePath);

        var downloadUrl = ResolveLookupUrl(xmlFilePath);

        var outDir = pdfOutputDirectory ?? Path.Combine(Path.GetDirectoryName(xmlFilePath) ?? ".", "pdf");
        Directory.CreateDirectory(outDir);
        var pdfPath = Path.Combine(outDir, InvoicePdfPaths.ResolvePdfBaseName(xmlFilePath) + ".pdf");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("InvoiceAutomation/1.0");

        using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var tmp = pdfPath + ".tmp";
        await using (var fs = File.Create(tmp))
            await stream.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);

        if (!FileProcessor.LooksLikePdf(tmp))
        {
            File.Delete(tmp);
            throw new InvalidOperationException(
                "Phản hồi không phải file PDF. Kiểm tra endpoint trong issuers.json hoặc mã tra cứu.");
        }

        if (File.Exists(pdfPath))
            File.Delete(pdfPath);
        File.Move(tmp, pdfPath);

        _logger.LogInformation("PDF saved to {Path}", pdfPath);
        return pdfPath;
    }

    private (ParsedInvoiceXml Parsed, IssuerDefinition Issuer) ParseWithIssuer(string xmlFilePath)
    {
        if (!File.Exists(xmlFilePath))
            throw new FileNotFoundException("XML not found.", xmlFilePath);

        var registry = new IssuerRegistry(_issuersConfigPath);
        var issuer = registry.Resolve(_parser.Parse(xmlFilePath, null));
        var parsed = _parser.Parse(xmlFilePath, issuer);
        parsed.ResolvedIssuerId = issuer.Id;
        parsed.ResolvedIssuerName = issuer.Name;
        return (parsed, issuer);
    }

    private static string? BuildDownloadUrl(ParsedInvoiceXml invoice, IssuerDefinition issuer)
    {
        if (!string.IsNullOrWhiteSpace(invoice.LookupUrl) &&
            Uri.TryCreate(invoice.LookupUrl.Trim(), UriKind.Absolute, out _))
            return invoice.LookupUrl.Trim();

        if (string.IsNullOrWhiteSpace(issuer.PdfUrlTemplate))
            return null;

        var url = issuer.PdfUrlTemplate;
        url = url.Replace("{lookupCode}", Uri.EscapeDataString(invoice.LookupCode ?? ""), StringComparison.Ordinal);
        url = url.Replace("{sellerMst}", Uri.EscapeDataString(invoice.SellerMst), StringComparison.Ordinal);
        url = url.Replace("{sellerName}", Uri.EscapeDataString(invoice.SellerName), StringComparison.Ordinal);
        url = url.Replace("{invoiceNumber}", Uri.EscapeDataString(invoice.InvoiceNumber), StringComparison.Ordinal);
        url = url.Replace("{invoiceSerial}", Uri.EscapeDataString(invoice.InvoiceSerial), StringComparison.Ordinal);
        url = url.Replace("{invoiceForm}", Uri.EscapeDataString(invoice.InvoiceForm), StringComparison.Ordinal);
        url = url.Replace("{issueDate}", Uri.EscapeDataString(
            invoice.IssueDate?.ToString("yyyy-MM-dd") ?? ""), StringComparison.Ordinal);

        return Uri.TryCreate(url, UriKind.Absolute, out _) ? url : null;
    }
}
