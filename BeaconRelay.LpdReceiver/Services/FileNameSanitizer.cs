namespace BeaconRelay.LpdReceiver.Services;

public static class FileNameSanitizer
{
    public static string Sanitize(string? input, string fallback = "lpd_data")
    {
        var fileName = string.IsNullOrWhiteSpace(input) ? fallback : Path.GetFileName(input);
        fileName = fileName.Replace('/', '_').Replace('\\', '_');

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        fileName = fileName.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = fallback;
        }

        return fileName.Length > 120 ? fileName[..120] : fileName;
    }
}
