using System.Numerics;

namespace GAIA.Util;

/// <summary>Queues viewer-region captures after ImGui has rendered all overlays.</summary>
public static class ViewerScreenshotUtility
{
    private readonly record struct Request(Vector2 Position, Vector2 Size, string Path,
        ScreenshotUtility.ImageFormat Format, int Quality, Action<bool,string> Callback, int FramesToWait);
    private static readonly Queue<Request> Pending = new();
    private static readonly object Gate = new();

    public static void ScheduleRegionCapture(Vector2 viewerScreenPos, Vector2 viewerSize, string filePath,
        ScreenshotUtility.ImageFormat format = ScreenshotUtility.ImageFormat.PNG,
        Action<bool,string> callback = null)
    {
        lock (Gate) Pending.Enqueue(new Request(viewerScreenPos, viewerSize, filePath, format, 90, callback, 1));
    }

    public static bool CaptureMainWindowRegion(Vector2 position, Vector2 size, string path,
        ScreenshotUtility.ImageFormat format = ScreenshotUtility.ImageFormat.PNG, int jpegQuality = 90) =>
        ScreenshotUtility.Schedule((int)position.X, (int)position.Y, (int)size.X, (int)size.Y,
            path, format, jpegQuality, null);

    public static void ProcessDeferredCaptures()
    {
        while (true)
        {
            Request request;
            lock (Gate) { if (Pending.Count == 0) break; request = Pending.Dequeue(); }
            if (request.FramesToWait > 0)
            {
                lock (Gate) Pending.Enqueue(request with { FramesToWait = request.FramesToWait - 1 });
                break;
            }
            ScreenshotUtility.Schedule((int)request.Position.X, (int)request.Position.Y,
                (int)request.Size.X, (int)request.Size.Y, request.Path, request.Format, request.Quality,
                success => request.Callback?.Invoke(success, request.Path));
        }
    }

    public static void Dispose() { lock (Gate) Pending.Clear(); }
}
