using System;
using System.Collections.Generic;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Scripting.GeoScript.Operations;
using Xunit;

namespace VerificationTests;

public class ImageFilterTests
{
    [Fact]
    public void GaussianFilter_ImpulseResponse_CreatesPeak()
    {
        // Arrange
        int size = 11;
        var dataset = new ImageDataset("TestImage", "")
        {
            Width = size,
            Height = size,
            BitDepth = 8,
            PixelSize = 1.0f,
            ImageData = new byte[size * size * 4]
        };

        // Create an impulse in the center
        int cx = size / 2;
        int cy = size / 2;
        int centerIdx = (cy * size + cx) * 4;

        // Set center pixel to white (255)
        dataset.ImageData[centerIdx] = 255;
        dataset.ImageData[centerIdx + 1] = 255;
        dataset.ImageData[centerIdx + 2] = 255;
        dataset.ImageData[centerIdx + 3] = 255;

        // Act
        var filterOp = new FilterOperation();
        var parameters = new List<object> { "gaussian", 5 }; // Gaussian filter with kernel size 5
        var result = (ImageDataset)filterOp.Execute(dataset, parameters);

        // Assert
        // Check the center pixel value
        byte centerVal = result.ImageData[centerIdx];

        // Check immediate neighbor (e.g., cx+1, cy)
        int neighborIdx = (cy * size + (cx + 1)) * 4;
        byte neighborVal = result.ImageData[neighborIdx];

        // For a box blur (current implementation), the impulse response is a flat square.
        // So centerVal should be roughly equal to neighborVal (both ~ 255/25 = 10).
        // For a Gaussian blur, centerVal should be significantly higher than neighborVal.

        Assert.True(centerVal > neighborVal,
            $"Gaussian filter should produce a peak. Center: {centerVal}, Neighbor: {neighborVal}. " +
            "If they are equal, it might still be using Box Blur.");

        // Additional check: The center value should be non-zero
        Assert.True(centerVal > 0, "Center value should be non-zero.");
    }
}
