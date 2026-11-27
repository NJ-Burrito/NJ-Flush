// NJ-Flush: the RAM toilet's art, as one XAML string the overlay parses.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WS = System.Windows.Shapes;

static partial class Overlay {
    const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        WindowStyle='None' AllowsTransparency='True' Background='Transparent' Topmost='True' ShowInTaskbar='False'
        SizeToContent='WidthAndHeight' WindowStartupLocation='CenterScreen' ResizeMode='NoResize'>
  <!-- 1.2.2 (Nader: super light): the window is only as big as the art, not a full-screen layered window, and nothing is blurred
       (a DropShadowEffect on the animated canvas was re-blurred every frame). Solid (recording) mode is still full screen. -->
  <Grid x:Name='Root' Opacity='0'>
    <!-- Nader 2026-11-23: a lighter dim (not a blackout), no glow behind the toilet; solid (recording) mode = opaque + glow -->
    <Rectangle x:Name='Dim' Fill='#05080F' Opacity='0.45' Visibility='Collapsed'/>
    <Ellipse x:Name='GlowDisc' Width='1100' Height='1100' IsHitTestVisible='False' Opacity='0' Visibility='Collapsed'>
      <Ellipse.Fill><RadialGradientBrush>
        <GradientStop x:Name='Glow' Color='#3322D3EE' Offset='0'/><GradientStop Color='#0022D3EE' Offset='1'/>
      </RadialGradientBrush></Ellipse.Fill>
    </Ellipse>
    <!-- 1.2.3: fixed width, so the longer result line never widens the window and shifts the toilet right -->
    <StackPanel VerticalAlignment='Center' HorizontalAlignment='Center' Margin='40' Width='560'>
      <Viewbox x:Name='Box' Height='460' RenderTransformOrigin='0.5,0.5'>
        <Viewbox.RenderTransform><ScaleTransform x:Name='Pop' ScaleX='0.6' ScaleY='0.6'/></Viewbox.RenderTransform>
        <Canvas Width='440' Height='440'>
          <Canvas.Resources>
            <LinearGradientBrush x:Key='Porcelain' StartPoint='0,0.5' EndPoint='1,0.5'>
              <GradientStop Color='#93A6B6' Offset='0'/><GradientStop Color='#DCE6EE' Offset='0.16'/><GradientStop Color='#FFFFFF' Offset='0.36'/>
              <GradientStop Color='#EDF2F6' Offset='0.58'/><GradientStop Color='#C7D3DD' Offset='0.82'/><GradientStop Color='#8FA3B4' Offset='1'/>
            </LinearGradientBrush>
            <LinearGradientBrush x:Key='Chrome' StartPoint='0,0' EndPoint='1,1'>
              <GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#CBD5E1' Offset='0.3'/>
              <GradientStop Color='#64748B' Offset='0.55'/><GradientStop Color='#E2E8F0' Offset='0.8'/><GradientStop Color='#94A3B8' Offset='1'/>
            </LinearGradientBrush>
            <LinearGradientBrush x:Key='ChromeRev' StartPoint='1,1' EndPoint='0,0'>
              <GradientStop Color='#334155' Offset='0'/><GradientStop Color='#94A3B8' Offset='0.4'/><GradientStop Color='#E2E8F0' Offset='0.75'/><GradientStop Color='#FFFFFF' Offset='1'/>
            </LinearGradientBrush>
            <LinearGradientBrush x:Key='ChromeH' StartPoint='0,0.5' EndPoint='1,0.5'>
              <GradientStop Color='#475569' Offset='0'/><GradientStop Color='#E2E8F0' Offset='0.2'/><GradientStop Color='#FFFFFF' Offset='0.38'/>
              <GradientStop Color='#94A3B8' Offset='0.62'/><GradientStop Color='#CBD5E1' Offset='0.85'/><GradientStop Color='#475569' Offset='1'/>
            </LinearGradientBrush>
            <LinearGradientBrush x:Key='ChromeV' StartPoint='0.5,0' EndPoint='0.5,1'>
              <GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#CBD5E1' Offset='0.35'/><GradientStop Color='#64748B' Offset='0.7'/><GradientStop Color='#A3B1C2' Offset='1'/>
            </LinearGradientBrush>
            <RadialGradientBrush x:Key='Shadow'><GradientStop Color='#99000000' Offset='0'/><GradientStop Color='#00000000' Offset='1'/></RadialGradientBrush>
            <LinearGradientBrush x:Key='Leaf' StartPoint='0,0.5' EndPoint='1,0.5'>
              <GradientStop Color='#86EFAC' Offset='0'/><GradientStop Color='#22C55E' Offset='0.45'/><GradientStop Color='#15803D' Offset='0.8'/><GradientStop Color='#14532D' Offset='1'/>
            </LinearGradientBrush>
          </Canvas.Resources>

