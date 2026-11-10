@echo off
rem ============================================================================
rem  RollRoll - build script
rem
rem  Usage:  build.cmd  [/clean]
rem
rem  Requires only the .NET Framework 4.x compiler shipped with Windows.
rem  No Visual Studio, no NuGet, no SDK download.
rem
rem  NOTE: This file must stay pure ASCII. cmd.exe reads .cmd using the OEM
rem        code page, so non-ASCII comments here get mangled into garbage
rem        commands. Keep it English-only.
rem ============================================================================
setlocal

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. Install .NET Framework 4.x, or edit the
    echo         CSC path at the top of this script.
    exit /b 1
)

pushd "%~dp0"

if /i "%~1"=="/clean" (
    if exist RollRoll.exe del /q RollRoll.exe
    if exist RollRoll.pdb del /q RollRoll.pdb
    echo Cleaned.
)

rem  /win32manifest declares PerMonitorV2 DPI awareness (sharp tray icon,
rem                 correct layout on 150%% displays).
rem  /warnaserror+   zero warnings expected.
echo Compiling...
"%CSC%" ^
    /nologo ^
    /target:winexe ^
    /platform:anycpu ^
    /optimize+ ^
    /warnaserror+ ^
    /codepage:65001 ^
    /win32manifest:app.manifest ^
    /out:RollRoll.exe ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    Program.cs ^
    Native.cs ^
    WindowInfo.cs ^
    Options.cs ^
    Log.cs ^
    Engines.cs ^
    ScrollController.cs ^
    TargetPicker.cs ^
    TrayIconFactory.cs ^
    UiKit.cs ^
    TrayController.cs ^
    MainForm.cs

if errorlevel 1 (
    echo.
    echo [FAILED] Build did not succeed.
    popd
    exit /b 1
)

echo [OK] RollRoll.exe
for %%F in (RollRoll.exe) do echo         %%~zF bytes

popd
endlocal
exit /b 0
