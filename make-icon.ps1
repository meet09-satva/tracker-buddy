# Regenerates app.ico (multi-size PNG icon): blue→teal rounded square with a white progress ring and clock hands.
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = [Drawing.Bitmap]::new($s, $s)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $r = $s * 0.24
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(0, 0, $r, $r, 180, 90); $path.AddArc($s - $r - 1, 0, $r, $r, 270, 90)
    $path.AddArc($s - $r - 1, $s - $r - 1, $r, $r, 0, 90); $path.AddArc(0, $s - $r - 1, $r, $r, 90, 90); $path.CloseFigure()
    $bg = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(0, 0), [Drawing.Point]::new($s, $s),
        [Drawing.Color]::FromArgb(0, 103, 192), [Drawing.Color]::FromArgb(0, 190, 170))
    $g.FillPath($bg, $path)
    $w = [Math]::Max(1.6, $s * 0.1); $m = $s * 0.2; $d = $s - 2 * $m
    $track = [Drawing.Pen]::new([Drawing.Color]::FromArgb(80, 255, 255, 255), $w)
    $ring = [Drawing.Pen]::new([Drawing.Color]::White, $w); $ring.StartCap = 'Round'; $ring.EndCap = 'Round'
    $g.DrawEllipse($track, $m, $m, $d, $d)
    $g.DrawArc($ring, $m, $m, $d, $d, -90, 260)
    $hand = [Drawing.Pen]::new([Drawing.Color]::White, [Math]::Max(1.2, $s * 0.065)); $hand.StartCap = 'Round'; $hand.EndCap = 'Round'
    $c = $s / 2
    $g.DrawLine($hand, $c, $c, $c, $c - $d * 0.28)
    $g.DrawLine($hand, $c, $c, $c + $d * 0.2, $c + $d * 0.08)
    $ms = [IO.MemoryStream]::new(); $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save("$PSScriptRoot\icon-preview.png") }
    , $ms.ToArray()
}
$out = [IO.BinaryWriter]::new([IO.File]::Create("$PSScriptRoot\app.ico"))
$out.Write([uint16]0); $out.Write([uint16]1); $out.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $px = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $out.Write([byte]$px); $out.Write([byte]$px); $out.Write([byte]0); $out.Write([byte]0)
    $out.Write([uint16]1); $out.Write([uint16]32); $out.Write([uint32]$images[$i].Length); $out.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $out.Write($img) }
$out.Close()
