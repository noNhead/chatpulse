@echo off
REM Builds ChatPulse into a standalone Windows executable.
REM Needs the .NET 8 SDK on THIS machine; the result does not — it carries its own runtime.
REM
REM Output: dist\ChatPulse.exe   single file, just double-click it
setlocal
cd /d "%~dp0."

echo [1/3] building...
dotnet build src\ChatPulse\ChatPulse.csproj -c Debug -v q --nologo || goto :fail

REM The icon is drawn by the app itself, so it takes one build before it can be embedded.
echo [2/3] generating icon...
src\ChatPulse\bin\Debug\net8.0-windows\ChatPulse.exe --icon src\ChatPulse\Assets\chatpulse.ico || goto :fail

echo [3/3] publishing self-contained single file...
dotnet publish src\ChatPulse\ChatPulse.csproj -c Release -o dist -v q --nologo || goto :fail

echo.
echo Done. Run: dist\ChatPulse.exe
pause
exit /b 0

:fail
echo.
echo Build failed. See the output above.
pause
exit /b 1
