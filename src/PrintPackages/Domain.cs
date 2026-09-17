using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;

namespace PrintPackages;

public sealed record PrinterIdentity(string QueueName, string DriverName, string DriverVersion, string Model);
public sealed record PrintPackageManifest(
    string FormatVersion, string Id, string Name, DateTimeOffset CreatedAt,
    string DocumentSha256, string DevModeSha256, PrinterIdentity Printer,
    IReadOnlyDictionary<string, string> Metadata);
public sealed record PrintPackage(PrintPackageManifest Manifest, byte[] Pdf, byte[] DevMode, byte[]? Preview);

public static class PackageArchive
{
    public const string Extension = ".printpkg";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Save(string path, PrintPackage package)
    {
        Validate(package);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        Write(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(package.Manifest, JsonOptions));
        Write(zip, "document.pdf", package.Pdf);
        Write(zip, "driver.devmode", package.DevMode);
        if (package.Preview is not null) Write(zip, "preview.png", package.Preview);
    }

    public static PrintPackage Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifest = JsonSerializer.Deserialize<PrintPackageManifest>(Read(zip, "manifest.json"), JsonOptions)
            ?? throw new InvalidDataException("Balíček neobsahuje platný manifest.");
        var package = new PrintPackage(manifest, Read(zip, "document.pdf"), Read(zip, "driver.devmode"), ReadOptional(zip, "preview.png"));
        Validate(package);
        return package;
    }

    public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    public static void Validate(PrintPackage package)
    {
        if (!package.Pdf.AsSpan().StartsWith("%PDF"u8)) throw new InvalidDataException("Dokument není platné PDF.");
        if (package.DevMode.Length == 0) throw new InvalidDataException("Chybí nastavení ovladače.");
        if (!Hash(package.Pdf).Equals(package.Manifest.DocumentSha256, StringComparison.OrdinalIgnoreCase) ||
            !Hash(package.DevMode).Equals(package.Manifest.DevModeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Kontrolní součet balíčku nesouhlasí.");
    }
    private static void Write(ZipArchive zip, string name, byte[] bytes) { using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); s.Write(bytes); }
    private static byte[] Read(ZipArchive zip, string name) { var e = zip.GetEntry(name) ?? throw new InvalidDataException($"Chybí {name}."); using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); }
    private static byte[]? ReadOptional(ZipArchive zip, string name) => zip.GetEntry(name) is null ? null : Read(zip, name);
}

public sealed class LibraryStore(string? root = null)
{
    public string Root { get; } = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintPackages", "Library");
    public IEnumerable<string> Find(string query = "") => Directory.Exists(Root)
        ? Directory.EnumerateFiles(Root, "*" + PackageArchive.Extension, SearchOption.AllDirectories)
            .Where(p => Path.GetFileNameWithoutExtension(p).Contains(query, StringComparison.OrdinalIgnoreCase)) : [];
    public string CreateFolder(string relativePath) { var path = Path.Combine(Root, relativePath); Directory.CreateDirectory(path); return path; }
    public void Delete(string path) => File.Delete(path);
    public void Copy(string source, string destination) => File.Copy(source, destination, false);
    public void Move(string source, string destination) => File.Move(source, destination);
}
