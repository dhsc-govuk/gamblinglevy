using System.IO.Compression;

namespace Application.Common.Csv;

// Bundles pre-built file byte content into a ZIP archive (BCL only, no package reference). Entry bytes are written raw
// so CSV content stays byte-identical to CsvWriter.Build output (UTF-8, no BOM, CRLF) — routing through a StreamWriter
// here would risk emitting a BOM and re-encoding.
public static class ZipWriter
{
    public static byte[] Build(IReadOnlyList<(string Name, byte[] Content)> entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return memory.ToArray();
    }
}