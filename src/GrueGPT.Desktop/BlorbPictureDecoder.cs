using Avalonia.Media.Imaging;
using GrueGPT.Core.Resources;

namespace GrueGPT.Desktop;

/// <summary>Decodes validated PNG and JPEG Blorb pictures with Avalonia's cross-platform image backend.</summary>
public static class BlorbPictureDecoder
{
    /// <summary>Decodes a raster picture for desktop rendering; Rect resources have dimensions but no pixels.</summary>
    public static Bitmap? Decode(BlorbPicture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        if (picture.Format == BlorbPictureFormat.Rectangle)
        {
            return null;
        }

        using MemoryStream stream = new(picture.EncodedData.ToArray(), writable: false);
        Bitmap bitmap = new(stream);
        if (bitmap.PixelSize.Width != picture.Width || bitmap.PixelSize.Height != picture.Height)
        {
            bitmap.Dispose();
            throw new InvalidDataException("The decoded picture dimensions do not match the Blorb resource header.");
        }

        return bitmap;
    }
}
