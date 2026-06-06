using System.Text;

namespace InvoiceAutomation.Core;

public static class DownloadErrorInvoiceXml
{
    public const string ErrorMarker = "Download Error";

    public static string PathForZipSave(string zipSavePath) =>
        Path.Combine(
            Path.GetDirectoryName(zipSavePath) ?? ".",
            Path.GetFileNameWithoutExtension(zipSavePath),
            "invoice.xml");

    public static void Write(string zipSavePath, string errorMessage)
    {
        var xmlPath = PathForZipSave(zipSavePath);
        var dir = Path.GetDirectoryName(xmlPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var shortMsg = errorMessage.Length > 200 ? errorMessage[..200] + "…" : errorMessage;
        var escapedShort = XmlEscape(shortMsg);
        var escapedFull = XmlEscape(errorMessage);
        var now = DateTime.Now.ToString("yyyy-MM-dd");

        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <HDon>
              <DLHDon>
                <TTChung>
                  <THDon>{ErrorMarker}</THDon>
                  <NLap>{now}</NLap>
                </TTChung>
                <NDHDon>
                  <NBan>
                    <Ten>[Lỗi tải] {escapedShort}</Ten>
                    <MST></MST>
                  </NBan>
                  <TTHDLQuan>
                    <SHDon>ERROR</SHDon>
                  </TTHDLQuan>
                </NDHDon>
                <TTKhac>
                  <TTin>
                    <TTruong>Error</TTruong>
                    <DLieu>{escapedFull}</DLieu>
                  </TTin>
                </TTKhac>
              </DLHDon>
            </HDon>
            """;

        if (File.Exists(xmlPath))
            File.Delete(xmlPath);
        File.WriteAllText(xmlPath, xml, Encoding.UTF8);
    }

    private static string XmlEscape(string s) =>
        System.Security.SecurityElement.Escape(s) ?? "";
}
