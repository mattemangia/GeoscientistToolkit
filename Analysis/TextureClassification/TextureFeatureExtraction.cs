// GeoscientistToolkit/Analysis/TextureClassification/TextureFeatureExtractor.cs

namespace GeoscientistToolkit.Analysis.TextureClassification;

public class TextureFeatureExtractor
{
    // ALGORITHM: Gray-Level Co-occurrence Matrix (GLCM) Texture Features
    //
    // Computes texture features from the GLCM, which captures spatial relationships between
    // pixel intensities. The GLCM is a histogram of co-occurring grayscale values at a given
    // offset. Features like contrast, correlation, energy, and homogeneity characterize texture.
    //
    // References:
    // - Haralick, R.M., Shanmugam, K., & Dinstein, I. (1973). "Textural features for image
    //   classification." IEEE Transactions on Systems, Man, and Cybernetics, SMC-3(6), 610-621.
    //   DOI: 10.1109/TSMC.1973.4309314
    //
    // - Haralick, R.M. (1979). "Statistical and structural approaches to texture."
    //   Proceedings of the IEEE, 67(5), 786-804.
    //   DOI: 10.1109/PROC.1979.11328
    //
    // - Clausi, D.A. (2002). "An analysis of co-occurrence texture statistics as a function of
    //   grey level quantization." Canadian Journal of Remote Sensing, 28(1), 45-62.
    //   DOI: 10.5589/m02-004
    //
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

    // ALGORITHM: Local Binary Patterns (LBP)
    //
    // Encodes local texture by comparing each pixel with its circular neighborhood. The LBP
    // descriptor is rotation-invariant and highly effective for texture classification. Each
    // pixel is assigned a binary code based on intensity differences with neighbors.
    //
    // References:
    // - Ojala, T., Pietikäinen, M., & Harwood, D. (1996). "A comparative study of texture measures
    //   with classification based on featured distributions." Pattern Recognition, 29(1), 51-59.
    //   DOI: 10.1016/0031-3203(95)00067-4
    //
    // - Ojala, T., Pietikäinen, M., & Mäenpää, T. (2002). "Multiresolution gray-scale and rotation
    //   invariant texture classification with local binary patterns." IEEE Transactions on Pattern
    //   Analysis and Machine Intelligence, 24(7), 971-987.
    //   DOI: 10.1109/TPAMI.2002.1017623
    //
    // - Pietikäinen, M., Hadid, A., Zhao, G., & Ahonen, T. (2011). "Computer Vision Using Local
    //   Binary Patterns." Springer.
    //   ISBN: 978-0-85729-748-8
    //
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

    // ALGORITHM: Gabor Filter Bank for Texture Analysis
    //
    // Applies a bank of Gabor filters at multiple scales and orientations to extract texture
    // features. Gabor filters are Gaussian-modulated sinusoids that are optimal for capturing
    // oriented frequency content, making them effective for texture segmentation and classification.
    //
    // References:
    // - Gabor, D. (1946). "Theory of communication. Part 1: The analysis of information."
    //   Journal of the Institution of Electrical Engineers - Part III: Radio and Communication
    //   Engineering, 93(26), 429-441.
    //   DOI: 10.1049/ji-3-2.1946.0074
    //
    // - Daugman, J.G. (1985). "Uncertainty relation for resolution in space, spatial frequency,
    //   and orientation optimized by two-dimensional visual cortical filters." Journal of the
    //   Optical Society of America A, 2(7), 1160-1169.
    //   DOI: 10.1364/JOSAA.2.001160
    //
    // - Manjunath, B.S., & Ma, W.Y. (1996). "Texture features for browsing and retrieval of image
    //   data." IEEE Transactions on Pattern Analysis and Machine Intelligence, 18(8), 837-842.
    //   DOI: 10.1109/34.531803
    //
    // - Jain, A.K., & Farrokhnia, F. (1991). "Unsupervised texture segmentation using Gabor filters."
    //   Pattern Recognition, 24(12), 1167-1186.
    //   DOI: 10.1016/0031-3203(91)90143-S
    //
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