          <!-- the niche: a soft shadow ring, then the room clipped to a rounded rectangle -->
          <Rectangle Canvas.Left='6' Canvas.Top='10' Width='428' Height='428' RadiusX='30' RadiusY='30' Fill='#26000000'/>
          <Rectangle Canvas.Left='3' Canvas.Top='6' Width='434' Height='434' RadiusX='33' RadiusY='33' Fill='#16000000'/>
          <Canvas Width='440' Height='440'>
            <Canvas.Clip><RectangleGeometry Rect='8,8,424,424' RadiusX='26' RadiusY='26'/></Canvas.Clip>

            <!-- WALL (static, cached): tiles, light from the top left, dado stripe, toilet shadow, holder and shelf -->
            <Canvas Width='440' Height='440'>
              <Canvas.CacheMode><BitmapCache RenderAtScale='1.5'/></Canvas.CacheMode>
              <Rectangle Canvas.Left='8' Canvas.Top='8' Width='424' Height='382'>
                <Rectangle.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='0.35,1'>
                  <GradientStop Color='#4E7A96' Offset='0'/><GradientStop Color='#365B75' Offset='0.5'/><GradientStop Color='#1E3448' Offset='1'/>
                </LinearGradientBrush></Rectangle.Fill>
              </Rectangle>
              <Canvas x:Name='Tiles'/>
              <Path Data='M40,8 L170,8 L300,380 L180,380 Z'>
                <Path.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'><GradientStop Color='#2EFFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/></LinearGradientBrush></Path.Fill>
              </Path>
              <Path Data='M200,8 L250,8 L380,380 L330,380 Z'>
                <Path.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'><GradientStop Color='#1CFFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/></LinearGradientBrush></Path.Fill>
              </Path>
              <Ellipse Canvas.Left='-90' Canvas.Top='-130' Width='470' Height='360'>
                <Ellipse.Fill><RadialGradientBrush><GradientStop Color='#40FFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/></RadialGradientBrush></Ellipse.Fill>
              </Ellipse>
              <Rectangle Canvas.Left='8' Canvas.Top='308' Width='424' Height='13'>
                <Rectangle.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'>
                  <GradientStop Color='#7FEFE3' Offset='0'/><GradientStop Color='#1DB7A6' Offset='0.5'/><GradientStop Color='#0B6F68' Offset='1'/>
                </LinearGradientBrush></Rectangle.Fill>
              </Rectangle>
              <Rectangle Canvas.Left='8' Canvas.Top='308' Width='424' Height='1.6' Fill='#B8FFFFFF'/>
              <Rectangle Canvas.Left='8' Canvas.Top='321' Width='424' Height='2' Fill='#55041A22'/>
              <Canvas x:Name='Dado'/>
              <Rectangle Canvas.Left='260' Canvas.Top='8' Width='172' Height='372'>
                <Rectangle.Fill><LinearGradientBrush StartPoint='0,0.5' EndPoint='1,0.5'><GradientStop Color='#00000000' Offset='0'/><GradientStop Color='#70000000' Offset='1'/></LinearGradientBrush></Rectangle.Fill>
              </Rectangle>
              <Rectangle Canvas.Left='8' Canvas.Top='8' Width='424' Height='72'>
                <Rectangle.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'><GradientStop Color='#70000000' Offset='0'/><GradientStop Color='#00000000' Offset='1'/></LinearGradientBrush></Rectangle.Fill>
              </Rectangle>
              <!-- the toilet's shadow on the wall, thrown right and down (light is top left) -->
              <Ellipse Canvas.Left='112' Canvas.Top='60' Width='262' Height='116' Fill='{StaticResource Shadow}' Opacity='0.55'/>
              <Ellipse Canvas.Left='108' Canvas.Top='146' Width='268' Height='276' Fill='{StaticResource Shadow}' Opacity='0.6'/>
              <!-- toilet-paper holder (the paper tail is animated, below) -->
              <Ellipse Canvas.Left='34' Canvas.Top='234' Width='60' Height='10' Fill='{StaticResource Shadow}' Opacity='0.7'/>
              <Rectangle Canvas.Left='20' Canvas.Top='194' Width='11' Height='42' RadiusX='3.5' RadiusY='3.5' Fill='{StaticResource ChromeH}'/>
              <Rectangle Canvas.Left='28' Canvas.Top='208' Width='58' Height='6' RadiusX='3' RadiusY='3' Fill='{StaticResource ChromeV}'/>
              <Rectangle Canvas.Left='38' Canvas.Top='186' Width='40' Height='46' RadiusX='5' RadiusY='5'>
                <Rectangle.Fill><LinearGradientBrush StartPoint='0,0.5' EndPoint='1,0.5'>
                  <GradientStop Color='#B4BDC8' Offset='0'/><GradientStop Color='#F8FAFC' Offset='0.3'/><GradientStop Color='#FFFFFF' Offset='0.45'/><GradientStop Color='#DDE4EB' Offset='0.8'/><GradientStop Color='#A8B3BF' Offset='1'/>
                </LinearGradientBrush></Rectangle.Fill>
              </Rectangle>
              <Ellipse Canvas.Left='32' Canvas.Top='186' Width='13' Height='46' Fill='{StaticResource Porcelain}'/>
              <Ellipse Canvas.Left='35' Canvas.Top='198' Width='7' Height='22' Fill='#8896A4'/>
              <Rectangle Canvas.Left='46' Canvas.Top='189' Width='3' Height='40' RadiusX='1.5' RadiusY='1.5' Fill='#CCFFFFFF'/>
              <!-- plant shelf -->
              <Ellipse Canvas.Left='348' Canvas.Top='214' Width='84' Height='14' Fill='{StaticResource Shadow}' Opacity='0.7'/>
              <Path Data='M356,226 L368,226 L356,242 Z' Fill='{StaticResource ChromeV}'/>
              <Path Data='M408,226 L420,226 L420,242 Z' Fill='{StaticResource ChromeV}'/>
              <Rectangle Canvas.Left='344' Canvas.Top='216' Width='80' Height='9' RadiusX='3' RadiusY='3'>
                <Rectangle.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'><GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#E2E8F0' Offset='0.4'/><GradientStop Color='#8C9BAA' Offset='1'/></LinearGradientBrush></Rectangle.Fill>
              </Rectangle>
              <Path Data='M368,216 L400,216 L396,191 L372,191 Z'>
                <Path.Fill><LinearGradientBrush StartPoint='0,0.5' EndPoint='1,0.5'><GradientStop Color='#9A3412' Offset='0'/><GradientStop Color='#FB923C' Offset='0.35'/><GradientStop Color='#EA580C' Offset='0.7'/><GradientStop Color='#7C2D12' Offset='1'/></LinearGradientBrush></Path.Fill>
              </Path>
              <Rectangle Canvas.Left='366' Canvas.Top='186' Width='36' Height='8' RadiusX='3' RadiusY='3'>
                <Rectangle.Fill><LinearGradientBrush StartPoint='0,0.5' EndPoint='1,0.5'><GradientStop Color='#C2410C' Offset='0'/><GradientStop Color='#FDBA74' Offset='0.35'/><GradientStop Color='#C2410C' Offset='0.8'/><GradientStop Color='#7C2D12' Offset='1'/></LinearGradientBrush></Rectangle.Fill>
              </Rectangle>
              <Rectangle Canvas.Left='368' Canvas.Top='187' Width='32' Height='1.5' RadiusX='0.7' RadiusY='0.7' Fill='#AAFFFFFF'/>
            </Canvas>

