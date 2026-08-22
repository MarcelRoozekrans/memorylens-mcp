using System.IO.Compression;

namespace MemoryLens.Mcp.TestSupport;

/// <summary>
/// Builds a stand-in for the dotMemory .nupkg: a zip whose entries cover every
/// branch of the byte-sniffing classifier in DotMemoryAutoInstaller.
/// </summary>
public static class PackageFixtureBuilder
{
    /// <summary>Shebang script — must become executable.</summary>
    public const string ShebangEntry = "tools/dotMemory.sh";

    /// <summary>Second-hop shebang script — the file #118 actually died on.</summary>
    public const string NestedShebangEntry = "tools/linux-x64/runtime-dotnet.sh";

    /// <summary>ELF magic, dotted name so no extension rule could catch it.</summary>
    public const string ElfEntry = "tools/linux-x64/JetBrains.Profiler.PdbServer";

    /// <summary>Mach-O magic.</summary>
    public const string MachOEntry = "tools/macos-x64/JetBrains.Profiler.Native";

    /// <summary>Managed assembly ("MZ") — must NOT become executable.</summary>
    public const string ManagedEntry = "tools/JetBrains.dotMemory.Console.dll";

    /// <summary>Plain text — must NOT become executable.</summary>
    public const string TextEntry = "tools/README.txt";

    private static readonly byte[] Elf = [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00];
    private static readonly byte[] MachO = [0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01];
    private static readonly byte[] Managed = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];

    /// <summary>Writes the fixture zip to <paramref name="zipPath"/>.</summary>
    public static void WriteSampleZip(string zipPath)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        WriteText(zip, ShebangEntry, "#!/bin/sh\necho dotmemory\n");
        WriteText(zip, NestedShebangEntry, "#!/bin/sh\necho runtime\n");
        WriteBytes(zip, ElfEntry, Elf);
        WriteBytes(zip, MachOEntry, MachO);
        WriteBytes(zip, ManagedEntry, Managed);
        WriteText(zip, TextEntry, "not executable\n");
    }

    /// <summary>
    /// Writes the fixture zip and extracts it into <paramref name="targetDir"/> the same
    /// way the installer does — through ZipFile.ExtractToDirectory, which is what discards
    /// the permission bits in the first place.
    /// </summary>
    public static void ExtractSampleTo(string targetDir)
    {
        var zipPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            WriteSampleZip(zipPath);
            Directory.CreateDirectory(targetDir);
            ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }

    private static void WriteText(ZipArchive zip, string entryName, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
        writer.Write(content);
    }

    private static void WriteBytes(ZipArchive zip, string entryName, byte[] content)
    {
        using var stream = zip.CreateEntry(entryName).Open();
        stream.Write(content, 0, content.Length);
    }
}
