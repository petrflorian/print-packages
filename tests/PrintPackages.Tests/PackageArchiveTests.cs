namespace PrintPackages.Tests;

public sealed class PackageArchiveTests
{
    [Fact]
    public void SaveLoad_preserves_files_and_checksums()
    {
        var pdf = "%PDF-1.7\nminimal"u8.ToArray(); var devMode = new byte[] { 1, 2, 3 };
        var manifest = new PrintPackageManifest("1", "id", "Test", DateTimeOffset.UtcNow, PackageArchive.Hash(pdf), PackageArchive.Hash(devMode), new PrinterIdentity("OKI C844", "OKI PCL", "1.0", "C844"), new Dictionary<string, string>());
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + PackageArchive.Extension);
        try { PackageArchive.Save(path, new PrintPackage(manifest, pdf, devMode, null)); var loaded = PackageArchive.Load(path); Assert.Equal(pdf, loaded.Pdf); Assert.Equal(devMode, loaded.DevMode); }
        finally { File.Delete(path); }
    }
    [Fact]
    public void Load_rejects_tampered_document()
    {
        var pdf = "%PDF-1.7\nx"u8.ToArray(); var devMode = new byte[] { 1 };
        var manifest = new PrintPackageManifest("1", "id", "Test", DateTimeOffset.UtcNow, "BAD", PackageArchive.Hash(devMode), new PrinterIdentity("q", "d", "v", "m"), new Dictionary<string, string>());
        Assert.Throws<InvalidDataException>(() => PackageArchive.Validate(new PrintPackage(manifest, pdf, devMode, null)));
    }
}
