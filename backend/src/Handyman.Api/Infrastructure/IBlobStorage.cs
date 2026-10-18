namespace Handyman.Api.Infrastructure;

public interface IBlobStorage
{
    Task<string> UploadAsync(string bucket, string path, Stream content, string contentType, CancellationToken ct = default);
    Task<string> GetSignedUrlAsync(string bucket, string path, TimeSpan lifetime, CancellationToken ct = default);
    Task DeleteAsync(string bucket, string path, CancellationToken ct = default);
}
