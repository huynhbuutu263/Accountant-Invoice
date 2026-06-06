using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InvoiceAutomation.Core.Models
{
    public interface IInvoiceUploadService
    {
        Task UploadAsync(string filePath);
    }
}
