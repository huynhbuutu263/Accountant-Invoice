namespace InvoiceAutomation.Core.Models;

/// <summary>Parsed e-invoice XML fields used for issuer lookup and PDF download.</summary>
public sealed class ParsedInvoiceXml
{
    public string FilePath { get; init; } = "";
    public string SellerMst { get; init; } = "";
    public string SellerName { get; init; } = "";
    public string InvoiceSerial { get; init; } = "";
    public string InvoiceForm { get; init; } = "";
    public string InvoiceNumber { get; init; } = "";
    public DateTime? IssueDate { get; init; }
    public string? LookupCode { get; init; }
    public string? LookupUrl { get; init; }
    public string? ResolvedIssuerId { get; set; }
    public string? ResolvedIssuerName { get; set; }
}
