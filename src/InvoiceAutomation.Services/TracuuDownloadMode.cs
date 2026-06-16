namespace InvoiceAutomation.Services;

public enum TracuuDownloadMode
{
    /// <summary>Mode 1 — captcha thủ công: chờ download khi có gợi ý tra cứu.</summary>
    ManualCaptcha,

    /// <summary>Mode 3 — tự động In PDF; bỏ qua khi có gợi ý tra cứu.</summary>
    AutoPrint,

    /// <summary>Auto batch — gợi ý tra cứu + link saveinvoice-pdf → fetch API (không captcha).</summary>
    AutoApiLink
}
