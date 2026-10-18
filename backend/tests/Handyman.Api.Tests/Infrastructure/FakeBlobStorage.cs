using System.Collections.Concurrent;
using Handyman.Api.Infrastructure;

namespace Handyman.Api.Tests.Infrastructure;

public class FakeBlobStorage : IBlobStorage
{
    public ConcurrentDictionary<string, byte[]> Objects { get; } = new();

    public async Task<string> UploadAsync(string bucket, string path, Stream content, string contentType, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        Objects[$"{bucket}/{path}"] = buffer.ToArray();
        return path;
    }

    public Task<string> GetSignedUrlAsync(string bucket, string path, TimeSpan lifetime, CancellationToken ct = default)
        => Task.FromResult($"http://fake-storage/{bucket}/{path}");

    public Task DeleteAsync(string bucket, string path, CancellationToken ct = default)
    {
        Objects.TryRemove($"{bucket}/{path}", out _);
        return Task.CompletedTask;
    }
}
