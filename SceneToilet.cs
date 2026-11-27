// NJ-Flush: the RAM flush animation, the toilet in four tiers (Light, Medium, Heavy, Clogged), and its sound cues.
// 1.3 rework: the toilet stands in a tiled bathroom niche (subway-tile wall lit from the top left, floor, skirting, a toilet-paper holder,
// a potted plant, a rubber duck on the tank). All art is hand-authored vector drawing in code (no third-party assets, no raster, no emoji).
// Static groups are BitmapCache'd; only transforms, opacity, clips and dash offsets animate; no blur or drop-shadow effects anywhere.
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

// Overlay: four flush tiers by RAM in use when the flush starts (Nader 2026-11-23): 70 light, 80 medium, 90 heavy, 96 clogged.
// Motion built on nj-ui-motion-laws (L1 squash, L2 anticipation, L3 one motion at a time, L5 follow-through, L6 easing, L7 arcs,
// L8 secondary action, L10 exaggerated feedback, L26 peak-end) and nj-animation-direction (beats = pressure changes, holds never frozen).
// Sound (nj-real-sound): one premixed soundtrack per tier from real Mixkit recordings (make_sfx.py), started with the clock, so every
// sound sits on its cue. Change a cue time here -> change it in make_sfx.py.
// Windows "Show animations" off -> no motion, the tier and the result still show (reduced motion keeps the feedback).
static class Overlay {
    public static int FPS = 30;
    public static bool Solid;  // opaque backdrop: screen recordings never show the desktop behind
    [DllImport("winmm.dll")] static extern int waveOutSetVolume(IntPtr device, uint volume);  // this process's volume (SoundPlayer)
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

    static readonly string[] Names = { "LIGHT FLUSH", "MEDIUM FLUSH", "HEAVY FLUSH", "CLOGGED" };
    static readonly string[] Files = { "flush-light.wav", "flush-medium.wav", "flush-heavy.wav", "flush-clogged.wav" };
    static readonly string[] Accents = { "#22D3EE", "#2DD4BF", "#FB923C", "#F87171" };
    static readonly int[] PreviewPct = { 72, 84, 93, 97 };
    // water and button colours: { in, mid, out }
    static readonly string[] Clean = { "#CFFAFE", "#22D3EE", "#0E7490" }, Murky = { "#FFEDD5", "#FB923C", "#9A3412" }, Red = { "#FEE2E2", "#F87171", "#7F1D1D" };

    // ponytail: fixed thresholds (Nader 2026-11-23); under 70% still plays the light flush
    public static int TierOf(double pct) { return pct >= 96 ? 3 : pct >= 90 ? 2 : pct >= 80 ? 1 : 0; }

    static WA.EasingFunctionBase E(WA.EasingFunctionBase e, WA.EasingMode m) { e.EasingMode = m; return e; }
    static readonly WA.IEasingFunction QIn = E(new WA.QuadraticEase(), WA.EasingMode.EaseIn), QInOut = E(new WA.QuadraticEase(), WA.EasingMode.EaseInOut),
        QOut = E(new WA.QuadraticEase(), WA.EasingMode.EaseOut),
        CIn = E(new WA.CubicEase(), WA.EasingMode.EaseIn), COut = E(new WA.CubicEase(), WA.EasingMode.EaseOut),
        Back = new WA.BackEase { Amplitude = 0.5, EasingMode = WA.EasingMode.EaseOut };

