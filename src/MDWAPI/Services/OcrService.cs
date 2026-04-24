using System.Text.RegularExpressions;
using Tesseract;

namespace MDWAPI.Services;

/// <summary>
/// OCR service using Tesseract — thread-safe via per-call engine creation.
/// tessdata files must exist at the configured path before use.
/// </summary>
public class OcrService
{
    private readonly string _tessdataPath;
    private readonly string _languages;
    private readonly ILogger<OcrService> _log;

    public OcrService(IConfiguration cfg, ILogger<OcrService> log)
    {
        _tessdataPath = cfg["Tesseract:TessdataPath"] ?? "tessdata";
        _languages = cfg["Tesseract:Languages"] ?? "chi_tra+eng";
        _log = log;
    }

    /// <summary>
    /// Run OCR on a data URL (data:image/png;base64,...).
    /// Returns null if tessdata not found or OCR fails.
    /// </summary>
    public string? OcrDataUrl(string dataUrl, string? languageOverride = null)
    {
        try
        {
            var base64 = dataUrl.Contains(',')
                ? dataUrl[(dataUrl.IndexOf(',') + 1)..]
                : dataUrl;

            var bytes = Convert.FromBase64String(base64);
            return OcrBytes(bytes, languageOverride);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "OcrDataUrl failed");
            return null;
        }
    }

    public string? OcrBytes(byte[] bytes, string? languageOverride = null)
    {
        var lang = languageOverride ?? _languages;

        try
        {
            using var engine = new TesseractEngine(_tessdataPath, lang, EngineMode.Default);
            engine.SetVariable("preserve_interword_spaces", "1");

            using var pix = Pix.LoadFromMemory(bytes);
            using var page = engine.Process(pix);

            var raw = page.GetText() ?? "";
            return Normalize(raw);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tesseract OCR failed (lang={Lang}, tessdataPath={Path})", lang, _tessdataPath);
            return null;
        }
    }

    public bool IsTessdataReady()
    {
        if (!Directory.Exists(_tessdataPath)) return false;
        // ต้องมีอย่างน้อย 1 .traineddata file
        return Directory.GetFiles(_tessdataPath, "*.traineddata").Length > 0;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = text.Replace("\r", " ").Replace("\n", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        text = Regex.Replace(text, @"\s+([,.:;])", "$1");
        return text;
    }
}
