using QRCoder;

namespace SecureGateway.Utils
{
    /// <summary>QR code rendering for share links (scanned by the mobile clients).</summary>
    public static class QrCode
    {
        /// <summary>PNG bytes of a QR code for <paramref name="text"/>; dark modules on white.</summary>
        public static byte[] Png(string text, int pixelsPerModule = 8)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text ?? "", QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(data);
            return png.GetGraphic(pixelsPerModule, new byte[] { 0x1E, 0x1E, 0x2E }, new byte[] { 0xFF, 0xFF, 0xFF });
        }
    }
}
