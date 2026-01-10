# GeoScript Image Operations

This guide documents the image processing commands available in GeoScript, with practical examples and best practices.

---

## Overview

GeoScript provides comprehensive image processing capabilities including:
- Brightness/contrast adjustments
- Various filters (Gaussian, median, Sobel, etc.)
- Segmentation operations (threshold, binarize)
- Color operations (grayscale, invert, normalize)
- Pipeline syntax for operation chaining

---

## Getting Started

### Opening the GeoScript Editor

1. Go to **File → GeoScript Editor...**
2. Select an image or CT dataset from the dropdown
3. Write your script
4. Click **Run Script**

### Basic Syntax

GeoScript uses a pipeline syntax with the `|>` operator to chain operations:

```geoscript
# Single operation
FILTER type=gaussian size=5

# Chained operations
FILTER type=gaussian size=5 |> BRIGHTNESS_CONTRAST brightness=10 contrast=1.2

# Multi-step pipeline
GRAYSCALE |> THRESHOLD min=100 max=200 |> INVERT
```

---

## Image Processing Commands

### BRIGHTNESS_CONTRAST

Adjust brightness and contrast of an image.

**Syntax:**
```geoscript
BRIGHTNESS_CONTRAST brightness=<-100 to 100> contrast=<0.1 to 3.0>
```

**Parameters:**
| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| `brightness` | -100 to +100 | 0 | Brightness adjustment |
| `contrast` | 0.1 to 3.0 | 1.0 | Contrast multiplier |

**Examples:**
```geoscript
# Increase brightness only
BRIGHTNESS_CONTRAST brightness=20

# Increase contrast only
BRIGHTNESS_CONTRAST contrast=1.5

# Adjust both
BRIGHTNESS_CONTRAST brightness=10 contrast=1.2
```

---

### FILTER

Apply various image filters for noise reduction, edge detection, and enhancement.

**Syntax:**
```geoscript
FILTER type=<filterType> [size=<kernelSize>] [sigma=<value>]
```

**Filter Types:**

| Type | Description | Best For |
|------|-------------|----------|
| `gaussian` | Gaussian blur | Noise reduction, smoothing |
| `median` | Median filter | Salt-and-pepper noise |
| `mean` / `box` | Mean/box filter | Simple averaging |
| `sobel` | Sobel edge detection | Edge detection |
| `canny` | Canny edge detection | Precise edges |
| `bilateral` | Edge-preserving blur | Noise reduction preserving edges |
| `nlm` | Non-local means | High-quality denoising |
| `unsharp` | Unsharp mask | Sharpening |

**Parameters:**
| Parameter | Default | Description |
|-----------|---------|-------------|
| `type` | (required) | Filter type |
| `size` | 5 (blur), 3 (median) | Kernel size in pixels |
| `sigma` | auto | Sigma for Gaussian filters |

**Examples:**
```geoscript
# Gaussian blur
FILTER type=gaussian size=5

# Median filter for noise reduction
FILTER type=median size=3

# Edge detection
FILTER type=sobel

# Chained filtering
FILTER type=median size=3 |> FILTER type=gaussian size=5
```

---

### THRESHOLD

Apply threshold segmentation to create a binary mask.

**Syntax:**
```geoscript
THRESHOLD min=<0-255> max=<0-255>
```

**Parameters:**
| Parameter | Range | Description |
|-----------|-------|-------------|
| `min` | 0-255 | Minimum threshold value |
| `max` | 0-255 | Maximum threshold value |

**Examples:**
```geoscript
# Threshold between 100 and 200
THRESHOLD min=100 max=200

# Segment bright regions
THRESHOLD min=180 max=255

# Pipeline with preprocessing
GRAYSCALE |> THRESHOLD min=128 max=255
```

---

### BINARIZE

Convert image to binary (black/white) using a threshold.

**Syntax:**
```geoscript
BINARIZE threshold=<0-255 or 'auto'>
```

**Parameters:**
| Parameter | Value | Description |
|-----------|-------|-------------|
| `threshold` | 0-255 | Manual threshold value |
| `threshold` | 'auto' | Otsu's automatic thresholding |

**Examples:**
```geoscript
# Manual threshold
BINARIZE threshold=128

# Automatic Otsu thresholding
BINARIZE threshold=auto

# Pipeline with preprocessing
FILTER type=gaussian size=3 |> BINARIZE threshold=auto
```

---

### GRAYSCALE

Convert image to grayscale.

