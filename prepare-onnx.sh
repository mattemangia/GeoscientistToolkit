#!/bin/bash
#
# prepare-onnx.sh
# Script to prepare ONNX models after publish for distribution.
# Run this script after dotnet publish to prepare ONNX models for packaging.
#
# Usage: ./prepare-onnx.sh [output-directory]
#   output-directory: Optional. Where to copy ONNX models. Default: ./ONNX
#
# Supported models:
#   - SAM2 (Segment Anything Model 2)
#   - MicroSAM
#   - Grounding DINO
#   - MiDaS (Depth estimation)
#
set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ONNX_SOURCE_DIR="$SCRIPT_DIR/ONNX"
ONNX_TEMPLATE_DIR="$SCRIPT_DIR/InstallerPackager/Assets/onnx-installer"
OUTPUT_DIR="${1:-$ONNX_SOURCE_DIR}"

echo "=========================================="
echo "  ONNX Model Preparation Script"
echo "=========================================="
echo ""

# Create output directory if it doesn't exist
mkdir -p "$OUTPUT_DIR"

echo "Source directory: $ONNX_SOURCE_DIR"
echo "Output directory: $OUTPUT_DIR"
echo ""

# Check for existing ONNX files
ONNX_FILES=$(find "$ONNX_SOURCE_DIR" -name "*.onnx" 2>/dev/null | wc -l)

if [ "$ONNX_FILES" -gt 0 ]; then
    echo "Found $ONNX_FILES ONNX model(s) in source directory."
    echo ""
    echo "Models found:"
    find "$ONNX_SOURCE_DIR" -name "*.onnx" -exec basename {} \;
    echo ""
else
    echo "No ONNX models found in $ONNX_SOURCE_DIR"
    echo ""
    echo "To use this script:"
    echo "  1. Download ONNX models manually or using the URLs below"
    echo "  2. Place them in: $ONNX_SOURCE_DIR"
    echo "  3. Run this script again"
    echo ""
    echo "Common ONNX model sources:"
    echo "  - SAM2: https://github.com/facebookresearch/segment-anything-2"
    echo "  - MicroSAM: https://github.com/computational-cell-analytics/micro-sam"
    echo "  - MiDaS: https://github.com/isl-org/MiDaS"
    echo "  - Grounding DINO: https://github.com/IDEA-Research/GroundingDINO"
    echo ""
fi

# Prepare installer template with models
TEMPLATE_MODELS_DIR="$ONNX_TEMPLATE_DIR/models"
mkdir -p "$TEMPLATE_MODELS_DIR"

if [ "$ONNX_FILES" -gt 0 ]; then
    echo "Copying ONNX models to installer template..."
    cp -v "$ONNX_SOURCE_DIR"/*.onnx "$TEMPLATE_MODELS_DIR/" 2>/dev/null || true

    # Also copy any subdirectory structure
    for subdir in "$ONNX_SOURCE_DIR"/*/; do
        if [ -d "$subdir" ]; then
            dirname=$(basename "$subdir")
            if [ "$dirname" != "." ] && [ "$dirname" != ".." ]; then
                mkdir -p "$TEMPLATE_MODELS_DIR/$dirname"
                cp -rv "$subdir"* "$TEMPLATE_MODELS_DIR/$dirname/" 2>/dev/null || true
            fi
        fi
    done

    echo ""
    echo "Models copied to installer template successfully!"
fi

# Summary
echo ""
echo "=========================================="
echo "  Summary"
echo "=========================================="
echo ""
echo "ONNX models directory: $ONNX_SOURCE_DIR"
echo "Installer template: $ONNX_TEMPLATE_DIR"
echo ""

if [ "$ONNX_FILES" -gt 0 ]; then
    echo "Status: Ready for packaging"
    echo ""
    echo "Next steps:"
    echo "  1. Run the packager: dotnet run --project InstallerPackager"
    echo "  2. The ONNX models will be included in the installer packages"
else
    echo "Status: No models found - add .onnx files to $ONNX_SOURCE_DIR"
fi

echo ""
echo "Done."
