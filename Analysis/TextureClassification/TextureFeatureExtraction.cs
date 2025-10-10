// GeoscientistToolkit/Analysis/TextureClassification/TextureFeatureExtractor.cs

namespace GeoscientistToolkit.Analysis.TextureClassification;

public class TextureFeatureExtractor
{
    public float[] ExtractGLCMFeatures(byte[] sliceData, int width, int height, int distance, int numAngles)
    {
        var features = new float[width * height];

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var glcm = ComputeLocalGLCM(sliceData, width, height, x, y, distance, numAngles);
                features[y * width + x] = ComputeGLCMContrast(glcm);
            }
        });

        return features;
    }

    public float[] ExtractLBPFeatures(byte[] sliceData, int width, int height, int radius, int points)
    {
        var features = new float[width * height];

        Parallel.For(radius, height - radius, y =>
        {
            for (var x = radius; x < width - radius; x++)
                features[y * width + x] = ComputeLBP(sliceData, width, height, x, y, radius, points);
        });

        return features;
    }

    public float[] ExtractGaborFeatures(byte[] sliceData, int width, int height, int numScales, int numOrientations)
    {
        var features = new float[width * height];
        var responses = new float[numScales * numOrientations];

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var maxResponse = 0f;

                for (var scale = 0; scale < numScales; scale++)
                for (var orientation = 0; orientation < numOrientations; orientation++)
                {
                    var response = ComputeGaborResponse(sliceData, width, height, x, y,
                        scale, orientation, numOrientations);
                    maxResponse = Math.Max(maxResponse, Math.Abs(response));
                }

                features[y * width + x] = maxResponse;
            }
        });

        return features;
    }

    private float[,] ComputeLocalGLCM(byte[] data, int width, int height, int cx, int cy, int distance, int numAngles)
    {
        var glcm = new float[256, 256];
        var windowSize = 7;
        var halfWindow = windowSize / 2;

        for (var dy = -halfWindow; dy <= halfWindow; dy++)
        for (var dx = -halfWindow; dx <= halfWindow; dx++)
        {
            var x = cx + dx;
            var y = cy + dy;

            if (x < 0 || x >= width || y < 0 || y >= height) continue;

            int intensity1 = data[y * width + x];

            // Check neighbor at distance
            var nx = x + distance;
            var ny = y;

            if (nx >= 0 && nx < width)
            {
                int intensity2 = data[ny * width + nx];
                glcm[intensity1, intensity2]++;
            }
        }

        // Normalize
        float sum = 0;
        for (var i = 0; i < 256; i++)
        for (var j = 0; j < 256; j++)
            sum += glcm[i, j];

        if (sum > 0)
            for (var i = 0; i < 256; i++)
            for (var j = 0; j < 256; j++)
                glcm[i, j] /= sum;

        return glcm;
    }

    private float ComputeGLCMContrast(float[,] glcm)
    {
        float contrast = 0;

        for (var i = 0; i < 256; i++)
        for (var j = 0; j < 256; j++)
            contrast += (i - j) * (i - j) * glcm[i, j];

        return contrast;
    }

    private float ComputeLBP(byte[] data, int width, int height, int cx, int cy, int radius, int points)
    {
        int center = data[cy * width + cx];
        var lbpValue = 0;

        for (var p = 0; p < points; p++)
        {
            var angle = 2 * MathF.PI * p / points;
            var nx = (int)(cx + radius * MathF.Cos(angle));
            var ny = (int)(cy + radius * MathF.Sin(angle));

            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
            {
                int neighbor = data[ny * width + nx];
                if (neighbor >= center) lbpValue |= 1 << p;
            }
        }

        return lbpValue / (float)((1 << points) - 1);
    }

    private float ComputeGaborResponse(byte[] data, int width, int height, int cx, int cy,
        int scale, int orientation, int numOrientations)
    {
        var wavelength = 4.0f * (1 << scale);
        var theta = MathF.PI * orientation / numOrientations;
        var sigma = wavelength * 0.56f;
        var gamma = 0.5f;

        float response = 0;
        var kernelSize = (int)(sigma * 3);

        for (var dy = -kernelSize; dy <= kernelSize; dy++)
        for (var dx = -kernelSize; dx <= kernelSize; dx++)
        {
            var x = cx + dx;
            var y = cy + dy;

            if (x < 0 || x >= width || y < 0 || y >= height) continue;

            var xTheta = dx * MathF.Cos(theta) + dy * MathF.Sin(theta);
            var yTheta = -dx * MathF.Sin(theta) + dy * MathF.Cos(theta);

            var envelope = MathF.Exp(-(xTheta * xTheta + gamma * gamma * yTheta * yTheta) / (2 * sigma * sigma));
            var carrier = MathF.Cos(2 * MathF.PI * xTheta / wavelength);

            var gaborValue = envelope * carrier;
            response += data[y * width + x] * gaborValue;
        }

        return response;
    }
}