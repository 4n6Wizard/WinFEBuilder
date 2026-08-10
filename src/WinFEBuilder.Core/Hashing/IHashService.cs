using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Hashing;

public interface IHashService
{
    /// <summary>Compute the SHA-256 of a file as a lowercase hex string.</summary>
    Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default);

    /// <summary>Compute a SHA-256 hash entry (path/size/hash/timestamp) for a file.</summary>
    Task<FileHashEntry> ComputeEntryAsync(string filePath, string baseDirectory, CancellationToken ct = default);

    /// <summary>Compute the SHA-256 of an arbitrary byte buffer (used by tests).</summary>
    string ComputeSha256(byte[] data);
}
