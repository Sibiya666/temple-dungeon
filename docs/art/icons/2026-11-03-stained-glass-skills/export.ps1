Add-Type -AssemblyName System.Drawing
$ids = @('cleave','battle_cry','fireball','ice_lance','spark')
$labels = @('Рассекающий удар','Боевой клич','Огненный шар','Ледяное копьё','Искра')
$masterRoot=Join-Path $PSScriptRoot 'masters'
$exportRoot=Join-Path $PSScriptRoot 'export'
New-Item -ItemType Directory -Force $exportRoot | Out-Null
$sheet=[System.Drawing.Bitmap]::new(1250,440)
$draw=[System.Drawing.Graphics]::FromImage($sheet)
$draw.Clear([System.Drawing.Color]::FromArgb(13,17,20))
$draw.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font=[System.Drawing.Font]::new('Segoe UI',13)
$small=[System.Drawing.Font]::new('Segoe UI',10)
for($i=0;$i -lt $ids.Count;$i++) {
 $id=$ids[$i];$x=15+$i*250
 $draw.DrawString($labels[$i],$font,[System.Drawing.Brushes]::Ivory,$x,12)
 $image=[System.Drawing.Image]::FromFile((Join-Path $masterRoot ($id+'.png')))
 $draw.DrawImage($image,[System.Drawing.Rectangle]::new($x,50,220,220))
 $positions=@(0,92,154);$sizes=@(64,32,24)
 for($j=0;$j -lt $sizes.Count;$j++) {
  $size=$sizes[$j]
  $bitmap=[System.Drawing.Bitmap]::new($size,$size)
  $g=[System.Drawing.Graphics]::FromImage($bitmap)
  $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.DrawImage($image,[System.Drawing.Rectangle]::new(0,0,$size,$size))
  $g.Dispose()
  $bitmap.Save((Join-Path $exportRoot ($id+'-'+$size+'.png')),[System.Drawing.Imaging.ImageFormat]::Png)
  $draw.DrawImageUnscaled($bitmap,$x+$positions[$j],310+(64-$size)/2)
  $draw.DrawString(($size.ToString()+' px'),$small,[System.Drawing.Brushes]::Ivory,$x+$positions[$j],382)
  $bitmap.Dispose()
 }
 $image.Dispose()
}
$draw.DrawString('Витражная серия · пять отдельных вариантов навыков · в игру не подключены',$small,[System.Drawing.Brushes]::Ivory,15,417)
$sheet.Save((Join-Path $PSScriptRoot 'preview.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$draw.Dispose();$sheet.Dispose();$font.Dispose();$small.Dispose()
Write-Output 'EXPORTED 5 masters into 15 PNGs (64/32/24) and preview.png'
