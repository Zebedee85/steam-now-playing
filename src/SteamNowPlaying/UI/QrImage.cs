using QRCoder;

namespace SteamNowPlaying.UI;

static class QrImage
{
    /// <summary>Renders text as a QR code bitmap (black on white, with a quiet zone).</summary>
    public static Bitmap Create(string text, int pixelsPerModule)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(pixelsPerModule);

        using var stream = new MemoryStream(bytes);
        using var decoded = Image.FromStream(stream);
        return new Bitmap(decoded); // copy, so the stream can be released
    }
}
