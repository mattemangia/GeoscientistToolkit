// GeoscientistToolkit/Business/Photogrammetry/CameraCalibration.cs

using System;
using System.Numerics;
using GeoscientistToolkit.Business.Photogrammetry;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Handles camera calibration and intrinsic parameter computation
    /// </summary>
    public static class CameraCalibration
    {
        /// <summary>
        /// Computes camera intrinsic matrix from image metadata
        /// </summary>
        public static Matrix4x4 ComputeIntrinsics(PhotogrammetryImage image)
        {
            float focalLengthPixels = ComputeFocalLengthInPixels(image);
            float cx = image.Dataset.Width / 2.0f;
            float cy = image.Dataset.Height / 2.0f;

            return new Matrix4x4(
                focalLengthPixels, 0, cx, 0,
                0, focalLengthPixels, cy, 0,
                0, 0, 1, 0,
                0, 0, 0, 1
            );
        }

        private static float ComputeFocalLengthInPixels(PhotogrammetryImage image)
        {
            if (image.FocalLengthMm.HasValue && image.SensorWidthMm.HasValue)
            {
                return (image.FocalLengthMm.Value / image.SensorWidthMm.Value) * image.Dataset.Width;
            }

            // Default estimate: Use 1.0x the larger dimension (more conservative than 1.2x)
            // This corresponds to approximately 50° field of view, typical for smartphones
            float estimate = 1.0f * Math.Max(image.Dataset.Width, image.Dataset.Height);
            
            Logger.Log($"[CameraCalibration] Warning: No EXIF focal length for {image.Dataset.Name}, " +
                       $"using default estimate: {estimate:F1}px (assumes ~50° FOV)");
            
            return estimate;
        }
    }
}