            <!-- paper tail and leaves: the only wall props that move (ambient, a breath of air) -->
            <Path Canvas.Left='0' Canvas.Top='0' Data='M44,230 L72,230 C74,246 68,256 74,270 L58,268 C62,256 54,246 44,230 Z'>
              <Path.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='1,0'><GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#F1F5F9' Offset='0.5'/><GradientStop Color='#C4CDD6' Offset='1'/></LinearGradientBrush></Path.Fill>
              <Path.RenderTransform><RotateTransform x:Name='TpRot' CenterX='58' CenterY='230'/></Path.RenderTransform>
            </Path>
            <Canvas x:Name='Leaves'>
              <Canvas.RenderTransform><RotateTransform x:Name='LeafRot' CenterX='384' CenterY='190'/></Canvas.RenderTransform>
              <Path Data='M384,190 C370,176 364,158 372,142 C382,154 388,172 384,190 Z' Fill='{StaticResource Leaf}'/>
              <Path Data='M384,190 C398,176 408,158 402,142 C390,154 384,172 384,190 Z' Fill='{StaticResource Leaf}'/>
              <Path Data='M384,190 C379,170 381,150 384,130 C389,150 391,170 384,190 Z' Fill='{StaticResource Leaf}'/>
              <Path Data='M384,190 C366,188 356,178 352,166 C366,168 378,176 384,190 Z' Fill='{StaticResource Leaf}'/>
              <Path Data='M384,190 C402,188 412,178 416,166 C402,168 390,176 384,190 Z' Fill='{StaticResource Leaf}'/>
              <Path Data='M383,186 C381,170 382,154 384,138' Stroke='#99FFFFFF' StrokeThickness='1.2' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
            </Canvas>

            <!-- FLOOR (static, cached): skirting, floor tiles, sheen, and the toilet's contact shadows -->
            <Canvas Width='440' Height='440'>
              <Canvas.CacheMode><BitmapCache RenderAtScale='1.5'/></Canvas.CacheMode>
              <Rectangle Canvas.Left='8' Canvas.Top='388' Width='424' Height='44'>
                <Rectangle.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'><GradientStop Color='#3A546E' Offset='0'/><GradientStop Color='#22364A' Offset='0.5'/><GradientStop Color='#0C1624' Offset='1'/></LinearGradientBrush></Rectangle.Fill>
              </Rectangle>
              <Canvas x:Name='FloorLines'/>
              <Ellipse Canvas.Left='40' Canvas.Top='392' Width='300' Height='34'>
                <Ellipse.Fill><RadialGradientBrush><GradientStop Color='#30FFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/></RadialGradientBrush></Ellipse.Fill>
              </Ellipse>
              <Rectangle Canvas.Left='8' Canvas.Top='378' Width='424' Height='11'>
                <Rectangle.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'><GradientStop Color='#EEF3F7' Offset='0'/><GradientStop Color='#B6C3CE' Offset='0.55'/><GradientStop Color='#6F8294' Offset='1'/></LinearGradientBrush></Rectangle.Fill>
              </Rectangle>
              <Rectangle Canvas.Left='8' Canvas.Top='378.5' Width='424' Height='1.6' Fill='#CCFFFFFF'/>
              <Ellipse Canvas.Left='96' Canvas.Top='398' Width='270' Height='34' Fill='{StaticResource Shadow}'/>
              <Ellipse Canvas.Left='126' Canvas.Top='405' Width='200' Height='20' Fill='{StaticResource Shadow}'/>
              <Ellipse Canvas.Left='156' Canvas.Top='408' Width='132' Height='12' Fill='{StaticResource Shadow}'/>
            </Canvas>

