using System;
using System.IO;
using SkiaSharp;

namespace BoomBx.Services
{
    public sealed record ThumbnailResult(string Path, int Width, int Height, long Bytes, bool Resized);

    /// <summary>
    /// Produces the small, compressed copies used as sound thumbnails.
    ///
    /// Images are scaled down to fit a 256x256 box (aspect ratio preserved, never upscaled) and
    /// re-encoded: JPEG 85 for opaque images (photos) and PNG for images with transparency.
    /// That keeps the soundboard UI responsive even when the user imports multi-megabyte pictures.
    /// </summary>
    public static class ThumbnailService
    {
        public const int MaxDimension = 256;
        public const int JpegQuality = 85;

        public static ThumbnailResult CreateThumbnail(string sourcePath, string destinationDirectory, string baseName)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The selected image could not be found.", sourcePath);
            }

            using var input = SKBitmap.Decode(sourcePath)
                ?? throw new InvalidOperationException("The selected file is not a supported image (PNG or JPEG).");

            int width = input.Width;
            int height = input.Height;

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("The selected image has invalid dimensions.");
            }

            double scale = Math.Min(1.0, (double)MaxDimension / Math.Max(width, height));
            int targetWidth = Math.Max(1, (int)Math.Round(width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(height * scale));

            using var resized = input.Resize(new SKImageInfo(targetWidth, targetHeight), SKFilterQuality.High)
                ?? throw new InvalidOperationException("The image could not be resized.");

            bool keepAlpha = input.AlphaType != SKAlphaType.Opaque;
            var format = keepAlpha ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
            var extension = keepAlpha ? ".png" : ".jpg";

            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(format, keepAlpha ? 100 : JpegQuality);

            if (encoded is null || encoded.Size == 0)
            {
                throw new InvalidOperationException("The image could not be encoded.");
            }

            Directory.CreateDirectory(destinationDirectory);
            var destination = Path.Combine(destinationDirectory, baseName + extension);

            using (var stream = File.Create(destination))
            {
                encoded.SaveTo(stream);
            }

            return new ThumbnailResult(
                destination,
                targetWidth,
                targetHeight,
                new FileInfo(destination).Length,
                scale < 1.0 || encoded.Size < new FileInfo(sourcePath).Length);
        }
    }
}
