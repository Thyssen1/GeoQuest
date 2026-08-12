using SkiaSharp;
using Svg.Skia;

namespace GeoQuest.Tools.FlagConverter;

/// <summary>
/// Dev-time tool. Rasterises the SVG flag sources in Assets/Flags to PNGs so the
/// game can load them with Avalonia's built-in Bitmap and take no SVG dependency
/// at runtime. Re-run after adding or updating flag sources.
///
///     dotnet run --project tools/FlagConverter -- --height 320
/// </summary>
internal static class Program
{
    private const int DefaultHeight = 320;

    /// <summary>
    /// Width-to-height ratio of the output canvas. Every flag is centred on a canvas of
    /// this shape so all PNGs come out identically sized and the game grid stays uniform.
    /// 1.5 (3:2) is the most common national flag ratio, so it wastes the least space.
    /// </summary>
    private const double DefaultCanvasRatio = 1.5d;

    private static int Main(string[] args)
    {
        var height = DefaultHeight;
        string? inputDir = null;
        var force = false;
        var fill = false;
        double? canvasRatio = DefaultCanvasRatio;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--height" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out height) || height <= 0)
                    {
                        Console.Error.WriteLine("--height must be a positive integer.");
                        return 1;
                    }
                    break;
                case "--ratio" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                    {
                        Console.Error.WriteLine("--ratio must be a positive number, e.g. 1.5 for 3:2.");
                        return 1;
                    }
                    canvasRatio = parsed;
                    break;
                case "--fill":
                    // Stretch each flag to fill the canvas edge to edge. Every PNG comes
                    // out identical in size with no padding, at the cost of distorting
                    // flags whose true ratio is not the canvas ratio.
                    fill = true;
                    break;
                case "--native":
                    // Opt out of the shared canvas: each PNG comes out at its own true
                    // proportions, which leaves the grid ragged. Kept for flexibility.
                    canvasRatio = null;
                    break;
                case "--input" when i + 1 < args.Length:
                    inputDir = args[++i];
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unrecognised argument: {args[i]}");
                    return 1;
            }
        }

        inputDir ??= LocateFlagsDirectory();
        if (inputDir is null)
        {
            Console.Error.WriteLine("Could not locate Assets/Flags. Pass --input explicitly.");
            return 1;
        }

        var sources = Directory.GetFiles(inputDir, "*.svg");
        if (sources.Length == 0)
        {
            Console.Error.WriteLine($"No .svg files found in {inputDir}");
            return 1;
        }

        if (fill && canvasRatio is null)
        {
            Console.Error.WriteLine("--fill and --native are mutually exclusive.");
            return 1;
        }

        var describe = canvasRatio is double r
            ? $"{(fill ? "stretched to fill" : "centred on")} a shared {(int)Math.Round(height * r)}x{height} canvas"
            : "at native proportions";

        Console.WriteLine($"Rasterising {sources.Length} flags {describe} -> {inputDir}");

        var converted = 0;
        var skipped = 0;
        var failed = new List<string>();

        foreach (var source in sources.OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(source);
            var target = Path.Combine(inputDir, name + ".png");

            if (!force && File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source))
            {
                skipped++;
                continue;
            }

            try
            {
                if (Rasterise(source, target, height, canvasRatio, fill))
                {
                    converted++;
                }
                else
                {
                    failed.Add(name);
                }
            }
            catch (Exception ex)
            {
                failed.Add($"{name} ({ex.Message})");
            }
        }

        Console.WriteLine($"Converted {converted}, up to date {skipped}, failed {failed.Count}");

        if (failed.Count > 0)
        {
            Console.Error.WriteLine("Failed: " + string.Join(", ", failed));
            return 1;
        }

        return 0;
    }

    private static bool Rasterise(string svgPath, string pngPath, int targetHeight, double? canvasRatio, bool fill)
    {
        using var svg = new SKSvg();

        var picture = svg.Load(svgPath);
        if (picture is null)
        {
            return false;
        }

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        int canvasWidth;
        float scaleX;
        float scaleY;

        if (canvasRatio is double ratio)
        {
            canvasWidth = Math.Max(1, (int)Math.Round(targetHeight * ratio));

            if (fill)
            {
                // Stretch each axis independently so the flag covers the canvas exactly.
                scaleX = canvasWidth / bounds.Width;
                scaleY = targetHeight / bounds.Height;
            }
            else
            {
                // Fit whole, preserving proportions; leftover space stays transparent.
                scaleX = scaleY = Math.Min(canvasWidth / bounds.Width, targetHeight / bounds.Height);
            }
        }
        else
        {
            // Native: height is pinned and width follows from the source viewBox.
            scaleX = scaleY = targetHeight / bounds.Height;
            canvasWidth = Math.Max(1, (int)Math.Round(bounds.Width * scaleX));
        }

        var info = new SKImageInfo(canvasWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Centre the drawn flag within the canvas. A no-op when filling, since the
        // scaled flag then covers the canvas exactly.
        var offsetX = (canvasWidth - bounds.Width * scaleX) / 2f;
        var offsetY = (targetHeight - bounds.Height * scaleY) / 2f;

        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scaleX, scaleY);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(pngPath);
        data.SaveTo(stream);

        return true;
    }

    /// <summary>Walks up from the executable to find the repo's Assets/Flags directory.</summary>
    private static string? LocateFlagsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Assets", "Flags");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