            <!-- THE TOILET (shakes as one) -->
            <Canvas x:Name='Toilet' Width='300' Height='440'>
              <Canvas.RenderTransform><TransformGroup>
                <ScaleTransform CenterX='150' CenterY='412' ScaleX='0.88' ScaleY='0.88'/>
                <TranslateTransform X='70'/>
                <TranslateTransform x:Name='Shake'/>
              </TransformGroup></Canvas.RenderTransform>

              <!-- static porcelain, cached: pedestal, tank, lid, button recess, bowl, seat, inner wall -->
              <Canvas Width='300' Height='440'>
                <Canvas.CacheMode><BitmapCache RenderAtScale='1.5'/></Canvas.CacheMode>
                <Path Data='M92,320 C100,365 92,392 78,412 L222,412 C208,392 200,365 208,320 Z' Fill='{StaticResource Porcelain}'/>
                <Path Data='M100,336 C104,364 100,388 92,404' Stroke='#B3FFFFFF' StrokeThickness='3' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
                <Path Data='M203,338 C199,364 204,388 212,404' Stroke='#887DD3FC' StrokeThickness='2.5' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
                <Rectangle Canvas.Left='66' Canvas.Top='402' Width='168' Height='12' RadiusX='6' RadiusY='6'>
                  <Rectangle.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'><GradientStop Color='#F8FAFC' Offset='0'/><GradientStop Color='#C3CFD9' Offset='0.5'/><GradientStop Color='#7C8B98' Offset='1'/></LinearGradientBrush></Rectangle.Fill>
                </Rectangle>
                <Rectangle Canvas.Left='74' Canvas.Top='403' Width='152' Height='1.6' RadiusX='0.8' RadiusY='0.8' Fill='#D0FFFFFF'/>

                <Rectangle Canvas.Left='46' Canvas.Top='16' Width='208' Height='96' RadiusX='18' RadiusY='18' Fill='{StaticResource Porcelain}'/>
                <Rectangle Canvas.Left='46' Canvas.Top='16' Width='208' Height='44' RadiusX='18' RadiusY='18'>
                  <Rectangle.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'><GradientStop Color='#70FFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/></LinearGradientBrush></Rectangle.Fill>
                </Rectangle>
                <Rectangle Canvas.Left='46' Canvas.Top='84' Width='208' Height='28' RadiusX='14' RadiusY='14'>
                  <Rectangle.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'><GradientStop Color='#00475569' Offset='0'/><GradientStop Color='#3C475569' Offset='1'/></LinearGradientBrush></Rectangle.Fill>
                </Rectangle>
                <Rectangle Canvas.Left='54' Canvas.Top='30' Width='4' Height='62' RadiusX='2' RadiusY='2' Fill='#CCFFFFFF'/>
                <Rectangle Canvas.Left='242' Canvas.Top='28' Width='4' Height='64' RadiusX='2' RadiusY='2' Fill='#9990D0FB'/>
                <Rectangle Canvas.Left='56' Canvas.Top='104' Width='188' Height='10' RadiusX='5' RadiusY='5' Fill='#7C8B98' Opacity='0.45'/>
                <Rectangle Canvas.Left='40' Canvas.Top='24' Width='220' Height='10' RadiusX='5' RadiusY='5'>
                  <Rectangle.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'><GradientStop Color='#66000000' Offset='0'/><GradientStop Color='#00000000' Offset='1'/></LinearGradientBrush></Rectangle.Fill>
                </Rectangle>
                <Rectangle Canvas.Left='36' Canvas.Top='4' Width='228' Height='24' RadiusX='12' RadiusY='12'>
                  <Rectangle.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'>
                    <GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#EEF3F7' Offset='0.45'/><GradientStop Color='#C4D0DA' Offset='0.85'/><GradientStop Color='#A5B4C1' Offset='1'/>
                  </LinearGradientBrush></Rectangle.Fill>
                </Rectangle>
                <Rectangle Canvas.Left='54' Canvas.Top='8' Width='192' Height='3' RadiusX='1.5' RadiusY='1.5' Fill='White' Opacity='0.95'/>
                <Rectangle Canvas.Left='252' Canvas.Top='9' Width='8' Height='14' RadiusX='4' RadiusY='4' Fill='#8893C5FD'/>

