namespace InvoiceAutomation.Core.Validation;

public sealed class FlowValidationException : Exception
{
    public FlowValidationException(string message) : base(message) { }
}
