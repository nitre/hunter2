@echo off
rem Start hunter2: build the C# (quick when nothing changed), then run the game in a window.
rem Set GODOT to your Godot 4.7.2 .NET executable, or put it on PATH as godot.exe.
cd /d "%~dp0godot"
dotnet build --nologo -v q
if errorlevel 1 (
  echo Build failed - nothing started.
  pause
  exit /b 1
)
rem the desktop shortcut's icon (the soldier's face) comes from the game data: make it again if it is missing
if not exist "%~dp0game_data\hunters_loading.ico" python -I "%~dp0tools\make_icon.py"
if "%GODOT%"=="" set GODOT=godot.exe
start "" "%GODOT%" --path "%~dp0godot"