                <Ellipse Canvas.Left='122' Canvas.Top='38' Width='56' Height='56' Fill='{StaticResource Chrome}'/>
                <Ellipse Canvas.Left='126' Canvas.Top='42' Width='48' Height='48' Fill='{StaticResource ChromeRev}'/>

                <!-- bowl: shell with rim light, seat ring, inner wall -->
                <Ellipse Canvas.Left='20' Canvas.Top='112' Width='260' Height='300'>
                  <Ellipse.Fill><RadialGradientBrush GradientOrigin='0.33,0.26' Center='0.45,0.4' RadiusX='0.72' RadiusY='0.72'>
                    <GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#F4F7FA' Offset='0.45'/><GradientStop Color='#CBD6DF' Offset='0.8'/><GradientStop Color='#96A9B8' Offset='1'/>
                  </RadialGradientBrush></Ellipse.Fill>
                </Ellipse>
                <Path Data='M270,300 C274,352 242,402 190,411' Stroke='#887DD3FC' StrokeThickness='3.2' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
                <Path Data='M31,236 C29,286 47,342 82,382' Stroke='#B0FFFFFF' StrokeThickness='4' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
                <Ellipse Canvas.Left='33' Canvas.Top='124' Width='234' Height='276' StrokeThickness='15'>
                  <Ellipse.Stroke><LinearGradientBrush StartPoint='0.15,0' EndPoint='0.85,1'>
                    <GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#EEF3F7' Offset='0.3'/><GradientStop Color='#C8D4DE' Offset='0.65'/><GradientStop Color='#94A6B5' Offset='1'/>
                  </LinearGradientBrush></Ellipse.Stroke>
                </Ellipse>
                <Path Data='M36,238 A117,138 0 0 1 110,132' Stroke='#EEFFFFFF' StrokeThickness='3.2' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
                <Path Data='M262,310 A117,138 0 0 1 172,398' Stroke='#665B6B7C' StrokeThickness='4' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
                <Ellipse Canvas.Left='52' Canvas.Top='142' Width='196' Height='244'>
                  <Ellipse.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'>
                    <GradientStop Color='#6B8294' Offset='0'/><GradientStop Color='#A9BCCB' Offset='0.28'/><GradientStop Color='#DCE6ED' Offset='0.62'/><GradientStop Color='#F6F9FB' Offset='1'/>
                  </LinearGradientBrush></Ellipse.Fill>
                </Ellipse>
                <Ellipse Canvas.Left='52' Canvas.Top='142' Width='196' Height='244'>
                  <Ellipse.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>
                    <GradientStop Color='#55000000' Offset='0'/><GradientStop Color='#00000000' Offset='0.45'/><GradientStop Color='#00000000' Offset='1'/>
                  </LinearGradientBrush></Ellipse.Fill>
                </Ellipse>
              </Canvas>

              <!-- the rubber duck on the tank (secondary action: it hops on every press and wobbles through the flush) -->
              <Canvas Canvas.Left='194' Canvas.Top='-34' Width='52' Height='40'>
                <Canvas.RenderTransform><TransformGroup>
                  <ScaleTransform x:Name='DuckS' CenterX='20' CenterY='38'/>
                  <RotateTransform x:Name='DuckR' CenterX='20' CenterY='38'/>
                  <TranslateTransform x:Name='DuckT'/>
                </TransformGroup></Canvas.RenderTransform>
                <Ellipse Canvas.Left='-2' Canvas.Top='34' Width='46' Height='8' Fill='{StaticResource Shadow}'/>
                <Path Data='M6,28 C-4,25 -7,17 -3,11 C2,16 7,21 12,23 Z'><Path.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'><GradientStop Color='#FDE047' Offset='0'/><GradientStop Color='#CA8A04' Offset='1'/></LinearGradientBrush></Path.Fill></Path>
                <Ellipse Canvas.Left='2' Canvas.Top='14' Width='38' Height='25'>
                  <Ellipse.Fill><RadialGradientBrush GradientOrigin='0.3,0.25'><GradientStop Color='#FFF9C4' Offset='0'/><GradientStop Color='#FDE047' Offset='0.4'/><GradientStop Color='#EAB308' Offset='0.8'/><GradientStop Color='#A16207' Offset='1'/></RadialGradientBrush></Ellipse.Fill>
                </Ellipse>
                <Path Data='M12,25 C18,18 29,20 32,28 C27,33 16,34 12,25 Z' Stroke='#99A16207' StrokeThickness='0.8'><Path.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'><GradientStop Color='#FEF08A' Offset='0'/><GradientStop Color='#CA8A04' Offset='1'/></LinearGradientBrush></Path.Fill></Path>
                <Ellipse Canvas.Left='22' Canvas.Top='0' Width='20' Height='20'>
                  <Ellipse.Fill><RadialGradientBrush GradientOrigin='0.3,0.25'><GradientStop Color='#FFF9C4' Offset='0'/><GradientStop Color='#FDE047' Offset='0.4'/><GradientStop Color='#EAB308' Offset='0.85'/><GradientStop Color='#A16207' Offset='1'/></RadialGradientBrush></Ellipse.Fill>
                </Ellipse>
                <Path Data='M38,10 C44,8 49,10 50,12.5 C47,16 42,15.5 38,14.5 Z'><Path.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#FDBA74' Offset='0'/><GradientStop Color='#F97316' Offset='0.6'/><GradientStop Color='#C2410C' Offset='1'/></LinearGradientBrush></Path.Fill></Path>
                <Ellipse Canvas.Left='32.5' Canvas.Top='6' Width='4.2' Height='5' Fill='#1F2937'/>
                <Ellipse Canvas.Left='33.6' Canvas.Top='6.6' Width='1.7' Height='1.7' Fill='White'/>
                <Ellipse Canvas.Left='30' Canvas.Top='11' Width='4.5' Height='3' Fill='#80FB7185'/>
                <Ellipse Canvas.Left='25' Canvas.Top='2' Width='9' Height='4' Fill='#B0FFFFFF'/>
                <Ellipse Canvas.Left='8' Canvas.Top='17' Width='14' Height='5' Fill='#99FFFFFF'/>
              </Canvas>

