using GeoscientistToolkit.Analysis.Photogrammetry;
using OpenCvSharp;
using Xunit;
using System.IO;

namespace VerificationTests
{
    public class PhotogrammetryTests
    {
        [Fact(Skip = "Requires OpenCV native dependencies not available in CI environment")]
        public void Undistort_WithCoefficients_ChangesImage()
        {
            // Arrange
            // Create a test image with a grid pattern
            int width = 640;
            int height = 480;
            using var inputImage = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);

            // Draw grid
            for (int i = 0; i < width; i += 20)
                Cv2.Line(inputImage, new Point(i, 0), new Point(i, height), Scalar.White, 1);
            for (int i = 0; i < height; i += 20)
                Cv2.Line(inputImage, new Point(0, i), new Point(width, i), Scalar.White, 1);

            // Config without distortion
            var configNoDist = new PhotogrammetryPipeline.PipelineConfig
            {
                TargetWidth = width,
                TargetHeight = height,
                FocalLengthX = 500,
                FocalLengthY = 500,
                PrincipalPointX = width / 2.0,
                PrincipalPointY = height / 2.0,
                DistortionCoefficients = null // No distortion
            };

            // Config with distortion
            var configDist = new PhotogrammetryPipeline.PipelineConfig
            {
                TargetWidth = width,
                TargetHeight = height,
                FocalLengthX = 500,
                FocalLengthY = 500,
                PrincipalPointX = width / 2.0,
                PrincipalPointY = height / 2.0,
                // Add significant radial distortion
                DistortionCoefficients = new double[] { 0.2, 0.1, 0, 0 }
            };

            using var pipelineNoDist = new PhotogrammetryPipeline(configNoDist);
            using var pipelineDist = new PhotogrammetryPipeline(configDist);

            // Act
            // Note: ProcessFrame logs warnings if models are missing but should return a result with PreprocessedFrame set.
            var resultNoDist = pipelineNoDist.ProcessFrame(inputImage);
            var resultDist = pipelineDist.ProcessFrame(inputImage);

            // Assert
            Assert.True(resultNoDist.Success, "Pipeline without distortion should succeed");
            Assert.True(resultDist.Success, "Pipeline with distortion should succeed");

            Assert.NotNull(resultNoDist.PreprocessedFrame);
            Assert.NotNull(resultDist.PreprocessedFrame);

            // Compare the two preprocessed frames.
            // They should be different because one is undistorted using the coefficients.
            using var diff = new Mat();
            // Ensure same size
            if (resultNoDist.PreprocessedFrame.Size() != resultDist.PreprocessedFrame.Size())
            {
                // This shouldn't happen as both use same target/camera params (except dist coeffs)
                 Assert.Fail("Output sizes differ");
            }

            Cv2.Absdiff(resultNoDist.PreprocessedFrame, resultDist.PreprocessedFrame, diff);

            // Convert to grayscale
            using var grayDiff = new Mat();
            Cv2.CvtColor(diff, grayDiff, ColorConversionCodes.BGR2GRAY);

            int nonZero = Cv2.CountNonZero(grayDiff);

            Assert.True(nonZero > 100, $"Images should be different. Non-zero pixel count difference: {nonZero}");
        }
    }
}
