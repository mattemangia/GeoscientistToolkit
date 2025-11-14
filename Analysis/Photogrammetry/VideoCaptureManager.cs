// GeoscientistToolkit/Analysis/Photogrammetry/VideoCaptureManager.cs

using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// Manages video capture from webcam or file.
/// </summary>
public class VideoCaptureManager : IDisposable
{
    private VideoCapture _capture;
    private readonly List<CameraInfo> _availableCameras = new();
    private bool _isCapturing;

    public class CameraInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Fps { get; set; }
    }

    public bool IsCapturing => _isCapturing;
    public List<CameraInfo> AvailableCameras => _availableCameras;
    public double CurrentFps => _capture?.Fps ?? 0;
    public int CurrentWidth => _capture?.FrameWidth ?? 0;
    public int CurrentHeight => _capture?.FrameHeight ?? 0;

    public VideoCaptureManager()
    {
        DetectCameras();
    }

    /// <summary>
    /// Detect available cameras.
    /// </summary>
    private void DetectCameras()
    {
        _availableCameras.Clear();

        // Try to open cameras 0-9
        for (int i = 0; i < 10; i++)
        {
            try
            {
                using var testCapture = new VideoCapture(i);
                if (testCapture.IsOpened())
                {
                    var info = new CameraInfo
                    {
                        Index = i,
                        Name = $"Camera {i}",
                        Width = testCapture.FrameWidth,
                        Height = testCapture.FrameHeight,
                        Fps = testCapture.Fps
                    };
                    _availableCameras.Add(info);
                }
            }
            catch
            {
                // Camera not available
            }
        }

        Logger.Log($"VideoCaptureManager: Detected {_availableCameras.Count} cameras");
    }

    /// <summary>
    /// Open camera by index.
    /// </summary>
    public bool OpenCamera(int cameraIndex, int targetWidth = 640, int targetHeight = 480)
    {
        CloseCamera();

        try
        {
            _capture = new VideoCapture(cameraIndex);

            if (!_capture.IsOpened())
            {
                Logger.LogError($"VideoCaptureManager: Failed to open camera {cameraIndex}");
                return false;
            }

            // Set resolution
            _capture.FrameWidth = targetWidth;
            _capture.FrameHeight = targetHeight;

            _isCapturing = true;
            Logger.Log($"VideoCaptureManager: Opened camera {cameraIndex} at {targetWidth}x{targetHeight}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"VideoCaptureManager: Error opening camera: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Open video file.
    /// </summary>
    public bool OpenFile(string videoPath)
    {
        CloseCamera();

        try
        {
            _capture = new VideoCapture(videoPath);

            if (!_capture.IsOpened())
            {
                Logger.LogError($"VideoCaptureManager: Failed to open video file {videoPath}");
                return false;
            }

            _isCapturing = true;
            Logger.Log($"VideoCaptureManager: Opened video file {videoPath}");
            Logger.Log($"  Resolution: {_capture.FrameWidth}x{_capture.FrameHeight}");
            Logger.Log($"  FPS: {_capture.Fps}");
            Logger.Log($"  Frame count: {_capture.FrameCount}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"VideoCaptureManager: Error opening video file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Capture next frame.
    /// </summary>
    public bool CaptureFrame(out Mat frame)
    {
        frame = null;

        if (_capture == null || !_capture.IsOpened())
            return false;

        try
        {
            frame = new Mat();
            bool success = _capture.Read(frame);

            if (!success || frame.Empty())
            {
                frame?.Dispose();
                frame = null;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"VideoCaptureManager: Error capturing frame: {ex.Message}");
            frame?.Dispose();
            frame = null;
            return false;
        }
    }

    /// <summary>
    /// Close camera/video.
    /// </summary>
    public void CloseCamera()
    {
        if (_capture != null)
        {
            _capture.Release();
            _capture.Dispose();
            _capture = null;
            _isCapturing = false;
            Logger.Log("VideoCaptureManager: Camera/video closed");
        }
    }

    public void Dispose()
    {
        CloseCamera();
    }
}