              <!-- chrome flush button: it squashes on press -->
              <Canvas>
                <Canvas.RenderTransform><ScaleTransform x:Name='BtnScale' CenterX='150' CenterY='66'/></Canvas.RenderTransform>
                <Ellipse Canvas.Left='129' Canvas.Top='45' Width='42' Height='42'>
                  <Ellipse.Fill><RadialGradientBrush GradientOrigin='0.35,0.3'>
                    <GradientStop x:Name='BtnHi' Color='#CFFAFE' Offset='0'/><GradientStop x:Name='BtnLo' Color='#0891B2' Offset='1'/>
                  </RadialGradientBrush></Ellipse.Fill>
                </Ellipse>
                <Ellipse Canvas.Left='136' Canvas.Top='48.5' Width='18' Height='10' Fill='White' Opacity='0.78'/>
                <Ellipse Canvas.Left='156' Canvas.Top='76' Width='9' Height='5' Fill='White' Opacity='0.3'/>
              </Canvas>

              <!-- water: base, dark edge, meniscus, ripples, caustics, drain, swirl, bubbles, highlights -->
              <Grid Canvas.Left='64' Canvas.Top='158' Width='172' Height='216' RenderTransformOrigin='0.5,0.5'>
                <Grid.RenderTransform><ScaleTransform x:Name='WaterLvl'/></Grid.RenderTransform>
                <Grid.Clip><EllipseGeometry Center='86,108' RadiusX='86' RadiusY='108'/></Grid.Clip>
                <Ellipse><Ellipse.Fill><RadialGradientBrush GradientOrigin='0.42,0.4'>
                  <GradientStop x:Name='WaterIn' Color='#CFFAFE' Offset='0'/><GradientStop x:Name='WaterMid' Color='#22D3EE' Offset='0.55'/>
                  <GradientStop x:Name='WaterOut' Color='#0E7490' Offset='1'/>
                </RadialGradientBrush></Ellipse.Fill></Ellipse>
                <Ellipse Stroke='#5A05303E' StrokeThickness='14'/>
                <Ellipse Margin='10' Stroke='#66FFFFFF' StrokeThickness='1.2'/>
                <Canvas x:Name='Ripples' Width='172' Height='216'/>
                <Canvas x:Name='Caustics' Width='172' Height='216'/>
                <Ellipse Width='46' Height='46' RenderTransformOrigin='0.5,0.5'>
                  <Ellipse.Fill><RadialGradientBrush>
                    <GradientStop Color='#020617' Offset='0'/><GradientStop Color='#0B2A3C' Offset='0.7'/><GradientStop Color='#000B2A3C' Offset='1'/>
                  </RadialGradientBrush></Ellipse.Fill>
                  <Ellipse.RenderTransform><ScaleTransform x:Name='DrainScale' ScaleX='0.3' ScaleY='0.3'/></Ellipse.RenderTransform>
                </Ellipse>
                <Canvas x:Name='Swirl' Width='172' Height='216'>
                  <Canvas.RenderTransform><TransformGroup>
                    <ScaleTransform x:Name='SwScale' CenterX='86' CenterY='108'/>
                    <RotateTransform x:Name='SwRot' CenterX='86' CenterY='108'/>
                  </TransformGroup></Canvas.RenderTransform>
                </Canvas>
                <Canvas x:Name='Bubbles' Width='172' Height='216'/>
                <Path Data='M30,58 C42,34 92,20 138,34 C100,38 62,46 30,58 Z'>
                  <Path.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'><GradientStop Color='#BBFFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/></LinearGradientBrush></Path.Fill>
                </Path>
                <Ellipse Width='10' Height='6' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='128,52,0,0' Fill='#C8FFFFFF'/>
                <Ellipse Width='70' Height='34' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='86,166,0,0' RenderTransformOrigin='0.5,0.5'>
                  <Ellipse.RenderTransform><RotateTransform Angle='-30'/></Ellipse.RenderTransform>
                  <Ellipse.Fill><RadialGradientBrush><GradientStop Color='#40FFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/></RadialGradientBrush></Ellipse.Fill>
                </Ellipse>
              </Grid>
              <!-- the flush's rim wash: a bright dashed ring that whirls round the bowl -->
              <Ellipse x:Name='RimWash' Canvas.Left='57' Canvas.Top='147' Width='186' Height='234' Stroke='#F2FFFFFF' StrokeThickness='4' StrokeDashArray='4 3' Opacity='0'/>
              <Ellipse Canvas.Left='52' Canvas.Top='142' Width='196' Height='244' StrokeThickness='2' Opacity='0.7'>
                <Ellipse.Stroke><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'>
                  <GradientStop Color='#40FFFFFF' Offset='0'/><GradientStop Color='#FFFFFFFF' Offset='1'/>
                </LinearGradientBrush></Ellipse.Stroke>
              </Ellipse>

