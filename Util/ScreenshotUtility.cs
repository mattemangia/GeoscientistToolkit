using System.Numerics;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using StbImageWriteSharp;

namespace GAIA.Util;

/// <summary>Deferred OpenGL framebuffer and texture capture used by the shell and every viewer.</summary>
public static class ScreenshotUtility
{
    public enum ImageFormat { PNG, JPEG, BMP, TGA }
    private readonly record struct Request(int X, int Y, int Width, int Height, string Path,
        ImageFormat Format, int JpegQuality, Action<bool> Callback);
    private static readonly Queue<Request> Pending = new();
    private static readonly object Gate = new();

    public static void BeginFrame() { }
    public static void EndFrame(object unused = null) { }

    public static bool CaptureImGuiWindow(string windowName, string filePath,
        ImageFormat format = ImageFormat.PNG)
    {
        return GetImGuiWindowRect(windowName, out var position, out var size) &&
               CaptureFramebufferRegion((int)position.X, (int)position.Y, (int)size.X, (int)size.Y,
                   filePath, format);
    }

    public static bool GetImGuiWindowRect(string windowName, out Vector2 position, out Vector2 size)
    {
        position = Vector2.Zero; size = Vector2.Zero;
        if (!ImGui.Begin(windowName)) { ImGui.End(); return false; }
        position = ImGui.GetWindowPos(); size = ImGui.GetWindowSize(); ImGui.End(); return true;
    }

    public static bool CaptureFullFramebuffer(string filePath, ImageFormat format = ImageFormat.PNG)
    {
        var window = OpenTkManager.MainWindow;
        if (window == null) return false;
        return Schedule(0, 0, window.FramebufferSize.X, window.FramebufferSize.Y, filePath, format, 90, null);
    }

    public static bool CaptureFramebufferRegion(int x, int y, int width, int height, string filePath,
        ImageFormat format = ImageFormat.PNG) => Schedule(x, y, width, height, filePath, format, 90, null);

    internal static bool Schedule(int x, int y, int width, int height, string path, ImageFormat format,
        int quality, Action<bool> callback)
    {
        if (width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(path)) return false;
        lock (Gate) Pending.Enqueue(new Request(x, y, width, height, path, format, quality, callback));
        return true;
    }

    public static bool CaptureTexture(int textureId, int width, int height, string filePath,
        ImageFormat format = ImageFormat.PNG, int jpegQuality = 90)
    {
        if (textureId == 0 || width <= 0 || height <= 0) return false;
        var previous = GL.GetInteger(GetPName.FramebufferBinding);
        var fbo = GL.GenFramebuffer();
        try
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, textureId, 0);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                return false;
            var rgba = ReadPixels(0, 0, width, height, false);
            return SaveImage(rgba, width, height, filePath, format, jpegQuality);
        }
        catch (Exception ex) { Logger.LogError($"[Screenshot] Texture capture failed: {ex.Message}"); return false; }
        finally { GL.BindFramebuffer(FramebufferTarget.Framebuffer, previous); GL.DeleteFramebuffer(fbo); }
    }

    public static void ProcessDeferredCaptures()
    {
        while (true)
        {
            Request request;
            lock (Gate) { if (Pending.Count == 0) break; request = Pending.Dequeue(); }
            var success = false;
            try
            {
                var framebuffer = OpenTkManager.MainWindow?.FramebufferSize;
                if (framebuffer is not { X: > 0, Y: > 0 }) throw new InvalidOperationException("No OpenGL framebuffer.");
                var x = Math.Clamp(request.X, 0, framebuffer.Value.X - 1);
                var yTop = Math.Clamp(request.Y, 0, framebuffer.Value.Y - 1);
                var width = Math.Min(request.Width, framebuffer.Value.X - x);
                var height = Math.Min(request.Height, framebuffer.Value.Y - yTop);
                if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(request));
                var glY = framebuffer.Value.Y - yTop - height;
                var pixels = ReadPixels(x, glY, width, height, true);
                success = SaveImage(pixels, width, height, request.Path, request.Format, request.JpegQuality);
            }
            catch (Exception ex) { Logger.LogError($"[Screenshot] Capture failed: {ex.Message}"); }
            request.Callback?.Invoke(success);
        }
    }

    private static byte[] ReadPixels(int x, int y, int width, int height, bool flipVertically)
    {
        var data = new byte[checked(width * height * 4)];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.ReadPixels(x, y, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        if (!flipVertically) return data;
        var rowBytes = width * 4; var row = new byte[rowBytes];
        for (var top=0;top<height/2;top++){var bottom=height-1-top;System.Buffer.BlockCopy(data,top*rowBytes,row,0,rowBytes);System.Buffer.BlockCopy(data,bottom*rowBytes,data,top*rowBytes,rowBytes);System.Buffer.BlockCopy(row,0,data,bottom*rowBytes,rowBytes);}
        return data;
    }

    internal static bool SaveImage(byte[] rgba, int width, int height, string path, ImageFormat format,
        int jpegQuality = 90)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            var writer = new ImageWriter();
            switch (format)
            {
                case ImageFormat.PNG: writer.WritePng(rgba,width,height,ColorComponents.RedGreenBlueAlpha,stream); break;
                case ImageFormat.JPEG: writer.WriteJpg(rgba,width,height,ColorComponents.RedGreenBlueAlpha,stream,jpegQuality); break;
                case ImageFormat.BMP: writer.WriteBmp(rgba,width,height,ColorComponents.RedGreenBlueAlpha,stream); break;
                case ImageFormat.TGA: writer.WriteTga(rgba,width,height,ColorComponents.RedGreenBlueAlpha,stream); break;
            }
            Logger.Log($"[Screenshot] Saved {path}"); return true;
        }
        catch (Exception ex) { Logger.LogError($"[Screenshot] Save failed: {ex.Message}"); return false; }
    }

    public static void Cleanup() { lock (Gate) Pending.Clear(); }
}
