@echo off

echo ===================================
echo  FBXtoRVT - Install
echo ===================================
echo.

set SOURCE_DIR=%~dp0
set ADDINS_DIR=%APPDATA%\Autodesk\Revit\Addins\2025
set DLL_DIR=%ADDINS_DIR%\FBXtoRVT

if not exist "%DLL_DIR%" (
    mkdir "%DLL_DIR%"
    echo Created: %DLL_DIR%
)

echo Copying DLL files to FBXtoRVT\...

set COPY_FAILED=0
for %%f in ("%SOURCE_DIR%FBXtoRVT\*.dll") do (
    copy /Y "%%f" "%DLL_DIR%" > nul
    if errorlevel 1 (
        echo [FAILED] %%~nxf
        set COPY_FAILED=1
    ) else (
        echo [OK] %%~nxf
    )
)

if "%COPY_FAILED%"=="1" (
    echo.
    echo Some files failed to copy. Close Revit and try again.
    goto END
)

echo.
echo Copying FBXtoRVT.addin...

copy /Y "%SOURCE_DIR%FBXtoRVT.addin" "%ADDINS_DIR%" > nul
if errorlevel 1 (
    echo [FAILED] FBXtoRVT.addin copy failed.
    goto END
)
echo [OK] FBXtoRVT.addin

echo.
echo Install complete!
echo   %ADDINS_DIR%\FBXtoRVT.addin
echo   %DLL_DIR%\

:END
echo.
pause
