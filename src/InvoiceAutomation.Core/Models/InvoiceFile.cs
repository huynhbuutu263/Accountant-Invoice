using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace InvoiceAutomation.Core.Models;

public class InvoiceFile : INotifyPropertyChanged
{
    private int _stt;
    private bool _hasPdf;

    public int Stt
    {
        get => _stt;
        set => SetField(ref _stt, value);
    }

    public bool HasPdf
    {
        get => _hasPdf;
        set => SetField(ref _hasPdf, value);
    }

    public string TaxCode { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string InvoiceNo { get; set; } = "";
    public DateTime? InvoiceDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string FilePath { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
