@echo off
setlocal
rem Program files live in the "app" subfolder; settings.json and crash.log stay next to this bat.
cd /d "%~dp0app"
if errorlevel 1 (
  echo ERROR: the "app" folder is missing next to Start-HipAimer.bat. Please unzip the whole package again.
  pause
  exit /b 1
)

rem Already running? Start the exe again: the second instance tells the first one to show its settings window.
tasklist /FI "IMAGENAME eq TarkovHipAimer.exe" 2>nul | find /I "TarkovHipAimer.exe" >nul
if not errorlevel 1 (
  start "" "%~dp0app\TarkovHipAimer.exe"
  exit /b 0
)

rem Find the .NET Framework 4.x C# compiler that ships with Windows.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo ERROR: csc.exe from .NET Framework 4 was not found under %WINDIR%\Microsoft.NET.
  echo Build failed - please screenshot this window.
  pause
  exit /b 1
)

set "ICON="
if exist "app.ico" set "ICON=/win32icon:app.ico"

echo Building TarkovHipAimer.exe (Tarkov Hip Aimer - by Leo, v3.6) ...
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /win32manifest:app.manifest %ICON% /out:TarkovHipAimer.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll src\*.cs > build.log 2>&1
if errorlevel 1 (
  type build.log
  echo.
  echo Build failed - please screenshot this window.
  pause
  exit /b 1
)

rem "start" (ShellExecute) is required so the requireAdministrator manifest shows the UAC prompt.
start "" "%~dp0app\TarkovHipAimer.exe"
exit /b 0
