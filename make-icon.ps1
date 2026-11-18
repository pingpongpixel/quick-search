$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot 'search.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()

foreach ($size in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.Clear([System.Drawing.Color]::Transparent)

  $u = $size / 256.0
  $pad = [Math]::Max(1, [int](8 * $u))
  $rect = New-Object System.Drawing.Rectangle($pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))
  $radius = [int](58 * $u)

  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $radius * 2
  $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
  $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
  $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
  $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
  $path.CloseFigure()

  $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point($rect.X, $rect.Y)),
    (New-Object System.Drawing.Point($rect.X, $rect.Bottom)),
    [System.Drawing.Color]::FromArgb(255, 37, 38, 51),
    [System.Drawing.Color]::FromArgb(255, 12, 13, 18))
  $g.FillPath($bg, $path)
  $bg.Dispose()

  $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0, 120, 212), [Math]::Max(1, 2.2 * $u))
  $g.DrawPath($edge, $path)
  $edge.Dispose()

  $blue = [System.Drawing.Color]::FromArgb(255, 0, 140, 235)
  $lensPen = New-Object System.Drawing.Pen($blue, [Math]::Max(1.6, 20 * $u))
  $lensPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $lensPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $cx = 108 * $u
  $cy = 108 * $u
  $r = 52 * $u
  $g.DrawEllipse($lensPen, [float]($cx - $r), [float]($cy - $r), [float]($r * 2), [float]($r * 2))

  $handlePen = New-Object System.Drawing.Pen($blue, [Math]::Max(1.6, 22 * $u))
  $handlePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $handlePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $g.DrawLine($handlePen, [float](148 * $u), [float](148 * $u), [float](198 * $u), [float](198 * $u))

  $glint = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(180, 255, 255, 255), [Math]::Max(1, 5 * $u))
  $glint.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $glint.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $g.DrawArc($glint, [float]($cx - $r * 0.66), [float]($cy - $r * 0.66), [float]($r * 1.32), [float]($r * 1.32), 200, 60)

  $lensPen.Dispose(); $handlePen.Dispose(); $glint.Dispose(); $path.Dispose(); $g.Dispose()

  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += ,@{ Size = $size; Bytes = $ms.ToArray() }
  $ms.Dispose()
  $bmp.Dispose()
}

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$pngs.Count)
$offset = 6 + (16 * $pngs.Count)
foreach ($p in $pngs) {
  $dim = $p.Size
  if ($dim -ge 256) { $dim = 0 }
  $bw.Write([Byte]$dim)
  $bw.Write([Byte]$dim)
  $bw.Write([Byte]0)
  $bw.Write([Byte]0)
  $bw.Write([UInt16]1)
  $bw.Write([UInt16]32)
  $bw.Write([UInt32]$p.Bytes.Length)
  $bw.Write([UInt32]$offset)
  $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.Bytes) }
$bw.Flush()
$bw.Close()
$fs.Close()

"saved $out ($((Get-Item $out).Length) bytes, $($sizes.Count) sizes)"
