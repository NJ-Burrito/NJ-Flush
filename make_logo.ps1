# Renders the NJ-Flush logo from the vector master brand\logo.svg (full) and brand\logo-small.svg (16-32 px)
# into logo-1024.png, NJ-Flush.png and NJ-Flush.ico. The art is SVG now (no WPF XAML); this is the Windows entry point.
# Run: pwsh -File make_logo.ps1   (needs uv; Chromium from PLAYWRIGHT_BROWSERS_PATH). Details: brand\README.md
$env:PYTHONUTF8 = '1'
Set-Location $PSScriptRoot
uv run --with pillow --with playwright python make_icon.py
if ($LASTEXITCODE -ne 0) { throw "make_icon.py failed ($LASTEXITCODE)" }