              <!-- clogged: the water rising to the rim, murky, with gunk afloat -->
              <Canvas x:Name='Rise' Canvas.Left='56' Canvas.Top='146' Width='188' Height='236' Opacity='0' RenderTransformOrigin='0.5,0.5'>
                <Canvas.RenderTransform><ScaleTransform x:Name='RiseScale' ScaleX='0.8' ScaleY='0.8'/></Canvas.RenderTransform>
                <Ellipse Width='188' Height='236' Stroke='#FECACA' StrokeThickness='3'>
                  <Ellipse.Fill><RadialGradientBrush GradientOrigin='0.4,0.35'>
                    <GradientStop Color='#E6FCA5A5' Offset='0'/><GradientStop Color='#E6EF4444' Offset='0.5'/><GradientStop Color='#EBB91C1C' Offset='0.85'/><GradientStop Color='#F07F1D1D' Offset='1'/>
                  </RadialGradientBrush></Ellipse.Fill>
                </Ellipse>
                <Ellipse Canvas.Left='10' Canvas.Top='12' Width='168' Height='212' Stroke='#66FFFFFF' StrokeThickness='1.2'/>
                <Ellipse Canvas.Left='58' Canvas.Top='150' Width='38' Height='17' RenderTransformOrigin='0.5,0.5' Fill='#7A4A2A'><Ellipse.RenderTransform><RotateTransform Angle='-18'/></Ellipse.RenderTransform></Ellipse>
                <Ellipse Canvas.Left='62' Canvas.Top='151' Width='24' Height='8' Fill='#A9714A' Opacity='0.7'/>
                <Ellipse Canvas.Left='112' Canvas.Top='118' Width='28' Height='13' RenderTransformOrigin='0.5,0.5' Fill='#5E3B22'><Ellipse.RenderTransform><RotateTransform Angle='22'/></Ellipse.RenderTransform></Ellipse>
                <Ellipse Canvas.Left='84' Canvas.Top='186' Width='24' Height='11' Fill='#6B4426'/>
                <Path Data='M26,66 C42,34 96,22 148,40 C102,44 62,54 26,66 Z'>
                  <Path.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'><GradientStop Color='#CCFFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/></LinearGradientBrush></Path.Fill>
                </Path>
                <Ellipse Canvas.Left='142' Canvas.Top='56' Width='11' Height='6' Fill='#D8FFFFFF'/>
              </Canvas>

