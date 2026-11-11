Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System.Drawing;
public static class CursorAlphaBounds358 {
 public static Rectangle Find(Bitmap b) {
  int x0=b.Width,y0=b.Height,x1=-1,y1=-1;
  for(int y=0;y<b.Height;y++) for(int x=0;x<b.Width;x++) if(b.GetPixel(x,y).A>16) {
   if(x<x0)x0=x;if(x>x1)x1=x;if(y<y0)y0=y;if(y>y1)y1=y;
  }
  if(x1<0) throw new System.Exception("Empty cursor cutout");
  return new Rectangle(x0,y0,x1-x0+1,y1-y0+1);
 }
}
'@ -ReferencedAssemblies System.Drawing.Common,System.Drawing.Primitives
$ids=@('spear','key','glass')
$labels=@('3 · Готический наконечник','5 · Башенный ключ','8 · Осколок витража')
$sheet=[System.Drawing.Bitmap]::new(1080,700)
$g=[System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::FromArgb(15,19,23))
$g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font=[System.Drawing.Font]::new('Segoe UI',14)
$small=[System.Drawing.Font]::new('Segoe UI',11)
$light=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(220,218,206))
$dark=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(12,17,21))
$paper=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(217,214,203))
for($i=0;$i -lt 3;$i++) {
 $id=$ids[$i]; $x=15+$i*360
 $g.DrawString($labels[$i],$font,$light,$x,12)
 $concept=[System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot ($id+'.png')))
 $g.DrawImage($concept,[System.Drawing.Rectangle]::new($x,50,330,330));$concept.Dispose()
 $cutout=[System.Drawing.Bitmap]::new((Join-Path $PSScriptRoot ('cutouts/'+$id+'.png')))
 if($cutout.GetPixel(0,0).A -gt 0) { throw ('Cutout corner is not transparent: '+$id) }
 $bounds=[CursorAlphaBounds358]::Find($cutout)
 $sizeImages=@()
 foreach($size in @(64,40,32)) {
  $scaled=[System.Drawing.Bitmap]::new($size,$size)
  $sg=[System.Drawing.Graphics]::FromImage($scaled)
  $sg.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $sg.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $factor=($size-4)/[Math]::Max($bounds.Width,$bounds.Height)
  $dw=$bounds.Width*$factor;$dh=$bounds.Height*$factor
  $sg.DrawImage($cutout,[System.Drawing.RectangleF]::new(($size-$dw)/2,($size-$dh)/2,$dw,$dh),[System.Drawing.RectangleF]$bounds,[System.Drawing.GraphicsUnit]::Pixel)
  $sg.Dispose()
  $scaled.Save((Join-Path $PSScriptRoot ('cutouts/'+$id+'-'+$size+'.png')),[System.Drawing.Imaging.ImageFormat]::Png)
  $sizeImages+=,$scaled
 }
 for($row=0;$row -lt 2;$row++) {
  $y=410+$row*110
  $bg=if($row -eq 0){$dark}else{$paper};$fg=if($row -eq 0){$light}else{$dark}
  $g.FillRectangle($bg,$x,$y,330,100)
  $positions=@(25,130,225)
  for($j=0;$j -lt 3;$j++) {
   $img=$sizeImages[$j]
   $g.DrawImageUnscaled($img,$x+$positions[$j],$y+8)
   $g.DrawString((@('64 px','40 px','32 px')[$j]),$small,$fg,$x+$positions[$j],$y+74)
  }
 }
 foreach($img in $sizeImages){$img.Dispose()};$cutout.Dispose()
}
$g.DrawString('Вверху — оригиналы Venice. Внизу — прозрачные пробы в малом размере. В игру не подключены.',$small,$light,15,661)
$sheet.Save((Join-Path $PSScriptRoot 'preview.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$sheet.Dispose();$font.Dispose();$small.Dispose();$light.Dispose();$dark.Dispose();$paper.Dispose()
