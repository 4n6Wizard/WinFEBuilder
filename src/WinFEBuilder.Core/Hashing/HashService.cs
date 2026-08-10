using System.Security.Cryptography;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.Core.Hashing;

/// <summary>SHA-256 hashing using .NET cryptography APIs, with streaming for large files.</summary>
public sealed class HashService : IHashService
{
    public async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        PathValidator.EnsureExistingFile(filePath);

        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, useAsync: true);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<FileHashEntry> ComputeEntryAsync(string filePath, string baseDirectory, CancellationToken ct = default)
    {
        PathValidator.EnsureExistingFile(filePath);
        var info = new FileInfo(filePath);
        var hash = await ComputeSha256Async(filePath, ct).ConfigureAwait(false);

        var rel = PathValidator.GetRelativePath(baseDirectory, filePath);
        return new FileHashEntry
        {
            RelativePath = rel,
            FullPath = info.FullName,
            SizeBytes = info.Length,
            Sha256 = hash,
            LastWriteUtc = info.LastWriteTimeUtc
        };
    }

    public string ComputeSha256(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
