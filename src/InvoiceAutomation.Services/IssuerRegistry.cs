using System.Text.Json;
using InvoiceAutomation.Core.Models;

namespace InvoiceAutomation.Services;

public sealed class IssuerRegistry
{
    private readonly List<IssuerDefinition> _issuers;

    public IssuerRegistry(string configPath)
    {
        if (!File.Exists(configPath))
        {
            _issuers = [new IssuerDefinition { Id = "default", Name = "default" }];
            return;
        }

        var json = File.ReadAllText(configPath);
        var catalog = JsonSerializer.Deserialize<IssuerCatalog>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        _issuers = catalog?.Issuers ?? [];
        if (_issuers.Count == 0)
            _issuers.Add(new IssuerDefinition { Id = "default", Name = "default" });
    }

    public IssuerDefinition Resolve(ParsedInvoiceXml invoice)
    {
        var mst = invoice.SellerMst.Trim();
        IssuerDefinition? wildcard = null;

        foreach (var issuer in _issuers)
        {
            if (issuer.MstPrefixes is not { Count: > 0 })
            {
                wildcard ??= issuer;
                continue;
            }

            foreach (var prefix in issuer.MstPrefixes)
            {
                if (!string.IsNullOrWhiteSpace(prefix) &&
                    mst.StartsWith(prefix.Trim(), StringComparison.Ordinal))
                    return issuer;
            }
        }

        return wildcard ?? _issuers[0];
    }
}
