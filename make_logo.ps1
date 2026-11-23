# Renders the NJ-Flush logo (same vector art as the overlay) to logo-1024.png; make_icon.py turns it into NJ-Flush.ico / .png.
# Run: pwsh -File make_logo.ps1   (Windows, WPF; no other dependency)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$xaml = @'
<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='300' Height='300'>
  <Border CornerRadius='68' Margin='8'>
    <Border.Background><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>
      <GradientStop Color='#0B3B52' Offset='0'/><GradientStop Color='#0E7490' Offset='0.55'/><GradientStop Color='#22D3EE' Offset='1'/>
    </LinearGradientBrush></Border.Background>
  </Border>
  <Border CornerRadius='68' Margin='8' BorderThickness='3'>
    <Border.BorderBrush><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>
      <GradientStop Color='#80FFFFFF' Offset='0'/><GradientStop Color='#10FFFFFF' Offset='0.5'/><GradientStop Color='#50A5F3FC' Offset='1'/>
    </LinearGradientBrush></Border.BorderBrush>
  </Border>
  <Ellipse Width='240' Height='240' Opacity='0.5'>
    <Ellipse.Fill><RadialGradientBrush><GradientStop Color='#6022D3EE' Offset='0'/><GradientStop Color='#0022D3EE' Offset='1'/></RadialGradientBrush></Ellipse.Fill>
  </Ellipse>
  <Canvas Width='300' Height='300'>
    <!-- tank -->
    <Rectangle Canvas.Left='96' Canvas.Top='42' Width='108' Height='40' RadiusX='12' RadiusY='12'>
      <Rectangle.Fill><LinearGradientBrush StartPoint='0,0.5' EndPoint='1,0.5'>
        <GradientStop Color='#B9C6D1' Offset='0'/><GradientStop Color='#FFFFFF' Offset='0.42'/><GradientStop Color='#A7B5C1' Offset='1'/>
      </LinearGradientBrush></Rectangle.Fill>
      <Rectangle.Effect><DropShadowEffect BlurRadius='10' ShadowDepth='3' Direction='270' Opacity='0.35'/></Rectangle.Effect>
    </Rectangle>
    <Ellipse Canvas.Left='138' Canvas.Top='50' Width='24' Height='24'>
      <Ellipse.Fill><RadialGradientBrush GradientOrigin='0.35,0.3'><GradientStop Color='#CFFAFE' Offset='0'/><GradientStop Color='#0891B2' Offset='1'/></RadialGradientBrush></Ellipse.Fill>
    </Ellipse>
    <!-- bowl -->
    <Ellipse Canvas.Left='62' Canvas.Top='86' Width='176' Height='176'>
      <Ellipse.Fill><RadialGradientBrush GradientOrigin='0.35,0.28' Center='0.45,0.4' RadiusX='0.68' RadiusY='0.68'>
        <GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#EEF3F6' Offset='0.5'/><GradientStop Color='#A9B7C3' Offset='1'/>
      </RadialGradientBrush></Ellipse.Fill>
      <Ellipse.Effect><DropShadowEffect BlurRadius='22' ShadowDepth='6' Direction='270' Opacity='0.45'/></Ellipse.Effect>
    </Ellipse>
    <Ellipse Canvas.Left='80' Canvas.Top='104' Width='140' Height='140'>
      <Ellipse.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'>
        <GradientStop Color='#7E93A3' Offset='0'/><GradientStop Color='#C8D3DC' Offset='0.4'/><GradientStop Color='#EEF3F6' Offset='1'/>
      </LinearGradientBrush></Ellipse.Fill>
    </Ellipse>
    <Grid Canvas.Left='90' Canvas.Top='114' Width='120' Height='120'>
      <Grid.Clip><EllipseGeometry Center='60,60' RadiusX='60' RadiusY='60'/></Grid.Clip>
      <Ellipse><Ellipse.Fill><RadialGradientBrush GradientOrigin='0.5,0.55'>
        <GradientStop Color='#CFFAFE' Offset='0'/><GradientStop Color='#22D3EE' Offset='0.55'/><GradientStop Color='#0E7490' Offset='1'/>
      </RadialGradientBrush></Ellipse.Fill></Ellipse>
      <Canvas x:Name='Swirl' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'/>
      <Ellipse Width='30' Height='30'>
        <Ellipse.Fill><RadialGradientBrush><GradientStop Color='#020617' Offset='0'/><GradientStop Color='#0B2A3C' Offset='0.7'/><GradientStop Color='#000B2A3C' Offset='1'/></RadialGradientBrush></Ellipse.Fill>
      </Ellipse>
      <Ellipse Width='56' Height='18' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='16,16,0,0' RenderTransformOrigin='0.5,0.5'>
        <Ellipse.RenderTransform><RotateTransform Angle='-28'/></Ellipse.RenderTransform>
        <Ellipse.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#99FFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/></LinearGradientBrush></Ellipse.Fill>
      </Ellipse>
    </Grid>
  </Canvas>
</Grid>
'@
$root = [Windows.Markup.XamlReader]::Parse($xaml)
$swirl = $root.FindName('Swirl')
foreach ($i in 0..7) {
    $big = ($i % 2) -eq 0
    $p = New-Object Windows.Shapes.Path
    $p.Data = [Windows.Media.Geometry]::Parse($(if ($big) { 'M60,60 C61,40 82,24 112,30 C84,34 68,46 60,60 Z' } else { 'M60,60 C61,50 72,42 88,43 C74,47 66,52 60,60 Z' }))
    $g = New-Object Windows.Media.LinearGradientBrush
    $g.StartPoint = '0,1'; $g.EndPoint = '1,0'
    $g.GradientStops.Add((New-Object Windows.Media.GradientStop ([Windows.Media.Color]::FromArgb($(if ($big) { 240 } else { 190 }), 255, 255, 255)), 0))
    $g.GradientStops.Add((New-Object Windows.Media.GradientStop ([Windows.Media.Color]::FromArgb(20, 224, 251, 255)), 1))
    $p.Fill = $g
    $p.RenderTransform = New-Object Windows.Media.RotateTransform ($i * 45), 60, 60
    [void]$swirl.Children.Add($p)
}
$size = 1024; $scale = $size / 300
$root.LayoutTransform = New-Object Windows.Media.ScaleTransform $scale, $scale
$root.Measure([Windows.Size]::new($size, $size)); $root.Arrange([Windows.Rect]::new(0, 0, $size, $size)); $root.UpdateLayout()
$bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
$bmp.Render($root)
$enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
$out = Join-Path $PSScriptRoot 'logo-1024.png'
$fs = [IO.File]::Create($out); $enc.Save($fs); $fs.Close()
"wrote $out"
