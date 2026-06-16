using InvoiceAutomation.Services;

namespace InvoiceAutomation.Tests;

public sealed class GdtRowPathBuilderTests
{
    [Fact]
    public void Builds_path_from_cell_list()
    {
        var cells = Enumerable.Range(0, 21).Select(i => $"c{i}").ToList();
        cells[1] = "0123456789";
        cells[6] = "HD-001";
        cells[7] = "15/06/2025";
        cells[10] = "Người bán:ACME Corp";

        Assert.True(GdtRowPathBuilder.TryBuildFromCells(cells, out var path));
        Assert.Equal(
            Path.Combine("0123456789", "06-2025", "15-06-2025", "15-06-2025_HD-001_ACME Corp"),
            path);
    }

    [Fact]
    public void Builds_path_from_newline_separated_inner_text()
    {
        var cells = Enumerable.Range(0, 21).Select(i => $"c{i}").ToList();
        cells[1] = "0123456789";
        cells[6] = "HD-001";
        cells[7] = "15/06/2025";
        cells[10] = "Người bán:ACME Corp";
        var rowText = string.Join('\n', cells);

        Assert.True(GdtRowPathBuilder.TryBuildRelativePath(rowText, out var path));
        Assert.Contains("0123456789", path);
        Assert.Contains("HD-001", path);
    }

    [Fact]
    public void Returns_false_when_too_few_columns()
    {
        Assert.False(GdtRowPathBuilder.TryBuildFromCells(["a", "b", "c"], out _));
    }
}
