using System;
using System.IO;
using SkiaSharp;

namespace BliBloup.Services
{
    public sealed record ThumbnailResult(string Path, int Width, int Height, long Bytes, bool Resized);

    /// <summary>
    /// Produces the square, compressed copies used as sound thumbnails.
    ///
    /// Every image is center-cropped to its smallest side (so the result contains the middle of
    /// the picture without distortion) and resized to exactly 256x256 through SKCanvas.DrawBitmap.
    /// The copy is then encoded: JPEG 85 for opaque images (photos) and PNG for images that use
    /// transparency, preserving the alpha channel. That keeps the soundboard UI responsive even
    /// when the user imports multi-megabyte pictures.
    /// </summary>
    public static class ThumbnailService
    {
        /// <summary>Thumbnails are strictly square: this is both width and height.</summary>
        public const int Size = 256;

        public const int JpegQuality = 85;

        public static ThumbnailResult CreateThumbnail(string sourcePath, string destinationDirectory, string baseName)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The selected image could not be found.", sourcePath);
            }

            using var input = SKBitmap.Decode(sourcePath)
                ?? throw new InvalidOperationException("The selected file is not a supported image (PNG or JPEG).");

            if (input.Width <= 0 || input.Height <= 0)
            {
                throw new InvalidOperationException("The selected image has invalid dimensions.");
            }

            bool keepAlpha = input.AlphaType != SKAlphaType.Opaque;

            // Center crop: take the largest centered square of the source.
            int side = Math.Min(input.Width, input.Height);
            int left = (input.Width - side) / 2;
            int top = (input.Height - side) / 2;

            // BGRA keeps a real alpha channel for PNG and is also accepted by the JPEG encoder.
            var alphaType = keepAlpha ? SKAlphaType.Premul : SKAlphaType.Opaque;
            var imageInfo = new SKImageInfo(Size, Size, SKColorType.Bgra8888, alphaType);

            using var surface = SKSurface.Create(imageInfo)
                ?? throw new InvalidOperationException("The thumbnail surface could not be created.");

            var canvas = surface.Canvas;

            if (keepAlpha)
            {
                // Transparent padding/fill so the destination keeps a real alpha channel.
                canvas.Clear(SKColors.Transparent);
            }

            using (var paint = new SKPaint
                   {
                       FilterQuality = SKFilterQuality.High,
                       IsAntialias = true
                   })
            {
                var sourceRect = new SKRect(left, top, left + side, top + side);
                var destinationRect = new SKRect(0, 0, Size, Size);
                canvas.DrawBitmap(input, sourceRect, destinationRect, paint);
            }

            canvas.Flush();

            using var cropped = surface.Snapshot();
            var format = keepAlpha ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
            var extension = keepAlpha ? ".png" : ".jpg";

            using var encoded = cropped.Encode(format, keepAlpha ? 100 : JpegQuality);

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
                Size,
                Size,
                new FileInfo(destination).Length,
                input.Width != Size || input.Height != Size);
        }
    }
}
