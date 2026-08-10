using System.Text;
using WinFEBuilder.Core.Hashing;
using Xunit;

namespace WinFEBuilder.Tests;

public class HashServiceTests
{
    private readonly HashService _hash = new();

    [Fact]
    public void ComputeSha256_Bytes_MatchesKnownVector()
    {
        // SHA-256 of "abc" (NIST test vector).
        var expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        Assert.Equal(expected, _hash.ComputeSha256(Encoding.ASCII.GetBytes("abc")));
    }

    [Fact]
    public void ComputeSha256_EmptyInput_MatchesKnownVector()
    {
        var expected = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        Assert.Equal(expected, _hash.ComputeSha256(Array.Empty<byte>()));
    }

    [Fact]
    public async Task ComputeSha256Async_File_MatchesInMemoryHash()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "winfe_hash_" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var data = Encoding.UTF8.GetBytes("The quick brown fox");
            await File.WriteAllBytesAsync(tmp, data);

            var fileHash = await _hash.ComputeSha256Async(tmp);
            var memHash = _hash.ComputeSha256(data);

            Assert.Equal(memHash, fileHash);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public async Task ComputeEntryAsync_PopulatesMetadata()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winfe_entry_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "sub", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "hello");

        try
        {
            var entry = await _hash.ComputeEntryAsync(file, dir);
            Assert.Equal(Path.Combine("sub", "a.txt"), entry.RelativePath);
            Assert.Equal(5, entry.SizeBytes);
            Assert.False(string.IsNullOrEmpty(entry.Sha256));
            Assert.Equal(64, entry.Sha256.Length); // 32 bytes hex
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
