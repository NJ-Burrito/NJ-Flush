@echo off
rem Builds the single shareable NJ-Flush.exe (sound and icon embedded) into dist\.
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist dist mkdir dist
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /win32icon:NJ-Flush.ico /resource:flush-light.wav,flush-light.wav /resource:flush-medium.wav,flush-medium.wav /resource:flush-heavy.wav,flush-heavy.wav /resource:flush-clogged.wav,flush-clogged.wav ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"%FW%\WPF\PresentationFramework.dll" ^
  /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\WindowsBase.dll" /r:System.Xaml.dll /r:System.Management.dll ^
  /out:dist\NJ-Flush.exe NJFlush.cs
