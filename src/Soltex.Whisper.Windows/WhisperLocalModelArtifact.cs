namespace Soltex.Whisper.Windows;

internal sealed record WhisperLocalModelArtifact(
    string ProviderId,
    string ModelId,
    string RuntimeId,
    string FileName,
    Uri DownloadUri,
    string UpstreamRevision,
    long ExpectedBytes,
    string ExpectedSha256)
{
    internal static WhisperLocalModelArtifact TurboQ5Cpu { get; } = new(
        WhisperLocalModelDefaults.ProviderId,
        WhisperLocalModelDefaults.ModelId,
        WhisperLocalModelDefaults.RuntimeId,
        "ggml-large-v3-turbo-q5_0.bin",
        new Uri(
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/" +
            "98aa99a0a9db05ae2342309f5096248665f7cba3/" +
            "ggml-large-v3-turbo-q5_0.bin"),
        "98aa99a0a9db05ae2342309f5096248665f7cba3",
        574_041_195,
        "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2");

    internal void Validate()
    {
        if (!string.Equals(ProviderId, WhisperLocalModelDefaults.ProviderId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(ModelId) ||
            string.IsNullOrWhiteSpace(RuntimeId) ||
            string.IsNullOrWhiteSpace(FileName) ||
            Path.GetFileName(FileName) != FileName ||
            !DownloadUri.IsAbsoluteUri ||
            !string.Equals(DownloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(UpstreamRevision) ||
            ExpectedBytes < 1 ||
            ExpectedSha256.Length != 64 ||
            !ExpectedSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The local model artifact descriptor is invalid.");
        }
    }
}
