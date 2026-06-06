using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InvoiceAutomation.Core.Models
{

    public class InvoiceUploadService : IInvoiceUploadService
    {
        //private IPlaywright? _playwright;
        //private IBrowser? _browser;
        //private IPage? _page;

        public async Task UploadAsync(string filePath)
        {
            await Task.Delay(500);
            //if (_page == null)
            //{
            //    _playwright = await Playwright.CreateAsync();

            //    _browser = await _playwright.Chromium.LaunchAsync(
            //        new BrowserTypeLaunchOptions
            //        {
            //            Headless = false
            //        });

            //    _page = await _browser.NewPageAsync();

            //    await _page.GotoAsync("https://tracuuhoadon.vn/");
            //}

            //await _page.SetInputFilesAsync(
            //    "input[type='file']",
            //    filePath);
        }
    }
}
