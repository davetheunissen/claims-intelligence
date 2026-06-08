namespace ClaimsIntelligence.Domain.Pipeline;

public static class MimeTypes
{
    public const string PlainText = "text/plain";
    public const string MarkDown = "text/markdown";
    public const string Html = "text/html";
    public const string Xml = "application/xml";
    public const string Json = "application/json";
    public const string Csv = "text/csv";
    public const string Pdf = "application/pdf";
    public const string MsWord = "application/msword";
    public const string MsWordX = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string MsPowerPoint = "application/vnd.ms-powerpoint";
    public const string MsPowerPointX = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    public const string MsExcel = "application/vnd.ms-excel";
    public const string MsExcelX = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string ImageBmp = "image/bmp";
    public const string ImageGif = "image/gif";
    public const string ImageJpeg = "image/jpeg";
    public const string ImagePng = "image/png";
    public const string ImageTiff = "image/tiff";
    public const string ImageWebP = "image/webp";
    public const string ImageSvg = "image/svg+xml";
    public const string AudioAac = "audio/aac";
    public const string AudioMp3 = "audio/mpeg";
    public const string AudioWav = "audio/wav";
    public const string VideoMp4 = "video/mp4";
    public const string VideoMpeg = "video/mpeg";
    public const string ArchiveZip = "application/zip";
    public const string ArchiveGzip = "application/gzip";
    public const string ArchiveTar = "application/x-tar";
}

public static class MimeTypeDetection
{
    private static readonly Dictionary<string, string> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"]  = MimeTypes.PlainText,
        [".md"]   = MimeTypes.MarkDown,
        [".htm"]  = MimeTypes.Html,
        [".html"] = MimeTypes.Html,
        [".xml"]  = MimeTypes.Xml,
        [".json"] = MimeTypes.Json,
        [".csv"]  = MimeTypes.Csv,
        [".pdf"]  = MimeTypes.Pdf,
        [".doc"]  = MimeTypes.MsWord,
        [".docx"] = MimeTypes.MsWordX,
        [".ppt"]  = MimeTypes.MsPowerPoint,
        [".pptx"] = MimeTypes.MsPowerPointX,
        [".xls"]  = MimeTypes.MsExcel,
        [".xlsx"] = MimeTypes.MsExcelX,
        [".bmp"]  = MimeTypes.ImageBmp,
        [".gif"]  = MimeTypes.ImageGif,
        [".jpeg"] = MimeTypes.ImageJpeg,
        [".jpg"]  = MimeTypes.ImageJpeg,
        [".png"]  = MimeTypes.ImagePng,
        [".tiff"] = MimeTypes.ImageTiff,
        [".tif"]  = MimeTypes.ImageTiff,
        [".webp"] = MimeTypes.ImageWebP,
        [".svg"]  = MimeTypes.ImageSvg,
        [".aac"]  = MimeTypes.AudioAac,
        [".mp3"]  = MimeTypes.AudioMp3,
        [".wav"]  = MimeTypes.AudioWav,
        [".mp4"]  = MimeTypes.VideoMp4,
        [".mpeg"] = MimeTypes.VideoMpeg,
        [".zip"]  = MimeTypes.ArchiveZip,
        [".gz"]   = MimeTypes.ArchiveGzip,
        [".tar"]  = MimeTypes.ArchiveTar,
    };

    public static string? TryGetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ExtensionMap.GetValueOrDefault(ext);
    }

    public static string GetMimeType(string fileName)
        => TryGetMimeType(fileName)
           ?? throw new NotSupportedException($"File type not supported: {fileName}");
}
