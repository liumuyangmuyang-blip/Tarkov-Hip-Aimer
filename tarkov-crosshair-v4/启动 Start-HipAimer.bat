@echo off
setlocal
rem Tarkov Hip Aimer v4.0 - by Leo. Launcher: builds the program with the C# compiler that ships with Windows and starts it.
rem This file is ASCII only on purpose: cmd.exe reads .bat files in the console code page (936 on Chinese Windows),
rem so it never spells the Chinese folder name. It finds the program folder by its English ending: "* app".
rem settings.json, crash.log and the crosshair picture folder stay next to this bat.

set "ROOT=%~dp0"
set "APPDIR="
for /d %%D in ("%ROOT%* app") do if not defined APPDIR if exist "%%~fD\src\Program.cs" set "APPDIR=%%~fD"
if not defined APPDIR if exist "%ROOT%app\src\Program.cs" set "APPDIR=%ROOT%app"
if not defined APPDIR goto :noapp

cd /d "%APPDIR%"
if errorlevel 1 goto :noapp

rem Already running? Start the exe again: the second instance tells the first one to show its settings window.
tasklist /FI "IMAGENAME eq TarkovHipAimer.exe" 2>nul | find /I "TarkovHipAimer.exe" >nul
if errorlevel 1 goto :build
if not exist "TarkovHipAimer.exe" goto :build
start "" "%APPDIR%\TarkovHipAimer.exe"
exit /b 0

:build
rem Find the .NET Framework 4.x C# compiler that ships with Windows.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto :nocsc
rem The program needs .NET Framework 4.7 or newer (release 460798+; Windows 10 1703+ and Windows 11 have it built in).
rem If the registry value cannot be read, do not block: csc will report the real problem.
set "NETREL="
for /f "tokens=3" %%R in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| find /I "Release"') do set /a NETREL=%%R
if defined NETREL if %NETREL% LSS 460798 goto :oldnet
if not exist "app.manifest" goto :nofile
if not exist "src\Program.cs" goto :nofile

set "ICON="
if exist "app.ico" set "ICON=/win32icon:app.ico"

echo Building TarkovHipAimer.exe (Tarkov Hip Aimer - by Leo, v4.0) ...
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /win32manifest:app.manifest %ICON% /out:TarkovHipAimer.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll src\*.cs > build.log 2>&1
if errorlevel 1 goto :buildfail
if not exist "TarkovHipAimer.exe" goto :buildfail

rem "start" (ShellExecute) is required so the requireAdministrator manifest shows the UAC prompt.
start "" "%APPDIR%\TarkovHipAimer.exe"
exit /b 0

:noapp
echo.
echo ERROR: the program folder (the folder whose name ends with " app") was not found next to this bat.
echo If you double-clicked inside the zip: right-click the zip, choose "Extract All", then run the bat in the extracted folder.
echo Otherwise please unzip the whole package again.
echo.
pause
exit /b 1

:nocsc
echo.
echo ERROR: csc.exe from .NET Framework 4 was not found under %WINDIR%\Microsoft.NET.
echo Build failed - please screenshot this window.
echo.
pause
exit /b 1

:oldnet
echo.
echo ERROR: this PC has an old .NET Framework 4 (release %NETREL%). Tarkov Hip Aimer v4.0 needs .NET Framework 4.7 or newer.
echo Please install .NET Framework 4.8 from Microsoft (search: ".NET Framework 4.8 download") or update Windows, then run this bat again.
echo.
pause
exit /b 1

:nofile
echo.
echo ERROR: app.manifest or src\Program.cs is missing in the program folder. Please unzip the whole package again.
echo.
pause
exit /b 1

:buildfail
if exist "build.log" type "build.log"
echo.
echo Build failed - please screenshot this window.
echo.
pause
exit /b 1
