using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoExplorer.Core;

namespace PhotoExplorer.Desktop;

public sealed class WpfThumbnailGenerator : IThumbnailGenerator
{
    public Task<Thumbnail> GenerateAsync(PhotoFile file, int size, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var image = Decode(file.FullPath, size);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new Thumbnail(stream.ToArray(), image.PixelWidth, image.PixelHeight);
    }, cancellationToken);

    public static BitmapSource Decode(string path, int maxSize = 2560) => DecodeWithInfo(path, maxSize).Image;

    public static (BitmapSource Image, int Width, int Height) DecodeWithInfo(string path, int maxSize = 2560)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // Read metadata without retaining the original stream or decoding all pixels.
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        var width = frame.PixelWidth;
        var height = frame.PixelHeight;
        ushort orientation = 1;
        try
        {
            if (frame.Metadata is BitmapMetadata metadata)
            {
                var value = metadata.GetQuery("/app1/ifd/{ushort=274}") ?? metadata.GetQuery("/ifd/{ushort=274}");
                if (value is not null) orientation = Convert.ToUInt16(value);
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        stream.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        if (Math.Max(width, height) > maxSize)
        {
            if (width >= height) bitmap.DecodePixelWidth = maxSize; else bitmap.DecodePixelHeight = maxSize;
        }
        bitmap.EndInit();
        bitmap.Freeze();
        var matrix = orientation switch
        {
            2 => new Matrix(-1, 0, 0, 1, 0, 0),
            3 => new Matrix(-1, 0, 0, -1, 0, 0),
            4 => new Matrix(1, 0, 0, -1, 0, 0),
            5 => new Matrix(0, 1, 1, 0, 0, 0),
            6 => new Matrix(0, 1, -1, 0, 0, 0),
            7 => new Matrix(0, -1, -1, 0, 0, 0),
            8 => new Matrix(0, -1, 1, 0, 0, 0),
            _ => Matrix.Identity
        };
        if (matrix.IsIdentity) return (bitmap, width, height);
        var transformed = new TransformedBitmap(bitmap, new MatrixTransform(matrix));
        transformed.Freeze();
        return (transformed, width, height);
    }

    public static BitmapSource FromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }

    public static (int Width, int Height) Dimensions(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }
}
