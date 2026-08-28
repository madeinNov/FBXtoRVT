@echo off

echo ===================================
echo  FBXtoRVT - Package
echo ===================================
echo.

set PROJECT_DIR=%~dp0..
set DLL_SOURCE=%PROJECT_DIR%\bin\Debug\net8.0-windows
set ADDIN_PATH=%PROJECT_DIR%\manifest\FBXtoRVT.addin
set BAT_PATH=%PROJECT_DIR%\build\install.bat
set PACKAGE_DIR=%PROJECT_DIR%\build\package
set PACKAGE_DLL_DIR=%PACKAGE_DIR%\FBXtoRVT

echo DLL source : %DLL_SOURCE%
echo Addin file : %ADDIN_PATH%
echo.

if not exist "%DLL_SOURCE%\FBXtoRVT.dll" (
    echo [FAILED] FBXtoRVT.dll not found. Build the project first.
    goto END
)

if not exist "%ADDIN_PATH%" (
    echo [FAILED] FBXtoRVT.addin not found.
    goto END
)

if not exist "%BAT_PATH%" (
    echo [FAILED] install.bat not found.
    goto END
)

if exist "%PACKAGE_DIR%" (
    rmdir /S /Q "%PACKAGE_DIR%"
)
mkdir "%PACKAGE_DIR%"
mkdir "%PACKAGE_DLL_DIR%"

echo Copying DLL files to FBXtoRVT\...

for %%f in ("%DLL_SOURCE%\*.dll") do (
    copy /Y "%%f" "%PACKAGE_DLL_DIR%" > nul
    echo [OK] FBXtoRVT\%%~nxf
)

echo.
echo Copying FBXtoRVT.addin...
copy /Y "%ADDIN_PATH%" "%PACKAGE_DIR%" > nul
echo [OK] FBXtoRVT.addin

echo.
echo Copying install.bat...
copy /Y "%BAT_PATH%" "%PACKAGE_DIR%" > nul
echo [OK] install.bat

echo.
echo Package ready: %PACKAGE_DIR%
echo.
echo Contents:
dir /B "%PACKAGE_DIR%"
echo.
dir /B "%PACKAGE_DLL_DIR%"

:END
echo.
pause
