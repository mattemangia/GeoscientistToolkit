#!/bin/sh
set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SOURCE_DIR="$SCRIPT_DIR/models"
TARGET_DIR="$SCRIPT_DIR/../ONNX"

if [ ! -d "$SOURCE_DIR" ]; then
  echo "[ERROR] Models folder not found: $SOURCE_DIR"
  echo "Drop your .onnx files into the models folder and run again."
  exit 1
fi

mkdir -p "$TARGET_DIR"
echo "Copying ONNX models to $TARGET_DIR..."
cp -R "$SOURCE_DIR"/. "$TARGET_DIR"/
echo "Done."