**Syntax:**
```geoscript
GRAYSCALE
```

**Examples:**
```geoscript
# Convert to grayscale
GRAYSCALE

# Grayscale then threshold
GRAYSCALE |> THRESHOLD min=100 max=200
```

---

### INVERT

Invert image colors (create negative).

**Syntax:**
```geoscript
INVERT
```

**Examples:**
```geoscript
# Invert colors
INVERT

# Create negative of binary mask
BINARIZE threshold=128 |> INVERT
```

---

### NORMALIZE

Normalize image to full intensity range (histogram stretch).

**Syntax:**
```geoscript
NORMALIZE
```

**Examples:**
```geoscript
# Normalize low-contrast image
NORMALIZE

# Pipeline with normalization
FILTER type=median size=3 |> NORMALIZE
```

---

## Complete Workflow Examples

### Example 1: Basic Image Enhancement

```geoscript
# Denoise and enhance an image
FILTER type=median size=3 |>
  BRIGHTNESS_CONTRAST brightness=10 contrast=1.3 |>
  NORMALIZE
```

### Example 2: Edge Detection Pipeline

```geoscript
# Prepare image for edge detection
GRAYSCALE |>
  FILTER type=gaussian size=3 |>
  FILTER type=sobel
```

### Example 3: Complete Segmentation Workflow

```geoscript
# Full segmentation pipeline
FILTER type=median size=3 |>
  GRAYSCALE |>
  NORMALIZE |>
  BINARIZE threshold=auto |>
  INFO
```

### Example 4: Multi-threshold Segmentation

```geoscript
# Segment different intensity ranges
# Run each separately to create multiple masks

# First range (dark regions)
THRESHOLD min=0 max=85

# Second range (mid-tones)
THRESHOLD min=85 max=170

# Third range (bright regions)
THRESHOLD min=170 max=255
```

### Example 5: Advanced Denoising

```geoscript
# Multi-stage denoising for noisy CT data
FILTER type=median size=3 |>
  FILTER type=bilateral size=5 |>
  NORMALIZE
```

---

## Dataset Type Support

| Operation | SingleImage | CtImageStack | Table | GIS |
|-----------|-------------|--------------|-------|-----|
| BRIGHTNESS_CONTRAST | ✓ | ✓ | - | - |
| FILTER | ✓ | ✓ | - | - |
| THRESHOLD | ✓ | ✓ | - | - |
| BINARIZE | ✓ | ✓ | - | - |
| GRAYSCALE | ✓ | ✓ | - | - |
| INVERT | ✓ | ✓ | - | - |
| NORMALIZE | ✓ | ✓ | - | - |
| LISTOPS | ✓ | ✓ | ✓ | ✓ |
| DISPTYPE | ✓ | ✓ | ✓ | ✓ |
| INFO | ✓ | ✓ | ✓ | ✓ |

---

## Tips and Best Practices

### 1. Always Preprocess Before Segmentation

```geoscript
# Apply noise reduction before binarization
FILTER type=median size=3 |> BINARIZE threshold=auto
```

### 2. Use Grayscale for Segmentation

```geoscript
# Most segmentation algorithms work better on grayscale
GRAYSCALE |> THRESHOLD min=100 max=200
```

### 3. Normalize First

```geoscript
# Normalize images before threshold-based operations
NORMALIZE |> BINARIZE threshold=128
```

### 4. Check Results with INFO

```geoscript
# Verify output after operations
FILTER type=gaussian size=5 |> INFO
```

### 5. List Available Operations

```geoscript
# See what's available for your dataset type
LISTOPS
```

### 6. Choose the Right Filter

| Noise Type | Recommended Filter |
|------------|-------------------|
| Gaussian noise | `gaussian` |
| Salt-and-pepper | `median` |
| Mixed noise | `bilateral` |
| High noise | `nlm` |

---

## Performance Notes

- Operations create new output datasets (non-destructive)
- Large images may take time to process
- All outputs are automatically added to the project
- Use `UNLOAD` to free memory when done

---

## Error Handling

- Operations check dataset compatibility automatically
- Invalid parameters show clear error messages
- Pipeline execution stops at first error
- Check the Output panel for detailed error information

---

## Related Pages

- [GeoScript Manual](GeoScript-Manual) - Complete scripting reference
- [CT Imaging and Segmentation](CT-Imaging-and-Segmentation) - CT-specific operations
- [User Guide](User-Guide) - Application documentation
- [Home](Home) - Wiki home page
