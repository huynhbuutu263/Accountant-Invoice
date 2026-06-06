namespace InvoiceAutomation.Services;

public sealed class IssuerCatalog
{
    public List<IssuerDefinition> Issuers { get; set; } = [];
}

public sealed class IssuerDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> MstPrefixes { get; set; } = [];
    public List<string> LookupFieldPriority { get; set; } = [];
    public List<string> DirectLinkFieldPriority { get; set; } = [];
    public string PdfUrlTemplate { get; set; } = "";
    public string? Comment { get; set; }
}
