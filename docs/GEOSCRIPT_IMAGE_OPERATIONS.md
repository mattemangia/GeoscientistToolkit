# GeoScript Dataset Operations Guide

This guide documents the new dataset operation commands available in GeoScript, with a focus on image processing and dataset manipulation.

## Overview

GeoScript now supports comprehensive dataset operations including:
- Image processing (brightness/contrast, filters, segmentation)
- Operation chaining with pipeline syntax
- Utility commands for dataset inspection
- Integration with existing thermodynamics and GIS commands

## Getting Started

### Opening the GeoScript Editor

1. Go to **File → GeoScript Editor...**
2. Select a dataset from the dropdown
3. Write your script
4. Click **▶ Run Script**

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

## Image Processing Commands

### BRIGHTNESS_CONTRAST

Adjust brightness and contrast of an image.

**Syntax:**
```geoscript
BRIGHTNESS_CONTRAST brightness=<-100 to 100> contrast=<0.1 to 3.0>
```

**Parameters:**
- `brightness`: Brightness adjustment (-100 to +100)
- `contrast`: Contrast multiplier (0.1 to 3.0)

**Examples:**
```geoscript
# Increase brightness
BRIGHTNESS_CONTRAST brightness=20

# Increase contrast
BRIGHTNESS_CONTRAST contrast=1.5

# Adjust both
BRIGHTNESS_CONTRAST brightness=10 contrast=1.2
```

---

### FILTER

Apply various image filters.

**Syntax:**
```geoscript
FILTER type=<filterType> [size=<kernelSize>] [sigma=<value>]
```

**Filter Types:**
- `gaussian` - Gaussian blur
- `median` - Median filter (noise reduction)
- `mean` / `box` - Mean/box filter
- `sobel` - Sobel edge detection
- `canny` - Canny edge detection
- `bilateral` - Bilateral filter
- `nlm` - Non-local means filter
- `unsharp` - Unsharp mask

**Parameters:**
- `type`: Filter type (required)
- `size`: Kernel size (default: 5 for blur filters, 3 for median)
- `sigma`: Sigma value for Gaussian filters

**Examples:**
```geoscript
# Apply Gaussian blur
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
- `min`: Minimum threshold value (0-255)
- `max`: Maximum threshold value (0-255)

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
- `threshold`: Threshold value (0-255) or 'auto' for Otsu's method

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

Invert image colors.

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

Normalize image to full intensity range.

**Syntax:**
```geoscript
NORMALIZE
```

**Examples:**
```geoscript
# Normalize intensity
NORMALIZE

# Pipeline with normalization
FILTER type=median size=3 |> NORMALIZE
```

## Utility Commands

### LISTOPS

List all available operations for the current dataset type.

**Syntax:**
```geoscript
LISTOPS
```

**Example:**
```geoscript
# Show available operations
LISTOPS
```

---

### DISPTYPE

Display detailed information about the dataset.

**Syntax:**
```geoscript
DISPTYPE
```

**Example:**
```geoscript
# Show dataset information
DISPTYPE
```

---

### INFO

Display quick summary information.

**Syntax:**
```geoscript
INFO
```

**Example:**
```geoscript
# Quick info
INFO
```

---

### UNLOAD

Unload dataset from memory.

**Syntax:**
```geoscript
UNLOAD
```

**Example:**
```geoscript
# Free memory
UNLOAD
```

## Complete Examples

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

### Example 3: Segmentation Workflow

```geoscript
# Complete segmentation workflow
FILTER type=median size=3 |>
  GRAYSCALE |>
  NORMALIZE |>
  BINARIZE threshold=auto |>
  INFO
```

### Example 4: Multi-threshold Segmentation

```geoscript
# Segment different intensity ranges (requires running separately)

# First range
THRESHOLD min=0 max=85

# Second range
THRESHOLD min=85 max=170

# Third range
THRESHOLD min=170 max=255
```

## Tips and Best Practices

1. **Always preprocess:** Apply noise reduction filters before segmentation
   ```geoscript
   FILTER type=median size=3 |> BINARIZE threshold=auto
   ```

2. **Use grayscale for segmentation:** Most segmentation algorithms work better on grayscale
   ```geoscript
   GRAYSCALE |> THRESHOLD min=100 max=200
   ```

3. **Normalize first:** Normalize images before threshold-based operations
   ```geoscript
   NORMALIZE |> BINARIZE threshold=128
   ```

4. **Check results:** Use INFO to verify output datasets
   ```geoscript
   FILTER type=gaussian size=5 |> INFO
   ```

5. **List operations:** Use LISTOPS to see what's available for your dataset type
   ```geoscript
   LISTOPS
   ```

## Dataset Types Support

| Operation | SingleImage | CtImageStack | Table | GIS |
|-----------|------------|--------------|-------|-----|
| BRIGHTNESS_CONTRAST | ✓ | ✓ | ✗ | ✗ |
| FILTER | ✓ | ✓ | ✗ | ✗ |
| THRESHOLD | ✓ | ✓ | ✗ | ✗ |
| BINARIZE | ✓ | ✓ | ✗ | ✗ |
| GRAYSCALE | ✓ | ✓ | ✗ | ✗ |
| INVERT | ✓ | ✓ | ✗ | ✗ |
| NORMALIZE | ✓ | ✓ | ✗ | ✗ |
| LISTOPS | ✓ | ✓ | ✓ | ✓ |
| DISPTYPE | ✓ | ✓ | ✓ | ✓ |
| UNLOAD | ✓ | ✓ | ✓ | ✓ |
| INFO | ✓ | ✓ | ✓ | ✓ |

## Integration with Existing Commands

GeoScript image operations integrate seamlessly with existing commands:

### With Thermodynamics
```geoscript
# Process image, then run thermodynamic simulation
BINARIZE threshold=auto |> EQUILIBRATE temp=25 pressure=1
```

### With Table Operations
```geoscript
# Table operations (on Table datasets)
SELECT WHERE 'Value' > 100 |> SORTBY 'Name' DESC
```

### With GIS Operations
```geoscript
# GIS operations (on GIS datasets)
BUFFER distance=100 |> DISSOLVE
```

## Error Handling

- Operations check dataset compatibility
- Invalid parameters will show clear error messages
- Pipeline execution stops at first error
- Check the Output panel for detailed error information

## Performance Notes

- Operations create new output datasets (non-destructive)
- Large images may take time to process
- All outputs are automatically added to the project
- Use UNLOAD to free memory when done

## Future Enhancements

Planned additions:
- [ ] Morphological operations (erode, dilate, open, close)
- [ ] Color space conversions (HSV, LAB, etc.)
- [ ] Advanced segmentation (watershed, region growing)
- [ ] Image arithmetic operations
- [ ] Geometric transformations (rotate, resize, crop)
- [ ] Batch processing capabilities

## See Also

- [GeoScript Thermodynamics Guide](./GEOSCRIPT_THERMODYNAMICS.md)
- [GeoScript GIS Operations](./GEOSCRIPT_GIS.md)
- [GeoScript Table Operations](./GEOSCRIPT_TABLE.md)
