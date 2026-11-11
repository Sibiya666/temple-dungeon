Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System.Drawing;
public static class CursorSelectionBounds {
 public static Rectangle Find(Bitmap b) {
  int x0=b.Width,y0=b.Height,x1=-1,y1=-1;
  for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)if(b.GetPixel(x,y).A>16){x0=System.Math.Min(x0,x);y0=System.Math.Min(y0,y);x1=System.Math.Max(x1,x);y1=System.Math.Max(y1,y);}
  if(x1<0)throw new System.Exception("Empty image");
  return new Rectangle(x0,y0,x1-x0+1,y1-y0+1);
 }
 public static Point Tip(Bitmap b) {
  int best=int.MaxValue;Point p=new Point();
  for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)if(b.GetPixel(x,y).A>=128 && x+y<best){best=x+y;p=new Point(x,y);}
  if(best==int.MaxValue)throw new System.Exception("No opaque pointer tip");
  return p;
 }
}
'@ -ReferencedAssemblies System.Drawing.Common,System.Drawing.Primitives
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$runtimeRoot=Join-Path $repoRoot 'game/Ui/Art/Cursors'
$manifest=@()
foreach($id in @('glass','spear','key')) {
 $sourceRelative='docs/art/cursor/2026-11-03-venice-358/cutouts/'+$id+'.png'
 $source=[System.Drawing.Bitmap]::new((Join-Path $repoRoot $sourceRelative))
 $bounds=[CursorSelectionBounds]::Find($source)
 $bitmap=[System.Drawing.Bitmap]::new(48,48)
 $g=[System.Drawing.Graphics]::FromImage($bitmap)
 $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
 $g.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
 $factor=44/[Math]::Max($bounds.Width,$bounds.Height)
 $w=$bounds.Width*$factor;$h=$bounds.Height*$factor
 $g.DrawImage($source,[System.Drawing.RectangleF]::new((48-$w)/2,(48-$h)/2,$w,$h),[System.Drawing.RectangleF]$bounds,[System.Drawing.GraphicsUnit]::Pixel)
 $g.Dispose()
 $tip=[CursorSelectionBounds]::Tip($bitmap)
 $bitmap.Save((Join-Path $runtimeRoot ($id+'.png')),[System.Drawing.Imaging.ImageFormat]::Png)
 $manifest += [ordered]@{id=$id;source=$sourceRelative;runtime=('res://Ui/Art/Cursors/'+$id+'.png');width=48;height=48;hotspot=@($tip.X,$tip.Y);tipAlpha=$bitmap.GetPixel($tip.X,$tip.Y).A}
 $bitmap.Dispose();$source.Dispose()
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime-manifest.json') -Encoding utf8
$manifest | ForEach-Object { Write-Output ($_.id+' hotspot='+($_.hotspot -join ',')+' alpha='+$_.tipAlpha) }
