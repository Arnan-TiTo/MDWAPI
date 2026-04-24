using SkiaSharp;
using ZXing;
using ZXing.SkiaSharp;

namespace MDWAPI.Helpers;

public static class BarcodeHelper
{
    private static readonly BarcodeReader _reader = new()
    {
        AutoRotate = true,
        Options = new ZXing.Common.DecodingOptions
        {
            TryHarder = true,
            TryInverted = true,
            PossibleFormats = new List<BarcodeFormat>
            {
                BarcodeFormat.QR_CODE,
                BarcodeFormat.CODE_128,
                BarcodeFormat.CODE_39,
                BarcodeFormat.DATA_MATRIX,
                BarcodeFormat.PDF_417,
                BarcodeFormat.EAN_13,
                BarcodeFormat.EAN_8,
                BarcodeFormat.ITF,
            }
        }
    };

    /// <summary>
    /// Decode a barcode from a data URL string (data:image/png;base64,...) or plain base64.
    /// Returns null if decoding fails.
    /// </summary>
    public static string? DecodeDataUrl(string dataUrl)
    {
        try
        {
            var base64 = dataUrl.Contains(',')
                ? dataUrl[(dataUrl.IndexOf(',') + 1)..]
                : dataUrl;

            var bytes = Convert.FromBase64String(base64);
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap == null) return null;

            var result = _reader.Decode(bitmap);
            return result?.Text;
        }
        catch
        {
            return null;
        }
    }
}
