namespace ImmichMCP.Client;

/// <summary>
/// Thrown when an asset exceeds the configured inline download size cap
/// (MAX_INLINE_DOWNLOAD_BYTES) while DOWNLOAD_MODE=base64. The caller should
/// surface a clear error pointing at the download URL instead of buffering
/// arbitrarily large files into memory.
/// </summary>
public sealed class InlineDownloadTooLargeException : Exception
{
    /// <summary>Reported Content-Length, or null when the response was chunked.</summary>
    public long? ContentLength { get; }

    /// <summary>The configured inline size cap in bytes.</summary>
    public long MaxInlineDownloadBytes { get; }

    public InlineDownloadTooLargeException(string path, long? contentLength, long maxInlineDownloadBytes)
        : base(contentLength is long len
            ? $"Asset at {path} is {len} bytes, which exceeds the inline download limit of {maxInlineDownloadBytes} bytes (MAX_INLINE_DOWNLOAD_BYTES). Fetch it via the download URL instead, or raise the limit."
            : $"Asset at {path} exceeds the inline download limit of {maxInlineDownloadBytes} bytes (MAX_INLINE_DOWNLOAD_BYTES). Fetch it via the download URL instead, or raise the limit.")
    {
        ContentLength = contentLength;
        MaxInlineDownloadBytes = maxInlineDownloadBytes;
    }
}
