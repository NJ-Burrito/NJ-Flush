// NJ-Flush panel: the shared WPF styles (buttons, switches, sliders, scrollbars).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WS = System.Windows.Shapes;

partial class View {
    static readonly W.ResourceDictionary Styles = (W.ResourceDictionary)W.Markup.XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Btn' TargetType='{x:Type Button}'>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='{x:Type Button}'>
        <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='~RMD~'
                Padding='{TemplateBinding Padding}' RenderTransformOrigin='0.5,0.5'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='UIElement.IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Opacity' Value='0.88'/></Trigger>
          <Trigger Property='ButtonBase.IsPressed' Value='True'><Setter TargetName='Bd' Property='RenderTransform'><Setter.Value><ScaleTransform ScaleX='0.97' ScaleY='0.97'/></Setter.Value></Setter></Trigger>
          <Trigger Property='UIElement.IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='~FG~'/></Trigger>
          <Trigger Property='UIElement.IsEnabled' Value='False'><Setter TargetName='Bd' Property='Opacity' Value='0.4'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Ghost' TargetType='{x:Type Button}'>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='{x:Type Button}'>
        <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='~RMD~'
                Padding='{TemplateBinding Padding}' RenderTransformOrigin='0.5,0.5'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='UIElement.IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='~RAISED~'/><Setter TargetName='Bd' Property='BorderBrush' Value='~LINE2~'/></Trigger>
          <Trigger Property='ButtonBase.IsPressed' Value='True'><Setter TargetName='Bd' Property='RenderTransform'><Setter.Value><ScaleTransform ScaleX='0.97' ScaleY='0.97'/></Setter.Value></Setter></Trigger>
          <Trigger Property='UIElement.IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='~FG~'/></Trigger>
          <Trigger Property='UIElement.IsEnabled' Value='False'><Setter TargetName='Bd' Property='Opacity' Value='0.4'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Toggle' TargetType='{x:Type CheckBox}'>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='Foreground' Value='~FG~'/><Setter Property='FontSize' Value='14'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='{x:Type CheckBox}'>
        <DockPanel Background='Transparent' MinHeight='40'>
          <Grid DockPanel.Dock='Right' Width='40' Height='22' Margin='16,0,0,0' VerticalAlignment='Center'>
            <Border x:Name='Track' CornerRadius='11' Background='#2A3A52' BorderBrush='#3A4C66' BorderThickness='1'/>
            <Ellipse x:Name='Knob' Width='16' Height='16' Fill='~FG~' HorizontalAlignment='Left' Margin='3,0,0,0'>
              <Ellipse.RenderTransform><TranslateTransform/></Ellipse.RenderTransform>
            </Ellipse>
          </Grid>
          <ContentPresenter VerticalAlignment='Center'/>
        </DockPanel>
        <ControlTemplate.Triggers>
          <Trigger Property='IsChecked' Value='True'>
            <Setter TargetName='Track' Property='Background' Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/>
            <Setter TargetName='Track' Property='BorderBrush' Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/>
            <Setter TargetName='Knob' Property='Fill' Value='~INK~'/>
            <Trigger.EnterActions><BeginStoryboard><Storyboard>
              <DoubleAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='18' Duration='0:0:0.15'>
                <DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation>
            </Storyboard></BeginStoryboard></Trigger.EnterActions>
            <Trigger.ExitActions><BeginStoryboard><Storyboard>
              <DoubleAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='0' Duration='0:0:0.15'>
                <DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation>
            </Storyboard></BeginStoryboard></Trigger.ExitActions>
          </Trigger>
          <Trigger Property='UIElement.IsMouseOver' Value='True'><Setter TargetName='Track' Property='Opacity' Value='0.88'/></Trigger>
          <Trigger Property='UIElement.IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Field' TargetType='{x:Type TextBox}'>
    <Setter Property='Foreground' Value='~FG~'/><Setter Property='CaretBrush' Value='~FG~'/><Setter Property='FontSize' Value='14'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='{x:Type TextBox}'>
        <Border x:Name='Bd' Background='~SURFACE~' BorderBrush='~LINE~' BorderThickness='1' CornerRadius='~RMD~' Padding='12,0' MinHeight='40'>
          <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='UIElement.IsMouseOver' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='~LINE2~'/></Trigger>
          <Trigger Property='UIElement.IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='~GUARD~'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Clear' TargetType='{x:Type RepeatButton}'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Fill' TargetType='{x:Type RepeatButton}'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type RepeatButton}'>
      <Border Height='4' CornerRadius='2' Background='~RAM~' VerticalAlignment='Center'/></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Slide' TargetType='{x:Type Slider}'>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='{x:Type Slider}'>
        <Grid Height='28' Background='Transparent'>
          <Border Height='4' CornerRadius='2' Background='#2A3A52' VerticalAlignment='Center'/>
          <Track x:Name='PART_Track'>
            <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Style='{StaticResource Fill}'/></Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Style='{StaticResource Clear}'/></Track.IncreaseRepeatButton>
            <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>
              <Ellipse x:Name='K' Width='16' Height='16' Fill='~FG~' Stroke='~RAM~' StrokeThickness='2'/>
              <ControlTemplate.Triggers><Trigger Property='UIElement.IsMouseOver' Value='True'><Setter TargetName='K' Property='Fill' Value='#FFFFFF'/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
          </Track>
        </Grid>
        <ControlTemplate.Triggers><Trigger Property='UIElement.IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style TargetType='{x:Type ScrollBar}'>
    <Setter Property='Width' Value='10'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>
      <Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Style='{StaticResource Clear}'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Style='{StaticResource Clear}'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border CornerRadius='4' Background='#2A3A52' Margin='2,0,2,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      </Track>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>"
        .Replace("~RMD~", "8").Replace("~FG~", Theme.Fg).Replace("~INK~", Theme.Ink).Replace("~RAISED~", Theme.Raised).Replace("~LINE2~", Theme.LineStrong)
        .Replace("~LINE~", Theme.Line).Replace("~SURFACE~", Theme.Surface).Replace("~GUARD~", Theme.Ident[3]).Replace("~RAM~", Theme.Ident[0]));
}