    // Nader 2026-11-23: "it took time" -> every duration and cue runs at 60% of the designed length (make_sfx.py SPEED matches)
    public const double Speed = 0.6;
    static TimeSpan Ms(double ms) { return TimeSpan.FromMilliseconds(ms * Speed); }
    static WA.DoubleAnimation A(double? from, double to, int ms, WA.IEasingFunction e) {
        var a = new WA.DoubleAnimation { To = to, Duration = Ms(ms), EasingFunction = e };
        if (from.HasValue) a.From = from.Value;
        return a;
    }
    // keyframes as (ms, value) pairs, eased in-out
    static WA.DoubleAnimationUsingKeyFrames K(params double[] tv) {
        var k = new WA.DoubleAnimationUsingKeyFrames();
        for (int i = 0; i < tv.Length; i += 2) k.KeyFrames.Add(new WA.EasingDoubleKeyFrame(tv[i + 1], WA.KeyTime.FromTimeSpan(Ms(tv[i])), QInOut));
        return k;
    }
    static WA.DoubleAnimationUsingKeyFrames KD(double delayMs, params double[] tv) { var k = K(tv); k.BeginTime = Ms(delayMs); return k; }
    static void S2(WM.ScaleTransform s, WA.AnimationTimeline a) { s.BeginAnimation(WM.ScaleTransform.ScaleXProperty, a); s.BeginAnimation(WM.ScaleTransform.ScaleYProperty, a); }
    static WM.Color C(string hex) { return (WM.Color)WM.ColorConverter.ConvertFromString(hex); }
    static WM.Color Alpha(string hex, byte a) { var c = C(hex); c.A = a; return c; }
    static WM.SolidColorBrush Brs(string hex, byte a) { return new WM.SolidColorBrush(Alpha(hex, a)); }
    static void Tint(WM.GradientStop s, string hex, int ms) { s.BeginAnimation(WM.GradientStop.ColorProperty, new WA.ColorAnimation(C(hex), Ms(ms))); }
    static void Shake(WM.TranslateTransform t, double amp, int times) {
        t.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(-amp, amp, Ms(45)) {
            AutoReverse = true, RepeatBehavior = new WA.RepeatBehavior(times), FillBehavior = WA.FillBehavior.Stop });
    }
    static WM.Brush Grad(W.Point a, W.Point b, params WM.Color[] cs) {
        var g = new WM.LinearGradientBrush { StartPoint = a, EndPoint = b };
        for (int i = 0; i < cs.Length; i++) g.GradientStops.Add(new WM.GradientStop(cs[i], i / (double)(cs.Length - 1)));
        return g;
    }
    static string F(double d) { return d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
    static T At<T>(T e, double x, double y) where T : W.UIElement { WC.Canvas.SetLeft(e, x); WC.Canvas.SetTop(e, y); return e; }
    static WS.Path Pth(string d, WM.Brush fill) { return new WS.Path { Data = WM.Geometry.Parse(d), Fill = fill }; }
    static WS.Path Ln(string d, WM.Brush stroke, double th) {
        return new WS.Path { Data = WM.Geometry.Parse(d), Stroke = stroke, StrokeThickness = th, StrokeStartLineCap = WM.PenLineCap.Round, StrokeEndLineCap = WM.PenLineCap.Round };
    }
    static WM.RadialGradientBrush Rad(double ox, double oy, params object[] st) {
        var g = new WM.RadialGradientBrush { GradientOrigin = new W.Point(ox, oy) };
        for (int i = 0; i + 1 < st.Length; i += 2) g.GradientStops.Add(new WM.GradientStop(C((string)st[i + 1]), Convert.ToDouble(st[i])));
        return g;
    }

    // tapered swirl arms (filled crescents, bright at the centre, fading out) and three fine spiral threads between them
    static void AddArms(WC.Canvas swirl) {
        foreach (int i in Enumerable.Range(0, 8)) {
            bool big = i % 2 == 0;
            swirl.Children.Add(new WS.Path {
                Data = WM.Geometry.Parse(big ? "M86,108 C88,76 118,52 162,60 C122,64 98,82 86,108 Z" : "M86,108 C88,92 104,80 126,82 C106,87 94,96 86,108 Z"),
                Fill = Grad(new W.Point(0, 1), new W.Point(1, 0), Alpha("#FFFFFF", (byte)(big ? 235 : 190)), Alpha("#E0FBFF", 20)),
                RenderTransform = new WM.RotateTransform(i * 45, 86, 108)
            });
        }
        for (int a = 0; a < 3; a++) {
            var sb = new StringBuilder();
            for (int i = 0; i <= 28; i++) {
                double ang = i * 0.3 + a * 2.094, r = 8 + i * 3.1;
                sb.Append(i == 0 ? "M" : " L").Append(F(86 + Math.Cos(ang) * r)).Append(",").Append(F(108 + Math.Sin(ang) * r * 1.1));
            }
            swirl.Children.Add(Ln(sb.ToString(), Brs("#E0F7FF", 150), 1.8));
        }
    }

    // the wall tiles (subway bond), dado diamonds and floor lines, drawn once into cached groups
    static void BuildSet(Func<string, object> n) {
        var tiles = (WC.Canvas)n("Tiles"); var rnd = new Random(11);
        var grout = new StringBuilder(); var hi = new StringBuilder(); var gloss = new StringBuilder(); var lite = new StringBuilder(); var dark = new StringBuilder();
        const double tw = 53, th = 31;
        for (int r = 0; r < 12; r++) {
            double y = 8 + r * th, off = (r % 2) * tw / 2;
            if (r > 0) { grout.Append("M8," + F(y) + " L432," + F(y) + " "); hi.Append("M8," + F(y + 1.3) + " L432," + F(y + 1.3) + " "); }
            for (int k = 0; k < 9; k++) {
                double x = 8 + off + k * tw;
                if (x <= 10 || x >= 430) continue;
                grout.Append("M" + F(x) + "," + F(y) + " L" + F(x) + "," + F(y + th) + " "); hi.Append("M" + F(x + 1.3) + "," + F(y + 1.3) + " L" + F(x + 1.3) + "," + F(y + th) + " ");
            }
        }
        for (int k = 0; k < 44; k++) {
            int r = rnd.Next(0, 12); double off = (r % 2) * tw / 2, x = 8 + off + rnd.Next(-1, 8) * tw, y = 8 + r * th;
            if (x < 8 || x + tw > 433) continue;
            (rnd.Next(0, 2) == 0 ? lite : dark).Append("M" + F(x) + "," + F(y) + " h" + F(tw) + " v" + F(th) + " h" + F(-tw) + " Z ");
            if (k % 3 == 0) gloss.Append("M" + F(x + 8) + "," + F(y + th - 2) + " L" + F(x + 22) + "," + F(y + 3) + " L" + F(x + 31) + "," + F(y + 3) + " L" + F(x + 17) + "," + F(y + th - 2) + " Z ");
        }
        tiles.Children.Add(Pth(lite.ToString(), Brs("#FFFFFF", 12)));
        tiles.Children.Add(Pth(dark.ToString(), Brs("#000000", 16)));
        tiles.Children.Add(Pth(gloss.ToString(), Brs("#FFFFFF", 20)));
        tiles.Children.Add(Ln(hi.ToString(), Brs("#FFFFFF", 34), 1));
        tiles.Children.Add(Ln(grout.ToString(), Brs("#071420", 150), 1.4));

        var dado = (WC.Canvas)n("Dado"); var dia = new StringBuilder();
        for (int k = 0; k < 20; k++) { double x = 14 + k * 21.5; dia.Append("M" + F(x) + ",314.5 l5,-4.5 l5,4.5 l-5,4.5 Z "); }
        dado.Children.Add(Pth(dia.ToString(), Brs("#FFFFFF", 80)));

        var fl = (WC.Canvas)n("FloorLines"); var fsb = new StringBuilder("M8,404 L432,404 M8,419 L432,419 ");
        for (int k = -8; k <= 8; k++) fsb.Append("M" + F(220 + k * 30) + ",389 L" + F(220 + k * 72) + ",432 ");
        fl.Children.Add(Ln(fsb.ToString(), Brs("#06101C", 150), 1.2));
        fl.Children.Add(Ln(fsb.ToString().Replace("M8,404 L432,404 M8,419 L432,419 ", "M8,405.2 L432,405.2 M8,420.2 L432,420.2 "), Brs("#FFFFFF", 22), 1));
    }

    class Bub { public WC.Canvas Body; public WM.TranslateTransform T; public WM.ScaleTransform S; public WS.Ellipse Ring; public WM.ScaleTransform RS; public double X; }

    // preview: "light" | "medium" | "heavy" | "clogged" plays that tier without flushing anything
    public static void Run(string preview, Flush job) {
        var ms = new Mem.MemStatus(); Mem.Status(ref ms);
        double pct = ms.total > 0 ? (ms.total - ms.avail) * 100.0 / ms.total : 0;
        int tier = TierOf(pct);
        string result = null;
        Thread work = null;
        if (preview != null) {
            int i = Array.FindIndex(Names, x => x.StartsWith(preview.ToUpperInvariant()));
            tier = i >= 0 ? i : 1; pct = PreviewPct[tier];
            result = Tx.T("ov.preview");
        } else {
            // 1.2.5 (Nader): the Explorer restart happens while the overlay is up; the overlay stays until the whole flush is done
            work = new Thread(() => { result = job.Run(); }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        }

        // 1.2.2 (super light): animations tick at FPS, not WPF's default 60
        WA.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(WA.Timeline), new W.FrameworkPropertyMetadata { DefaultValue = FPS });
        var win = (W.Window)W.Markup.XamlReader.Parse(Xaml);
        Func<string, object> n = win.FindName;
        BuildSet(n);
        var root = (W.UIElement)n("Root");
        var pop = (WM.ScaleTransform)n("Pop"); var btn = (WM.ScaleTransform)n("BtnScale"); var shake = (WM.TranslateTransform)n("Shake");
        var drain = (WM.ScaleTransform)n("DrainScale"); var swScale = (WM.ScaleTransform)n("SwScale"); var swRot = (WM.RotateTransform)n("SwRot");
        var lvl = (WM.ScaleTransform)n("WaterLvl");
        var water = new[] { (WM.GradientStop)n("WaterIn"), (WM.GradientStop)n("WaterMid"), (WM.GradientStop)n("WaterOut") };
        var btnHi = (WM.GradientStop)n("BtnHi"); var btnLo = (WM.GradientStop)n("BtnLo");
        var rise = (W.UIElement)n("Rise"); var riseScale = (WM.ScaleTransform)n("RiseScale");
        var plunger = (W.UIElement)n("Plunger"); var plY = (WM.TranslateTransform)n("PlY"); var cup = (WM.ScaleTransform)n("Cup");
        var bubbles = (WC.Canvas)n("Bubbles"); var splash = (WC.Canvas)n("Splash");
        var ripples = (WC.Canvas)n("Ripples"); var caustics = (WC.Canvas)n("Caustics");
        var rim = (WS.Ellipse)n("RimWash"); var spark = (W.UIElement)n("Spark"); var sparkS = (WM.ScaleTransform)n("SparkS");
        var duckS = (WM.ScaleTransform)n("DuckS"); var duckR = (WM.RotateTransform)n("DuckR"); var duckT = (WM.TranslateTransform)n("DuckT");
        var glints = (WC.Canvas)n("Glints"); var puddle = (W.UIElement)n("Puddle"); var puddleS = (WM.ScaleTransform)n("PuddleS");
        var alarm = (W.UIElement)n("Alarm"); var flash = (W.UIElement)n("Flash"); var stink = (WC.Canvas)n("Stink");
        var tpRot = (WM.RotateTransform)n("TpRot"); var leafRot = (WM.RotateTransform)n("LeafRot");
        var msg = (WC.TextBlock)n("Msg"); var msgScale = (WM.ScaleTransform)n("MsgScale");
        var tierText = (WC.TextBlock)n("Tier"); var pill = (WC.Border)n("Pill");
        AddArms((WC.Canvas)n("Swirl"));
        if (Solid) {  // recordings: full screen, opaque backdrop and glow
            foreach (string k in new[] { "Dim", "GlowDisc" }) { var u = (W.UIElement)n(k); u.Visibility = W.Visibility.Visible; u.Opacity = 1; }
            win.SizeToContent = W.SizeToContent.Manual; win.WindowState = W.WindowState.Maximized;
        }
        bool render = Scenes.RenderDir != null;   // /render light|medium|heavy|clogged <dir>: off screen, no sound, frames + a sheet
        if (render) {
            win.WindowStartupLocation = W.WindowStartupLocation.Manual; win.Left = -32000; win.Top = 0; win.Topmost = false; win.ShowActivated = false;
            ((WC.Grid)root).Background = new WM.SolidColorBrush(C("#05080F"));
        }
        msg.Text = Tx.T("ov.ram");
        if (tier == 3) msg.Text = Tx.T("ov.pipes"); else if (job != null && job.Req.Deep) msg.Text = Tx.T("ov.deep");
        ((WC.Grid)root).FlowDirection = Tx.Flow;                                           // the pill and the lines read from the right in Arabic
        ((WC.Viewbox)n("Box")).FlowDirection = W.FlowDirection.LeftToRight;              // the drawing is never mirrored
        bool motion = W.SystemParameters.ClientAreaAnimation;

        string accent = Accents[tier];
        tierText.Text = Tx.F("pill.tier", Tx.TierPill(tier), pct);
        tierText.Foreground = new WM.SolidColorBrush(C(accent));
        pill.Background = new WM.SolidColorBrush(C("#E00B1220"));  // dark pill: readable on any desktop
        pill.BorderBrush = new WM.SolidColorBrush(Alpha(accent, 0x88));
        ((WM.GradientStop)n("Glow")).Color = Alpha(accent, 0x40);
        string[] btnCol = tier == 2 ? Murky : tier == 3 ? Red : Clean;
        btnHi.Color = C(btnCol[0]); btnLo.Color = C(btnCol[2]);
        Action<string[], int> tintWater = (cols, t) => { for (int i = 0; i < 3; i++) Tint(water[i], cols[i], t); };
        string dropCol = tier == 2 ? "#FDBA74" : tier == 3 ? "#FCA5A5" : "#7DD3FC";
        if (tier >= 2) {   // the floor puddle takes the colour of the water that spills
            ((WM.GradientStop)n("PudA")).Color = Alpha(tier == 2 ? "#FB923C" : "#DC2626", 0x90); ((WM.GradientStop)n("PudB")).Color = Alpha(tier == 2 ? "#FB923C" : "#B91C1C", 0x4C);
        }

        // ambient life: ripples on the surface, caustic light, the paper tail and the leaves in a breath of air (the bowl is never frozen)
        var impact = new System.Collections.Generic.List<Tuple<WS.Ellipse, WM.ScaleTransform>>();
        for (int i = 0; i < 3; i++) {
            var sc = new WM.ScaleTransform(0.2, 0.2, 86, 108);
            var e = new WS.Ellipse { Width = 172, Height = 216, Stroke = new WM.SolidColorBrush(Alpha("#FFFFFF", 150)), StrokeThickness = 2, Opacity = 0, RenderTransform = sc };
            ripples.Children.Add(e);
            if (!motion) continue;
            var begin = Ms(i * 900);
            sc.BeginAnimation(WM.ScaleTransform.ScaleXProperty, new WA.DoubleAnimation(0.2, 1, Ms(2700)) { BeginTime = begin, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = COut });
            sc.BeginAnimation(WM.ScaleTransform.ScaleYProperty, new WA.DoubleAnimation(0.2, 1, Ms(2700)) { BeginTime = begin, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = COut });
            e.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(0.55, 0, Ms(2700)) { BeginTime = begin, RepeatBehavior = WA.RepeatBehavior.Forever });
        }
        for (int i = 0; i < 3; i++) {   // impact rings (press contact, plunger contact), driven by cues
            var sc = new WM.ScaleTransform(0.1, 0.1, 86, 108);
            var e = new WS.Ellipse { Width = 172, Height = 216, Stroke = new WM.SolidColorBrush(Alpha("#FFFFFF", 220)), StrokeThickness = 2.6, Opacity = 0, RenderTransform = sc };
            ripples.Children.Add(e); impact.Add(Tuple.Create(e, sc));
        }
        string[] cau = { "M26,74 C46,58 70,90 94,74 S134,62 150,78", "M30,126 C54,112 76,140 100,124 S138,114 152,132", "M44,168 C62,154 82,178 104,164 S128,156 138,168", "M58,96 C74,86 88,104 104,94", "M22,150 C34,142 46,156 60,148" };
        for (int i = 0; i < cau.Length; i++) {
            var tt = new WM.TranslateTransform();
            var p = Ln(cau[i], Brs("#FFFFFF", (byte)(110 - i * 10)), 1.4 + (i % 2) * 0.6); p.RenderTransform = tt; caustics.Children.Add(p);
            if (motion) {
                tt.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(-5, 6, Ms(1300 + i * 260)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
                tt.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(-2, 3, Ms(1700 + i * 190)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
            }
        }
        if (motion) {
            tpRot.BeginAnimation(WM.RotateTransform.AngleProperty, new WA.DoubleAnimation(-3.5, 3.5, Ms(1500)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
            leafRot.BeginAnimation(WM.RotateTransform.AngleProperty, new WA.DoubleAnimation(-2.5, 2.5, Ms(2100)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
        }
        int ringIdx = 0;
        Action ring = () => {
            var r = impact[ringIdx++ % 3];
            S2(r.Item2, A(0.1, 1, 700, COut)); r.Item1.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0.95, 700, 0));
        };

        // droplets (L8 secondary action) fly out on arcs (L7), thrown from the bowl: a pool of 16, used n at a time
        var drops = new System.Collections.Generic.List<Tuple<WS.Path, WM.TranslateTransform>>();
        for (int i = 0; i < 16; i++) {
            var t = new WM.TranslateTransform();
            var tf = new WM.TransformGroup(); double sz = 0.7 + i % 3 * 0.22; tf.Children.Add(new WM.ScaleTransform(sz, sz)); tf.Children.Add(t);
            var e = new WS.Path {
                Data = WM.Geometry.Parse("M5,0 C5,0 10,7 10,11 A5,5 0 1 1 0,11 C0,7 5,0 5,0 Z"), Opacity = 0, RenderTransform = tf,
                Fill = Rad(0.35, 0.6, 0.0, "#FFFFFF", 1.0, dropCol)
            };
            splash.Children.Add(e); drops.Add(Tuple.Create(e, t));
        }
        // the crown of water on a big burst
        var crownS = new WM.ScaleTransform(0.3, 0.1, 0, 6);
        var crown = Pth("M-50,6 C-44,-14 -38,-38 -32,-52 C-26,-36 -18,-22 -13,-6 C-11,-28 -6,-56 0,-76 C6,-56 11,-28 13,-6 C18,-22 26,-36 32,-52 C38,-38 44,-14 50,6 Z",
            Grad(new W.Point(0, 0), new W.Point(0, 1), Alpha("#FFFFFF", 0xF2), Alpha(dropCol, 0xB8)));
        crown.Opacity = 0; crown.RenderTransform = crownS; crown.Stroke = Brs("#FFFFFF", 150); crown.StrokeThickness = 1;
        splash.Children.Add(At(crown, 0, -12));
        Action<int, double, double> burst = (cnt, spread, up) => {
            for (int i = 0; i < cnt && i < drops.Count; i++) {
                double f = cnt < 2 ? 0 : (i / (double)(cnt - 1)) * 2 - 1;
                double dx = f * spread * (0.75 + 0.25 * ((i * 7) % 4) / 3.0), pk = up * (0.65 + 0.35 * ((i * 5) % 3) / 2.0);
                int dur = 640 + (i % 4) * 70;
                var y = new WA.DoubleAnimationUsingKeyFrames();
                y.KeyFrames.Add(new WA.EasingDoubleKeyFrame(-pk, WA.KeyTime.FromTimeSpan(Ms(dur * 0.42)), COut));
                y.KeyFrames.Add(new WA.EasingDoubleKeyFrame(60, WA.KeyTime.FromTimeSpan(Ms(dur)), QIn));
                drops[i].Item2.BeginAnimation(WM.TranslateTransform.YProperty, y);
                drops[i].Item2.BeginAnimation(WM.TranslateTransform.XProperty, A(0, dx, dur, COut));
                drops[i].Item1.BeginAnimation(W.UIElement.OpacityProperty, K(0, 1, dur * 0.7, 1, dur, 0));
            }
        };
        Action bigBurst = () => {
            burst(16, 135, 105);
            S2(crownS, K(0, 0.3, 150, 1.35, 300, 1.2));
            crownS.BeginAnimation(WM.ScaleTransform.ScaleYProperty, K(0, 0.1, 150, 1.7, 300, 1.4, 520, 0.3));
            crown.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 40, 0.6, 300, 0.5, 520, 0));
            flash.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 50, tier == 3 ? 0.42 : 0.3, 420, 0));
            Shake(shake, 3, 6);
        };

        // clogged: glossy bubbles rise through the red water and pop on the sound cues (eight pops, one failed-flush glug)
        var bubs = new System.Collections.Generic.List<Bub>();
        double[] bx = { 52, 108, 78, 124, 44, 96, 64, 114, 86 };
        for (int i = 0; i < 9; i++) {
            double d = i == 8 ? 34 : 13 + (i % 3) * 5;
            var body = new WC.Canvas { Width = d, Height = d, Opacity = 0, RenderTransformOrigin = new W.Point(0.5, 0.5) };
            var s = new WM.ScaleTransform(0.3, 0.3); var tr = new WM.TranslateTransform(0, 150); var tg = new WM.TransformGroup(); tg.Children.Add(s); tg.Children.Add(tr); body.RenderTransform = tg;
            var b = new WS.Ellipse { Width = d, Height = d, Stroke = Brs("#FFFFFF", 210), StrokeThickness = 1.2, Fill = Rad(0.32, 0.28, 0.0, "#44FFFFFF", 0.6, "#26FECACA", 1.0, "#88FFFFFF") };
            body.Children.Add(b); body.Children.Add(At(new WS.Ellipse { Width = d * 0.34, Height = d * 0.22, Fill = Brs("#FFFFFF", 235) }, d * 0.2, d * 0.14));
            var rs = new WM.ScaleTransform(0.4, 0.4);
            var rg = new WS.Ellipse { Width = d * 1.6, Height = d * 1.6, Stroke = Brs("#FFFFFF", 230), StrokeThickness = 1.6, Opacity = 0, RenderTransformOrigin = new W.Point(0.5, 0.5), RenderTransform = rs };
            double x = i == 8 ? 86 - d / 2 : bx[i];
            bubbles.Children.Add(At(body, x, 0)); bubbles.Children.Add(At(rg, x - d * 0.3, 70 - d * 0.3));
            bubs.Add(new Bub { Body = body, T = tr, S = s, Ring = rg, RS = rs, X = x });
        }
        // a bubble swells from the bottom, reaches the surface and pops exactly `rise` designed ms after it is started
        Action<int, int, double> popBub = (idx, rdur, topY) => {
            var b = bubs[idx]; b.Body.Opacity = 1;
            b.T.BeginAnimation(WM.TranslateTransform.YProperty, A(b.Body.Height > 30 ? 100 : 190, topY, rdur, QIn));
            S2(b.S, K(0, 0.35, rdur, 1, rdur + 36, 1.35, rdur + 90, 0.2));
            b.Body.BeginAnimation(W.UIElement.OpacityProperty, K(0, 1, rdur, 1, rdur + 50, 0));
            WC.Canvas.SetTop(b.Ring, topY + b.Body.Height * 0.5 - b.Ring.Height * 0.5 + 0);
            S2(b.RS, KD(rdur, 0, 0.5, 330, 1.5));
            b.Ring.BeginAnimation(W.UIElement.OpacityProperty, KD(rdur, 0, 0.95, 40, 0.8, 330, 0));
        };

        // clogged: red murk; steam-stink wisps on the heavy tier
        for (int i = 0; i < 3; i++) {
            var wisp = new WC.Canvas { RenderTransform = new WM.TranslateTransform() };
            string d = "M0,0 C9,-12 -9,-24 0,-36 C9,-48 -9,-60 0,-72";
            wisp.Children.Add(Ln(d, Brs("#3F6212", 130), 6)); wisp.Children.Add(Ln(d, Brs("#BEF264", 225), 3.2));
            stink.Children.Add(At(wisp, 190 + i * 30, 250 - (i % 2) * 14));
        }
        Action stinkOn = () => {
            stink.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 400, null));
            int i = 0;
            foreach (WC.Canvas w in stink.Children) {
                var t = (WM.TranslateTransform)w.RenderTransform;
                t.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(0, -34, Ms(1100 + i * 140)) { BeginTime = Ms(i * 260), RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QOut });
                var ok = K(0, 0, 250, 1, 1100 + i * 140, 0); ok.BeginTime = Ms(i * 260); ok.RepeatBehavior = WA.RepeatBehavior.Forever;
                w.BeginAnimation(W.UIElement.OpacityProperty, ok);
                i++;
            }
        };
        Action stinkOff = () => stink.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 500, null));

        // glints at the end of a flush (peak-end): three small four-point stars on the clean porcelain
        var gl = new System.Collections.Generic.List<Tuple<WS.Path, WM.ScaleTransform>>();
        double[][] gpos = { new double[] { 62, 58 }, new double[] { 60, 200 }, new double[] { 238, 252 } };
        foreach (double[] gp in gpos) {
            var st = new WM.ScaleTransform(0, 0);
            var s = Pth("M0,-10 L2.4,-2.4 L10,0 L2.4,2.4 L0,10 L-2.4,2.4 L-10,0 L-2.4,-2.4 Z", Brs("#FFFFFF", 250)); s.Opacity = 0; s.RenderTransform = st;
            glints.Children.Add(At(s, gp[0], gp[1])); gl.Add(Tuple.Create(s, st));
        }
        Action glint = () => {
            for (int i = 0; i < gl.Count; i++) {
                S2(gl[i].Item2, KD(i * 130, 0, 0.2, 120, 1.15, 380, 0.3));
                gl[i].Item1.BeginAnimation(W.UIElement.OpacityProperty, KD(i * 130, 0, 0, 80, 1, 380, 0));
            }
        };

        System.Media.SoundPlayer sound = null;
        if (Settings.Sound && !render) try { sound = new System.Media.SoundPlayer(Assembly.GetExecutingAssembly().GetManifestResourceStream(Files[tier])); sound.Load(); } catch (Exception) { sound = null; }

        // the duck hops (anticipation, jump, landing squash, settle)
        Action<double> duckHop = h => {
            duckT.BeginAnimation(WM.TranslateTransform.YProperty, K(0, 0, 70, 1.5, 200, -h, 330, 0, 400, -h * 0.2, 470, 0));
            duckS.BeginAnimation(WM.ScaleTransform.ScaleYProperty, K(0, 1, 70, 0.86, 200, 1.12, 330, 0.88, 400, 1));
            duckS.BeginAnimation(WM.ScaleTransform.ScaleXProperty, K(0, 1, 70, 1.08, 200, 0.94, 330, 1.1, 400, 1));
            duckR.BeginAnimation(WM.RotateTransform.AngleProperty, K(0, 0, 200, -7, 330, 6, 470, 0));
        };
        Action<int> duckWobble = dur => duckR.BeginAnimation(WM.RotateTransform.AngleProperty,
            new WA.DoubleAnimation(-6, 6, Ms(150)) { AutoReverse = true, RepeatBehavior = new WA.RepeatBehavior(Math.Max(1, dur / 300)), FillBehavior = WA.FillBehavior.Stop, EasingFunction = QInOut });
        // press: wind-up (L2), squash (L1), settle (L5); the contact (cue + 140) throws a spark and a ring on the water
        Action<double> pressH = h => {
            S2(btn, K(0, 1, 70, 1.12, 200, 0.68, 420, 1));
            spark.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 130, 0, 150, 1, 320, 0));
            S2(sparkS, K(0, 0.3, 150, 1.15, 320, 0.5));
            duckHop(h);
        };
        // flush: swirl, hole, water level drop, the rim wash whirling round (all on the same beat)
        Action<int, int, double, double, double, double> flush = (deg, dur, swirlTo, hole, lvlTo, turns) => {
            swRot.BeginAnimation(WM.RotateTransform.AngleProperty, A(0, deg, dur, QIn));
            S2(swScale, A(1, swirlTo, dur, CIn));
            S2(drain, A(0.3, hole, dur, QIn));
            S2(lvl, A(1, lvlTo, dur, QIn));
            rim.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 140, tier == 0 ? 0.55 : 0.95, dur - 360, tier == 0 ? 0.55 : 0.95, dur, 0));
            rim.BeginAnimation(WS.Shape.StrokeDashOffsetProperty, A(0, -163 * turns, dur, QIn));
            duckWobble((int)(dur * Speed));
        };
        Action refill = () => { S2(swScale, A(null, 1, 560, Back)); S2(drain, A(null, 0.3, 560, COut)); S2(lvl, A(null, 1, 560, Back)); glint(); };
        Action<string> say = s => msg.Text = s;

        var cues = new System.Collections.Generic.List<Tuple<int, Action>>();
        Action<int, Action> at = (t, a) => cues.Add(Tuple.Create((int)(t * Speed), a));
        int endAt;
        if (!motion) endAt = 400;
        else if (tier == 0) {           // light: one press, a half drain, quick refill
            at(420, () => pressH(6));
            at(560, ring);              // press contact = cue + 140 (the click sound sits here)
            at(600, () => flush(720, 1500, 0.4, 1.2, 0.93, 1.3));
            at(2150, () => { refill(); burst(3, 20, 18); });
            endAt = 2750;
        } else if (tier == 1) {         // medium: the full swirl
            at(450, () => pressH(8));
            at(590, ring);
            at(620, () => flush(1440, 2500, 0.08, 1.7, 0.86, 3));
            at(3150, () => { refill(); burst(5, 36, 30); });
            endAt = 3750;
        } else if (tier == 2) {         // heavy: murky water, two presses, the bowl shakes, big splash
            at(250, () => { tintWater(Murky, 450); stinkOn(); });
            at(450, () => pressH(8));
            at(590, ring);
            at(760, () => pressH(13));
            at(900, ring);
            at(950, () => { flush(2520, 2900, 0.05, 2.0, 0.8, 6); Shake(shake, 3, 26); });
            at(3250, () => { bigBurst(); S2(puddleS, A(0, 1, 520, COut)); });
            at(3900, () => { refill(); tintWater(Clean, 600); stinkOff(); puddle.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 600, null)); });
            endAt = 4500;
        } else {                        // clogged: water rises, the press fails, plunger x3, then it bursts free
            at(200, () => {
                tintWater(Red, 500);
                rise.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 900, COut));
                S2(riseScale, A(0.8, 1, 900, COut));
                alarm.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(0.1, 0.8, Ms(520)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
            });
            // bubble pops, one per sound cue (bubbling, water rising): started 260 ms early so each pops ON its cue
            int[] pops = { 300, 850, 1350, 1950, 2500, 3050, 3600, 4050 };
            for (int k = 0; k < pops.Length; k++) {
                int kk = k, t0 = pops[k];
                at(t0 - 260, () => popBub(kk, 260, 52 + (kk % 3) * 10));
            }
            at(1100, () => { say(Tx.T("ov.clogged")); S2(riseScale, new WA.DoubleAnimation(1, 1.035, Ms(450)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut }); });
            at(1400, () => { pressH(5); swRot.BeginAnimation(WM.RotateTransform.AngleProperty, new WA.DoubleAnimation(0, 40, Ms(220)) { AutoReverse = true, EasingFunction = COut }); Shake(shake, 4, 5); });
            at(1540, ring);             // press contact: nothing drains
            at(1400, () => popBub(8, 240, 44));    // the failed flush: one big glug, popping on its sound cue (1640)
            at(1640, () => { S2(riseScale, K(0, 1.04, 110, 1.075, 330, 1.035)); burst(4, 26, 26); });
            at(2050, () => {
                say(Tx.T("ov.plunge"));
                plunger.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 150, null));
                plY.BeginAnimation(WM.TranslateTransform.YProperty, A(-540, 0, 520, Back));
            });
            int hit = 0; double[] pud = { 0.35, 0.65, 0.95 };
            foreach (int t in new[] { 2700, 3250, 3800 }) {
                int tt = t, hh = hit++;
                at(tt, () => {
                    plY.BeginAnimation(WM.TranslateTransform.YProperty, K(0, 0, 90, -14, 240, 26, 440, 0));
                    cup.BeginAnimation(WM.ScaleTransform.ScaleYProperty, K(0, 1, 90, 1.05, 240, 0.55, 440, 1));
                    cup.BeginAnimation(WM.ScaleTransform.ScaleXProperty, K(0, 1, 90, 0.95, 240, 1.25, 440, 1));
                    Shake(shake, 2, 2);
                });
                at(tt + 240, () => {    // the squish sound sits here: the cup bottoms out, water squirts, the surface jolts
                    ring(); burst(6, 52, 42); duckHop(9);
                    S2(riseScale, K(0, 1.03, 90, 1.07, 300, 1.03));
                    S2(puddleS, A(null, pud[hh], 380, QOut));
                });
            }
            at(4400, () => {
                say(Tx.T("ov.ram"));
                plY.BeginAnimation(WM.TranslateTransform.YProperty, A(null, -540, 420, CIn));
                plunger.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 420, CIn));
                rise.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 350, null));
                S2(riseScale, A(null, 0.8, 350, CIn));
                alarm.BeginAnimation(W.UIElement.OpacityProperty, A(0, 0, 350, null));
                bigBurst();
                flush(2880, 2400, 0.04, 2.2, 0.78, 5);
                S2(puddleS, A(null, 0, 2400, CIn));
                tintWater(Clean, 2400);
                Tint(btnHi, Clean[0], 2400); Tint(btnLo, Clean[2], 2400);
            });
            at(6850, refill);
            endAt = 7450;
        }
        var ordered = cues.OrderBy(c => c.Item1).ToList(); cues.Clear(); cues.AddRange(ordered);   // stable: cues fire in time order

        bool started = false;
        Action startWork = () => { if (work != null && !started) { started = true; work.Start(); } };
        var clock = new Stopwatch();
        int next = 0, stage = 0; long shownAt = 0;
        var timer = new W.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var shots = new System.Collections.Generic.List<WM.Imaging.BitmapSource>(); int shot = 0;
        timer.Tick += delegate {
            long t = clock.ElapsedMilliseconds;
            if (render && Scenes.RenderFps > 0) {   // NJFLUSH_RENDER_FPS: every frame, for video
                Scenes.SeqShot((W.FrameworkElement)root, t, preview ?? "ram");
                if (stage >= 1 && t >= shownAt + 1500) { win.Close(); Scenes.FlushSeq(); return; }
            } else if (render) {   // 8 frames across the choreography, the 9th once the result has landed
                long len = (long)(endAt * Speed);
                bool take = shot < 8 ? t >= len * (shot + 1) / 8 : stage >= 1 && t >= shownAt + 500;
                if (take && shot < 9) {
                    var fe = (W.FrameworkElement)root;
                    var bmp = new WM.Imaging.RenderTargetBitmap((int)fe.ActualWidth, (int)fe.ActualHeight, 96, 96, WM.PixelFormats.Pbgra32);
                    bmp.Render(fe); bmp.Freeze(); shots.Add(bmp); shot++;
                    if (shot == 9) { Scenes.SaveFrames(shots, Scenes.RenderDir, preview ?? "ram"); win.Close(); return; }
                }
            }
            while (next < cues.Count && t >= cues[next].Item1) cues[next++].Item2();
            // 1.2.3: the flush starts with the last cue (the refill), so the motion runs exactly like the preview instead of
            // stuttering while every working set is emptied
            if (next >= cues.Count) startWork();
            long end = (long)(endAt * Speed);
            // the ending lands (L26) only when the flush, Explorer restart included, is done (20 s safety cap; the ripples keep moving)
            if (stage == 0 && next >= cues.Count && t >= end && (result != null || t >= end + 20000)) {
                msg.Text = result ?? Tx.T("ov.finishing"); shownAt = t; stage = 1;
                if (motion) { S2(msgScale, A(0.85, 1, 380, Back)); msg.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 220, null)); }
            } else if (stage == 1 && t >= shownAt + 1300) {
                var fade = new WA.DoubleAnimation(0, Ms(motion ? 350 : 1));
                fade.Completed += delegate { win.Close(); };
                root.BeginAnimation(W.UIElement.OpacityProperty, fade);
                stage = 2;
            }
        };
        // 1.2.1: the clock and the sound start when the first frame is on screen (Loaded fired before it, so a slow first paint
        // under full RAM skipped frames while the sound ran ahead)
        win.ContentRendered += delegate {
            clock.Start(); timer.Start();
            if (sound != null && !render) {    // one soundtrack per tier, cued to this clock, at the user's volume
                uint v = (uint)(Settings.Volume * 0xFFFF / 100); waveOutSetVolume(IntPtr.Zero, v | (v << 16));
                sound.Play();
            }
            root.BeginAnimation(W.UIElement.OpacityProperty, A(null, 1, motion ? 250 : 1, null));
            if (motion) S2(pop, A(0.6, 1, 450, new WA.BackEase { Amplitude = 0.4, EasingMode = WA.EasingMode.EaseOut }));
            else { pop.ScaleX = 1; pop.ScaleY = 1; }
        };
        if (!render) win.MouseDown += delegate { win.Close(); };
        win.KeyDown += (s, e) => { if (e.Key == W.Input.Key.Escape) win.Close(); };
        win.ShowDialog();
        timer.Stop(); if (sound != null) sound.Stop();
        startWork();  // closed before it started (a click on the overlay): the flush still runs
        if (work != null) work.Join(20000);  // let the flush finish before the widget starts
    }
}
