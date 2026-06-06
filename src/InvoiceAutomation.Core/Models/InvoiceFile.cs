using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InvoiceAutomation.Core.Models
{
    public class InvoiceFile
    {
        public string TaxCode { get; set; } = "";
        public string Supplier { get; set; } = "";
        public string InvoiceNo { get; set; } = "";
        public DateTime? InvoiceDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string FilePath { get; set; } = "";
    }
}
