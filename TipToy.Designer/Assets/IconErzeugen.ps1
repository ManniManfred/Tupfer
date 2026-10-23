# Erzeugt das App-Icon (TipToy.ico mit 16–256 px und TipToy.png mit 256 px).
# Aufruf unter Windows:  powershell -ExecutionPolicy Bypass -File IconErzeugen.ps1 -OutDir .
param([string]$OutDir = $PSScriptRoot)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = 2 * $r
  $p.AddArc($x, $y, $d, $d, 180, 90)
  $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

function C([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

# Stift im lokalen Koordinatensystem (Achse entlang +y, Spitze unten)
function DrawPen($g, [float]$dx, [float]$dy, [bool]$shadow) {
  $st = $g.Save()
  $g.TranslateTransform(150 + $dx, 100 + $dy)
  $g.RotateTransform(45)
  if ($shadow) {
    $b = New-Object System.Drawing.SolidBrush (C 55 120 30 0)
    $body = RoundRect -23 -92 46 140 23
    $g.FillPath($b, $body)
    $cone = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cone.AddPolygon([System.Drawing.PointF[]]@((New-Object System.Drawing.PointF -23, 40), (New-Object System.Drawing.PointF 23, 40), (New-Object System.Drawing.PointF 6, 80), (New-Object System.Drawing.PointF -6, 80)))
    $g.FillPath($b, $cone)
    $g.Restore($st); return
  }
  # Spitze
  $cone = New-Object System.Drawing.Drawing2D.GraphicsPath
  $cone.AddPolygon([System.Drawing.PointF[]]@((New-Object System.Drawing.PointF -23, 40), (New-Object System.Drawing.PointF 23, 40), (New-Object System.Drawing.PointF 7, 78), (New-Object System.Drawing.PointF -7, 78)))
  $g.FillPath((New-Object System.Drawing.SolidBrush (C 255 214 222 235)), $cone)
  $g.FillEllipse((New-Object System.Drawing.SolidBrush (C 255 31 42 68)), -7, 72, 14, 14)
  # Schaft
  $body = RoundRect -23 -92 46 140 23
  $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF -23, 0), (New-Object System.Drawing.PointF 23, 0), (C 255 255 255 255), (C 255 226 232 242))
  $g.FillPath($lg, $body)
  # Ring und Knopf
  $g.FillRectangle((New-Object System.Drawing.SolidBrush (C 255 31 42 68)), -23, 22, 46, 12)
  $g.FillEllipse((New-Object System.Drawing.SolidBrush (C 255 52 168 83)), -9, -60, 18, 18)
  $g.Restore($st)
}

function DrawIcon([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.InterpolationMode = 'HighQualityBicubic'
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.ScaleTransform($size / 256.0, $size / 256.0)
  $small = $size -le 24

  $inset = if ($small) { 4 } else { 12 }
  $bg = RoundRect $inset $inset (256 - 2 * $inset) (256 - 2 * $inset) 52
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 256, 256), (C 255 255 183 77), (C 255 244 81 30))
  $g.FillPath($grad, $bg)

  # OID-Punktraster
  if (-not $small) {
    $dot = New-Object System.Drawing.SolidBrush (C 120 120 30 0)
    for ($y = 132; $y -le 216; $y += 21) {
      for ($x = 40; $x -le 124; $x += 21) {
        if ($x + $y -lt 300) { $g.FillEllipse($dot, $x - 4.5, $y - 4.5, 9, 9) }
      }
    }
  }

  # Schallwellen
  if (-not $small) {
    $pen = New-Object System.Drawing.Pen (C 230 255 255 255), 12
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, 95 - 44, 158 - 44, 88, 88, -5, 70)
    $g.DrawArc($pen, 95 - 80, 158 - 80, 160, 160, -3, 66)
  }

  DrawPen $g 7 7 $true
  DrawPen $g 0 0 $false
  $g.Dispose()
  return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
  $bmp = DrawIcon $s
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  if ($s -eq 256) { $bmp.Save((Join-Path $OutDir 'TipToy.png')) }
  $pngs += , $ms.ToArray()
  $bmp.Dispose()
}

# ICO mit eingebetteten PNGs
$fs = [System.IO.File]::Create((Join-Path $OutDir 'TipToy.ico'))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $d = $pngs[$i]
  $b = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$d.Length); $w.Write([uint32]$offset)
  $offset += $d.Length
}
foreach ($d in $pngs) { $w.Write($d) }
$w.Close()
