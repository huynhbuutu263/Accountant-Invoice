using System.Xml.Linq;
using InvoiceAutomation.Core;

namespace InvoiceAutomation.Services;

/// <summary>
/// Mua vào: MST người mua (NMua) + tên người bán (NBan).
/// Bán ra: MST người bán (NBan) + tên người mua (NMua).
/// </summary>
public readonly record struct InvoicePartyInfo(string OwnerMst, string CounterpartyName);

public static class InvoicePartyResolver
{
    public static InvoicePartyInfo Resolve(XDocument doc, string invoiceKind, string? mstOverride = null)
    {
        string? FirstFromParty(string party, string localName) =>
            doc.Descendants().FirstOrDefault(x => x.Name.LocalName == party)
                ?.Elements().FirstOrDefault(x => x.Name.LocalName == localName)?.Value?.Trim();

        if (InvoiceKinds.IsPurchase(invoiceKind))
        {
            var ownerMst = FirstFromParty("NMua", "MST") ?? mstOverride ?? "";
            var counterparty = FirstFromParty("NBan", "Ten")
                ?? FirstFromParty("NBan", "MST")
                ?? "";
            return new InvoicePartyInfo(ownerMst, counterparty);
        }

        var sellerMst = FirstFromParty("NBan", "MST") ?? mstOverride ?? "";
        var buyerName = FirstFromParty("NMua", "Ten")
            ?? FirstFromParty("NMua", "MST")
            ?? FirstFromParty("NMua", "HVTNMHang")
            ?? "";
        return new InvoicePartyInfo(sellerMst, buyerName);
    }
}
