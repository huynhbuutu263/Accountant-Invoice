using System.IO.Compression;
using InvoiceAutomation.Core;
using Microsoft.Extensions.Logging;

namespace InvoiceAutomation.Services;

public sealed class FileProcessor : IFileProcessor
{
    private readonly ILogger<FileProcessor> _logger;

    public FileProcessor(ILogger<FileProcessor> logger) => _logger = logger;

    public Task<IReadOnlyList<string>> ExtractZipAsync(string zipPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(zipPath))
            throw new FileNotFoundException(zipPath);

        var destDir = Path.Combine(Path.GetDirectoryName(zipPath) ?? ".", Path.GetFileNameWithoutExtension(zipPath) + "_extracted");
        Directory.CreateDirectory(destDir);

        var extracted = new List<string>();
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(name) || name.EndsWith('/'))
                    continue;
                if (name.Contains("..", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Skipping zip entry with '..': {Name}", name);
                    continue;
                }

                var targetPath = Path.GetFullPath(Path.Combine(destDir, name));
                if (!targetPath.StartsWith(Path.GetFullPath(destDir), StringComparison.OrdinalIgnoreCase))
                    continue;

                var parent = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                entry.ExtractToFile(targetPath, overwrite: true);
                extracted.Add(targetPath);
                _logger.LogInformation("Extracted {Path} ({Size} bytes)", targetPath, entry.Length);
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(extracted);
    }

    public static bool LooksLikePdf(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[5];
            return fs.Read(buf, 0, 5) == 5 && buf[0] == (byte)'%' && buf[1] == (byte)'P' && buf[2] == (byte)'D' && buf[3] == (byte)'F';
        }
        catch
        {
            return false;
        }
    }

    public static bool LooksLikeXml(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var line = reader.ReadLine() ?? "";
            return line.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
