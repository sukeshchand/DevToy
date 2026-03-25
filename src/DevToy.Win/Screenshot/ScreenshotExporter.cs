using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace DevToy;

static class ScreenshotExporter
{
    /// <summary>Flatten original image + all annotations into a final bitmap.</summary>
    public static Bitmap Flatten(EditorSession session)
    {
        var original = session.OriginalImage;
        var result = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);

        g.DrawImage(original, 0, 0);

        // Render annotations in z-order (list order = z-order)
        foreach (var obj in session.Annotations)
        {
            obj.IsSelected = false; // don't render handles in export
            obj.Render(g);
        }

        return result;
    }

    /// <summary>Save flattened image to a file.</summary>
    public static string SaveToFile(EditorSession session, string? filePath = null)
    {
        filePath ??= GenerateFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        using var flattened = Flatten(session);
        var format = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Png,
        };
        flattened.Save(filePath, format);
        return filePath;
    }

    /// <summary>Copy flattened image to clipboard.</summary>
    public static void CopyToClipboard(EditorSession session)
    {
        try
        {
            using var flattened = Flatten(session);
            Clipboard.SetImage(flattened);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clipboard copy failed: {ex.Message}");
        }
    }

    public static string GenerateFilePath()
    {
        string dir = AppPaths.ScreenshotsDir;
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        return Path.Combine(dir, $"screenshot_{timestamp}.png");
    }
}
