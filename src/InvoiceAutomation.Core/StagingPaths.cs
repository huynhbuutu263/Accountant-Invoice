namespace InvoiceAutomation.Core;

public static class StagingPaths
{
    public const string DefaultFolderName = "_staging";

    public static string Folder(string downloadsRoot) =>
        Path.Combine(downloadsRoot, DefaultFolderName);

    public static string ZipPath(string downloadsRoot, string rowIndex) =>
        Path.Combine(Folder(downloadsRoot), $"{rowIndex}.zip");

    public static string RowPathSidecar(string stagingFolder, string rowIndex) =>
        Path.Combine(stagingFolder, $"{rowIndex}.rowpath");

    public static void SaveRowPathSidecar(string stagingFolder, string rowIndex, string rowFilePath)
    {
        Directory.CreateDirectory(stagingFolder);
        File.WriteAllText(RowPathSidecar(stagingFolder, rowIndex), rowFilePath);
    }

    public static string? ReadRowPathSidecar(string stagingFolder, string rowKey)
    {
        var path = RowPathSidecar(stagingFolder, rowKey);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    public static void DeleteRowPathSidecar(string stagingFolder, string rowKey)
    {
        var path = RowPathSidecar(stagingFolder, rowKey);
        if (File.Exists(path))
            File.Delete(path);
    }
}
