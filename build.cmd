@echo off
rem Builds the single shareable NJ-Flush.exe (sound and icon embedded) into dist\.
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist dist mkdir dist
"%FW%\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /win32icon:NJ-Flush.ico /resource:flush-light.wav,flush-light.wav /resource:flush-medium.wav,flush-medium.wav /resource:flush-heavy.wav,flush-heavy.wav /resource:flush-clogged.wav,flush-clogged.wav ^
  /resource:scene-cpu-0.wav,scene-cpu-0.wav /resource:scene-cpu-1.wav,scene-cpu-1.wav /resource:scene-cpu-2.wav,scene-cpu-2.wav /resource:scene-cpu-3.wav,scene-cpu-3.wav /resource:scene-gpu-0.wav,scene-gpu-0.wav /resource:scene-gpu-1.wav,scene-gpu-1.wav /resource:scene-gpu-2.wav,scene-gpu-2.wav /resource:scene-gpu-3.wav,scene-gpu-3.wav /resource:scene-all-0.wav,scene-all-0.wav /resource:scene-all-1.wav,scene-all-1.wav /resource:scene-all-2.wav,scene-all-2.wav /resource:scene-all-3.wav,scene-all-3.wav ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"%FW%\WPF\PresentationFramework.dll" ^
  /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\WindowsBase.dll" /r:System.Xaml.dll /r:System.Management.dll ^
  /out:dist\NJ-Flush.exe /recurse:src\*.cs
