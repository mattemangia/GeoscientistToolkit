@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "SOURCE_DIR=%SCRIPT_DIR%models"
set "TARGET_DIR=%SCRIPT_DIR%..\ONNX"

if not exist "%SOURCE_DIR%" (
  echo [ERROR] Models folder not found: "%SOURCE_DIR%"
  echo Drop your .onnx files into the models folder and run again.
  exit /b 1
)

if not exist "%TARGET_DIR%" (
  mkdir "%TARGET_DIR%"
)

echo Copying ONNX models to "%TARGET_DIR%"...
xcopy "%SOURCE_DIR%\*" "%TARGET_DIR%\" /E /I /Y >nul
echo Done.
