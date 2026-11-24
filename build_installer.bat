@echo off
setlocal

cd /d "%~dp0"

echo ============================================================================
echo   DONG GOI BO CAI DAT SYSMONITOR WIDGET (TU DONG DONG BO PHIEN BAN .CSPROJ)
echo ============================================================================

REM 1. Tu dong doc so phien ban tu <Version> trong WinHardwareMonitor.csproj
set "APP_VERSION="
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "$xml = [xml](Get-Content 'WinHardwareMonitor.csproj'); ($xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1).Trim()"`) do (
    set "APP_VERSION=%%v"
)

if "%APP_VERSION%"=="" (
    echo [LOI] Khong doc duoc the Version trong WinHardwareMonitor.csproj
    exit /b 1
)

echo [1/3] Phien ban doc tu .csproj: %APP_VERSION%

REM 2. Bien dich va xuat ban Single-File Self-Contained Release (tich hop san .NET 8)
echo [2/3] Dang bien dich ban Release (win-x64, Self-Contained Single-File)...
dotnet publish WinHardwareMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 (
    echo [LOI] Bien dich that bai
    exit /b 1
)

REM 3. Tim trinh bien dich Inno Setup 6 (ISCC.exe)
set "ISCC_EXE="
set "PF86=%ProgramFiles(x86)%"
if exist "%PF86%\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%PF86%\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE (
    for /f "delims=" %%i in ('where ISCC.exe 2^>nul') do set "ISCC_EXE=%%i"
)

if not defined ISCC_EXE (
    echo [LOI] Khong tim thay Inno Setup 6 ISCC.exe
    echo Hay cai dat Inno Setup 6 bang lenh: winget install JRSoftware.InnoSetup --scope user
    exit /b 1
)

echo [3/3] Dang dong goi Setup.exe bang Inno Setup: "%ISCC_EXE%"
"%ISCC_EXE%" /DMyAppVersion=%APP_VERSION% "installer\setup.iss"
if errorlevel 1 (
    echo [LOI] Dong goi Inno Setup that bai
    exit /b 1
)

echo.
echo ============================================================================
echo   HOAN TAT! Tep cai dat da duoc xuat tai:
echo   %cd%\Release_Installer\SysMonitor-Widget-Setup-%APP_VERSION%.exe
echo ============================================================================
endlocal
exit /b 0
