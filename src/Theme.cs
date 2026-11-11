using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace WSLBox
{
    internal static class MenuText
    {
        public static string Clean(string value) { return value == null ? null : value.Replace("…", "").Replace("...", ""); }
    }
    internal static class Theme
    {
        public static readonly Brush Text = new SolidColorBrush(Color.FromRgb(239, 243, 248));
        public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(171, 184, 201));
        public static ResourceDictionary Resources()
        {
            return (ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<Style x:Key='HeaderButton' TargetType='Button'>
 <Setter Property='Foreground' Value='#EEEFF3'/><Setter Property='Background' Value='Transparent'/>
 <Setter Property='BorderThickness' Value='0'/><Setter Property='Width' Value='28'/><Setter Property='Height' Value='28'/>
 <Setter Property='FontSize' Value='18'/><Setter Property='Cursor' Value='Hand'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
  <Border x:Name='plate' Background='{TemplateBinding Background}' CornerRadius='5' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
  <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='plate' Property='Background' Value='#28FFFFFF'/></Trigger>
   <Trigger Property='IsPressed' Value='True'><Setter TargetName='plate' Property='Background' Value='#42FFFFFF'/></Trigger>
   <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger></ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>
</Style>
<Style x:Key='FileItem' TargetType='ListBoxItem'>
 <Setter Property='Foreground' Value='#F5F7FA'/><Setter Property='Padding' Value='2'/><Setter Property='Margin' Value='1'/>
 <Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Background' Value='Transparent'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'>
  <Border x:Name='plate' CornerRadius='5' BorderThickness='1' BorderBrush='Transparent' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
   <ContentPresenter/></Border><ControlTemplate.Triggers>
   <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='plate' Property='Background' Value='#23FFFFFF'/></Trigger>
   <Trigger Property='IsSelected' Value='True'><Setter TargetName='plate' Property='Background' Value='#665486BC'/><Setter TargetName='plate' Property='BorderBrush' Value='#AA8CBDF3'/></Trigger>
   <DataTrigger Binding='{Binding IsDropTarget}' Value='True'><Setter TargetName='plate' Property='Background' Value='#665486BC'/><Setter TargetName='plate' Property='BorderBrush' Value='#AA8CBDF3'/></DataTrigger>
  </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>
<Style x:Key='FileList' TargetType='ListBox'>
 <Setter Property='ScrollViewer.CanContentScroll' Value='True'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBox'>
  <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
   <ScrollViewer Focusable='False' Padding='{TemplateBinding Padding}' Style='{DynamicResource FileScroller}'
     HorizontalScrollBarVisibility='{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}'
     VerticalScrollBarVisibility='{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}'>
    <ItemsPresenter/>
   </ScrollViewer>
  </Border>
 </ControlTemplate></Setter.Value></Setter>
</Style>
<Style x:Key='FileScroller' TargetType='ScrollViewer'>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollViewer'>
  <Grid ClipToBounds='True'>
   <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' CanContentScroll='{TemplateBinding CanContentScroll}' Margin='{TemplateBinding Padding}'/>
   <ScrollBar x:Name='PART_VerticalScrollBar' HorizontalAlignment='Right' Orientation='Vertical'
    Maximum='{TemplateBinding ScrollableHeight}' ViewportSize='{TemplateBinding ViewportHeight}'
    Value='{Binding VerticalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}'
    Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/>
  </Grid>
 </ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType='ScrollBar'>
 <Setter Property='Width' Value='8'/><Setter Property='Background' Value='Transparent'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
  <Grid Background='Transparent'><Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='True'
   Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' ViewportSize='{TemplateBinding ViewportSize}'
   Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
   <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
   <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border x:Name='thumb' Background='#509FAFC3' CornerRadius='4' Margin='1'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='thumb' Property='Background' Value='#AAAFBED1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
   <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
  </Track></Grid>
 </ControlTemplate></Setter.Value></Setter>
 <Style.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0'/></Trigger><Trigger Property='Maximum' Value='0'><Setter Property='Opacity' Value='0'/><Setter Property='IsHitTestVisible' Value='False'/></Trigger></Style.Triggers>
</Style></ResourceDictionary>");
        }
        public static System.Windows.Controls.Image HeaderIcon(string kind)
        {
            var group = new DrawingGroup();
            var pen = new Pen(Text, 1.4);
            using (DrawingContext draw = group.Open())
            {
                if (kind == "grid")
                    foreach (double x in new[] { 2d, 11d }) foreach (double y in new[] { 2d, 11d })
                        draw.DrawRoundedRectangle(null, pen, new Rect(x, y, 6.5, 6.5), 0.7, 0.7);
                else if (kind == "list")
                    foreach (double y in new[] { 4d, 10d, 16d })
                    {
                        draw.DrawEllipse(null, pen, new Point(3, y), 1.2, 1.2);
                        draw.DrawLine(pen, new Point(8, y), new Point(18, y));
                    }
                else throw new System.ArgumentException("Unknown header icon.", "kind");
            }
            // A fixed canvas keeps both mode icons aligned within the header.
            group.Children.Insert(0, new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
            group.Freeze(); var image = new DrawingImage(group); image.Freeze();
            return new System.Windows.Controls.Image { Source = image, Width = 20, Height = 20 };
        }
        public static ImageSource Fallback(bool folder)
        {
            var group = new DrawingGroup();
            if (folder)
            {
                group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(219, 158, 41)), null,
                    Geometry.Parse("M3,9 L17,9 21,14 37,14 37,34 3,34 Z")));
                group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(255, 210, 82)), null,
                    Geometry.Parse("M3,17 L37,17 37,34 3,34 Z")));
            }
            else
            {
                group.Children.Add(new GeometryDrawing(Brushes.WhiteSmoke, null, Geometry.Parse("M9,3 L25,3 33,11 33,37 9,37 Z")));
                group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(181, 190, 203)), null, Geometry.Parse("M25,3 L25,11 33,11 Z")));
            }
            var image = new DrawingImage(group); image.Freeze(); return image;
        }
    }
}
