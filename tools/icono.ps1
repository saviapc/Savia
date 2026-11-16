# Genera src\icono.ico (varios tamaños, PNG dentro de ICO). Solo hace falta si cambias el diseño.
Add-Type -AssemblyName System.Drawing
$salida = Join-Path $PSScriptRoot '..\src\icono.ico'
$tamanos = 256, 64, 48, 32, 16
$pngs = foreach ($t in $tamanos) {
    $bmp = New-Object System.Drawing.Bitmap $t, $t
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    # Fondo: cuadrado redondeado grafito
    $r = $t * 0.24; $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc(0, 0, $r * 2, $r * 2, 180, 90); $p.AddArc($t - $r * 2 - 1, 0, $r * 2, $r * 2, 270, 90)
    $p.AddArc($t - $r * 2 - 1, $t - $r * 2 - 1, $r * 2, $r * 2, 0, 90); $p.AddArc(0, $t - $r * 2 - 1, $r * 2, $r * 2, 90, 90); $p.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x1A, 0x1A, 0x17))), $p)
    # Forma orgánica salvia (la misma de la barra superior), centrada
    $s = $t / 15.0 * 0.62; $ox = $t * 0.5 - 7 * $s; $oy = $t * 0.5 - 7.4 * $s
    $f = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pt = { param([double]$x, [double]$y) New-Object System.Drawing.PointF ($ox + $x * $s), ($oy + $y * $s) }
    $f.AddBezier((& $pt 7 0), (& $pt 12 0), (& $pt 15 4), (& $pt 14 8))
    $f.AddBezier((& $pt 14 8), (& $pt 13 13), (& $pt 8 15), (& $pt 4 13))
    $f.AddBezier((& $pt 4 13), (& $pt 0 11), (& $pt (-1) 6), (& $pt 2 3))
    $f.AddBezier((& $pt 2 3), (& $pt 3 1), (& $pt 5 0), (& $pt 7 0))
    $f.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xA9, 0xC2, 0x9B))), $f)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$tamanos.Count)
$offset = 6 + 16 * $tamanos.Count
for ($i = 0; $i -lt $tamanos.Count; $i++) {
    $t = $tamanos[$i]
    $w.Write([byte]($(if ($t -ge 256) { 0 } else { $t }))); $w.Write([byte]($(if ($t -ge 256) { 0 } else { $t })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset); $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
[IO.File]::WriteAllBytes($salida, $out.ToArray())
"Icono generado: $salida"
