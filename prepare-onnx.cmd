@echo off
setlocal EnableDelayedExpansion

REM prepare-onnx.cmd
REM Script to prepare ONNX models after publish for distribution.
REM Run this script after dotnet publish to prepare ONNX models for packaging.
REM
REM Usage: prepare-onnx.cmd [output-directory]
REM   output-directory: Optional. Where to copy ONNX models. Default: .\ONNX
REM
REM Supported models:
REM   - SAM2 (Segment Anything Model 2)
REM   - MicroSAM
REM   - Grounding DINO
REM   - MiDaS (Depth estimation)

set "SCRIPT_DIR=%~dp0"
set "ONNX_SOURCE_DIR=%SCRIPT_DIR%ONNX"
set "ONNX_TEMPLATE_DIR=%SCRIPT_DIR%InstallerPackager\Assets\onnx-installer"

if "%~1"=="" (
    set "OUTPUT_DIR=%ONNX_SOURCE_DIR%"
) else (
    set "OUTPUT_DIR=%~1"
)

echo ==========================================
echo   ONNX Model Preparation Script
echo ==========================================
echo.

REM Create output directory if it doesn't exist
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

echo Source directory: %ONNX_SOURCE_DIR%
echo Output directory: %OUTPUT_DIR%
echo.

REM Count ONNX files
set ONNX_COUNT=0
for %%f in ("%ONNX_SOURCE_DIR%\*.onnx") do (
    if exist "%%f" set /a ONNX_COUNT+=1
)

if %ONNX_COUNT% gtr 0 (
    echo Found %ONNX_COUNT% ONNX model(s^) in source directory.
    echo.
    echo Models found:
    for %%f in ("%ONNX_SOURCE_DIR%\*.onnx") do (
        if exist "%%f" echo   %%~nxf
    )
    echo.
) else (
    echo No ONNX models found in %ONNX_SOURCE_DIR%
    echo.
    echo To use this script:
    echo   1. Download ONNX models manually or using the URLs below
    echo   2. Place them in: %ONNX_SOURCE_DIR%
    echo   3. Run this script again
    echo.
    echo Common ONNX model sources:
    echo   - SAM2: https://github.com/facebookresearch/segment-anything-2
    echo   - MicroSAM: https://github.com/computational-cell-analytics/micro-sam
    echo   - MiDaS: https://github.com/isl-org/MiDaS
    echo   - Grounding DINO: https://github.com/IDEA-Research/GroundingDINO
    echo.
)

REM Prepare installer template with models
set "TEMPLATE_MODELS_DIR=%ONNX_TEMPLATE_DIR%\models"
if not exist "%TEMPLATE_MODELS_DIR%" mkdir "%TEMPLATE_MODELS_DIR%"

if %ONNX_COUNT% gtr 0 (
    echo Copying ONNX models to installer template...
    xcopy "%ONNX_SOURCE_DIR%\*.onnx" "%TEMPLATE_MODELS_DIR%\" /Y /Q 2>nul

    REM Also copy any subdirectory structure
    for /d %%d in ("%ONNX_SOURCE_DIR%\*") do (
        set "subdir=%%~nxd"
        if not "!subdir!"=="" (
            if not exist "%TEMPLATE_MODELS_DIR%\!subdir!" mkdir "%TEMPLATE_MODELS_DIR%\!subdir!"
            xcopy "%%d\*" "%TEMPLATE_MODELS_DIR%\!subdir!\" /E /Y /Q 2>nul
        )
    )

    echo.
    echo Models copied to installer template successfully!
)

REM Summary
echo.
echo ==========================================
echo   Summary
echo ==========================================
echo.
echo ONNX models directory: %ONNX_SOURCE_DIR%
echo Installer template: %ONNX_TEMPLATE_DIR%
echo.

if %ONNX_COUNT% gtr 0 (
    echo Status: Ready for packaging
    echo.
    echo Next steps:
    echo   1. Run the packager: dotnet run --project InstallerPackager
    echo   2. The ONNX models will be included in the installer packages
) else (
    echo Status: No models found - add .onnx files to %ONNX_SOURCE_DIR%
)

echo.
echo Done.
endlocal
