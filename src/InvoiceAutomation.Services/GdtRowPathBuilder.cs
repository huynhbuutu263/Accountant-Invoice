using System.Text.RegularExpressions;

namespace InvoiceAutomation.Services;

/// <summary>Builds folder/file paths from GDT Ant Design table row cells (by column index).</summary>
public static partial class GdtRowPathBuilder
{
    public const int MinColumnCount = 11;
    public const int MstColumnIndex = 1;
    public const int MaHoaDonColumnIndex = 4;
    public const int ExportDayColumnIndex = 5;
    public const int SellerColumnIndex = 7;

    public static bool TryBuildRelativePath(string rowInnerText, out string relativePath) =>
        TryBuildFromCells(TokenizeRowText(rowInnerText), out relativePath);

    public static bool TryBuildFromCells(IReadOnlyList<string> cells, out string relativePath)
    {
        relativePath = "";
        if (cells.Count <= SellerColumnIndex)
            return false;

        var splitCells = cells.SelectMany(s => s.Split('\n')).ToList();
        try
        {
            var mstLogin = SanitizePathSegment(splitCells[MstColumnIndex]);
            var maHoaDon = SanitizePathSegment(splitCells[MaHoaDonColumnIndex]);
            var exportDay = SanitizePathSegment(splitCells[ExportDayColumnIndex].Replace('/', '-'));
            var monthParts = exportDay.Split('-', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (monthParts.Count < 2)
                return false;
            monthParts.RemoveAt(0);
            var month = string.Join('-', monthParts.Select(SanitizePathSegment));

            var sellerRaw = splitCells[SellerColumnIndex];
            var sellerName = sellerRaw.Contains(':')
                ? SanitizePathSegment(sellerRaw.Split(':', 2)[1].Trim())
                : SanitizePathSegment(sellerRaw);

            var fileName = string.Join('_', exportDay, maHoaDon, sellerName);
            relativePath = Path.Combine(mstLogin, month, fileName);
            return !string.IsNullOrWhiteSpace(relativePath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Splits row text on newlines, tabs, or runs of 2+ spaces (Ant Design often has no tab chars).</summary>
    public static List<string> TokenizeRowText(string rowInnerText)
    {
        var cells = new List<string>();
        if (string.IsNullOrWhiteSpace(rowInnerText))
            return cells;

        foreach (var line in rowInnerText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Contains('\t'))
            {
                cells.AddRange(line.Split('\t', StringSplitOptions.TrimEntries));
                continue;
            }

            cells.AddRange(MultiSpaceSplit().Split(line).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        return cells;
    }

    public static string SanitizePathSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return "_";

        foreach (var c in Path.GetInvalidFileNameChars())
            segment = segment.Replace(c, '_');

        segment = segment.Trim().TrimEnd('.');
        if (segment.Length == 0)
            return "_";
        if (segment.Length > 120)
            segment = segment[..120];
        return segment;
    }

    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex MultiSpaceSplit();
}
