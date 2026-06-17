namespace InvoiceAutomation.Core;

public static class InvoiceKinds
{
    public const string Purchase = "purchase";
    public const string Sales = "sales";

    public static bool IsPurchase(string? kind) =>
        string.Equals(kind, Purchase, StringComparison.OrdinalIgnoreCase);

    public static bool IsSales(string? kind) =>
        string.Equals(kind, Sales, StringComparison.OrdinalIgnoreCase);
}
