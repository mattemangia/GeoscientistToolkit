// GeoscientistToolkit/Business/Photogrammetry/CameraCalibration.cs

using System;
using System.Numerics;
using GeoscientistToolkit.Business.Photogrammetry;

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

            // Default estimate: 1.2x the larger dimension
            return 1.2f * Math.Max(image.Dataset.Width, image.Dataset.Height);
        }
    }
}