              <!-- plunger -->
              <Canvas x:Name='Plunger' Canvas.Left='108' Canvas.Top='92' Opacity='0'>
                <Canvas.RenderTransform><TranslateTransform x:Name='PlY' Y='-540'/></Canvas.RenderTransform>
                <Rectangle Canvas.Left='35' Canvas.Top='46' Width='14' Height='100' RadiusX='7' RadiusY='7'>
                  <Rectangle.Fill><LinearGradientBrush StartPoint='0,0.5' EndPoint='1,0.5'>
                    <GradientStop Color='#7A4B20' Offset='0'/><GradientStop Color='#FBBF24' Offset='0.22'/><GradientStop Color='#D97706' Offset='0.45'/>
                    <GradientStop Color='#92400E' Offset='0.78'/><GradientStop Color='#5E3416' Offset='1'/>
                  </LinearGradientBrush></Rectangle.Fill>
                </Rectangle>
                <Rectangle Canvas.Left='39' Canvas.Top='56' Width='1.5' Height='76' Fill='#78350F' Opacity='0.35'/>
                <Rectangle Canvas.Left='38' Canvas.Top='50' Width='2.6' Height='80' RadiusX='1.3' RadiusY='1.3' Fill='#FDE68A' Opacity='0.55'/>
                <Rectangle Canvas.Left='31' Canvas.Top='126' Width='22' Height='16' RadiusX='3' RadiusY='3' Fill='{StaticResource ChromeH}'/>
                <Ellipse Canvas.Left='8' Canvas.Top='184' Width='68' Height='13' Fill='{StaticResource Shadow}' Opacity='0.6'/>
                <Canvas Canvas.Top='140'>
                  <Canvas.RenderTransform><ScaleTransform x:Name='Cup' CenterX='42' CenterY='50'/></Canvas.RenderTransform>
                  <Path Data='M2,44 C2,10 22,0 42,0 C62,0 82,10 82,44 Z'>
                    <Path.Fill><RadialGradientBrush GradientOrigin='0.32,0.22' Center='0.45,0.4' RadiusX='0.7' RadiusY='0.8'>
                      <GradientStop Color='#FECACA' Offset='0'/><GradientStop Color='#EF4444' Offset='0.35'/><GradientStop Color='#B91C1C' Offset='0.7'/><GradientStop Color='#7F1D1D' Offset='1'/>
                    </RadialGradientBrush></Path.Fill>
                  </Path>
                  <Ellipse Canvas.Left='0' Canvas.Top='37' Width='84' Height='13' Stroke='#99FCA5A5' StrokeThickness='1'>
                    <Ellipse.Fill><LinearGradientBrush StartPoint='0,0.5' EndPoint='1,0.5'><GradientStop Color='#991B1B' Offset='0'/><GradientStop Color='#DC2626' Offset='0.4'/><GradientStop Color='#7F1D1D' Offset='1'/></LinearGradientBrush></Ellipse.Fill>
                  </Ellipse>
                  <Ellipse Canvas.Left='16' Canvas.Top='7' Width='22' Height='11' Fill='White' Opacity='0.55'/>
                  <Ellipse Canvas.Left='60' Canvas.Top='20' Width='8' Height='14' Fill='#44FFFFFF'/>
                </Canvas>
              </Canvas>

              <!-- button spark on press contact, and the end-of-flush glints -->
              <Path x:Name='Spark' Canvas.Left='150' Canvas.Top='66' Data='M0,-15 L3.4,-3.4 L15,0 L3.4,3.4 L0,15 L-3.4,3.4 L-15,0 L-3.4,-3.4 Z' Fill='#FFFFFF' Opacity='0'>
                <Path.RenderTransform><ScaleTransform x:Name='SparkS'/></Path.RenderTransform>
              </Path>
              <Canvas x:Name='Glints'/>
            </Canvas>

            <!-- foreground: floor puddle, splash drops, alarm vignette, flash -->
            <Ellipse x:Name='Puddle' Canvas.Left='70' Canvas.Top='410' Width='300' Height='28' RenderTransformOrigin='0.5,0.5'>
              <Ellipse.Fill><RadialGradientBrush GradientOrigin='0.4,0.4'>
                <GradientStop x:Name='PudA' Color='#9038BDF8' Offset='0'/><GradientStop x:Name='PudB' Color='#4C38BDF8' Offset='0.7'/><GradientStop Color='#0038BDF8' Offset='1'/>
              </RadialGradientBrush></Ellipse.Fill>
              <Ellipse.RenderTransform><ScaleTransform x:Name='PuddleS' ScaleX='0' ScaleY='0'/></Ellipse.RenderTransform>
            </Ellipse>
            <Canvas x:Name='Stink' Opacity='0'/>
            <Canvas x:Name='Splash' Canvas.Left='220' Canvas.Top='276'/>
            <Rectangle x:Name='Alarm' Canvas.Left='8' Canvas.Top='8' Width='424' Height='424' RadiusX='26' RadiusY='26' Opacity='0' IsHitTestVisible='False'>
              <Rectangle.Fill><RadialGradientBrush><GradientStop Color='#00DC2626' Offset='0'/><GradientStop Color='#00DC2626' Offset='0.6'/><GradientStop Color='#A8DC2626' Offset='1'/></RadialGradientBrush></Rectangle.Fill>
            </Rectangle>
            <Rectangle x:Name='Flash' Canvas.Left='8' Canvas.Top='8' Width='424' Height='424' RadiusX='26' RadiusY='26' Fill='White' Opacity='0' IsHitTestVisible='False'/>
          </Canvas>
        </Canvas>
      </Viewbox>
      <Border x:Name='Pill' HorizontalAlignment='Center' Margin='0,22,0,0' Padding='16,5' CornerRadius='15' BorderThickness='1'>
        <TextBlock x:Name='Tier' FontFamily='Thmanyah Sans, Segoe UI' FontSize='15' FontWeight='Bold'/>
      </Border>
      <Border Background='#D90B1220' CornerRadius='14' Padding='18,6' Margin='0,10,0,0' HorizontalAlignment='Center'>
      <TextBlock x:Name='Msg' Text='Flushing RAM...' Foreground='White' FontFamily='Thmanyah Sans, Segoe UI' FontSize='26'
                 HorizontalAlignment='Center' TextAlignment='Center' TextWrapping='Wrap' RenderTransformOrigin='0.5,0.5'>
        <TextBlock.RenderTransform><ScaleTransform x:Name='MsgScale'/></TextBlock.RenderTransform>
      </TextBlock>
      </Border>
    </StackPanel>
  </Grid>
</Window>";
}
