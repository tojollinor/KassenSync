param(
    [string]$OutputPath = "src\KassenSync.App\Assets\OrdnerSync.ico"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-RoundedRectPath {
    param(
        [float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2

    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-LogoPngBytes {
    param([int]$Size)

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)

    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $scale = $Size / 256.0

        function S([float]$v) { return [float]($v * $scale) }

        $dark = [System.Drawing.Color]::FromArgb(255, 23, 121, 201)
        $mid = [System.Drawing.Color]::FromArgb(255, 35, 153, 226)
        $light = [System.Drawing.Color]::FromArgb(255, 55, 171, 235)
        $lower = [System.Drawing.Color]::FromArgb(255, 31, 139, 220)
        $white = [System.Drawing.Color]::White

        # Folder tab
        $tab = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $tab.AddPolygon([System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new((S 36), (S 50)),
            [System.Drawing.PointF]::new((S 110), (S 50)),
            [System.Drawing.PointF]::new((S 136), (S 67)),
            [System.Drawing.PointF]::new((S 188), (S 67)),
            [System.Drawing.PointF]::new((S 188), (S 86)),
            [System.Drawing.PointF]::new((S 36), (S 86))
        ))
        $tabBrush = [System.Drawing.SolidBrush]::new($dark)
        $g.FillPath($tabBrush, $tab)

        # Main folder body
        $body = New-RoundedRectPath -X (S 20) -Y (S 68) -Width (S 216) -Height (S 150) -Radius (S 18)
        $outline = [System.Drawing.Pen]::new($dark, [Math]::Max(1, (S 7)))
        $bodyBrush = [System.Drawing.SolidBrush]::new($mid)
        $g.FillPath($bodyBrush, $body)
        $g.DrawPath($outline, $body)

        # Front highlight and lower stripe
        $front = New-RoundedRectPath -X (S 29) -Y (S 88) -Width (S 198) -Height (S 119) -Radius (S 15)
        $frontBrush = [System.Drawing.SolidBrush]::new($light)
        $g.FillPath($frontBrush, $front)

        $lowerRect = [System.Drawing.RectangleF]::new((S 29), (S 160), (S 198), (S 47))
        $lowerBrush = [System.Drawing.SolidBrush]::new($lower)
        $g.FillRectangle($lowerBrush, $lowerRect)

        # Circular sync arrows
        $arcPen = [System.Drawing.Pen]::new($white, [Math]::Max(2, (S 16)))
        $arcPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $arcPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

        $arcRect = [System.Drawing.RectangleF]::new((S 76), (S 108), (S 104), (S 78))
        $g.DrawArc($arcPen, $arcRect, 205, 150)
        $g.DrawArc($arcPen, $arcRect, 25, 150)

        $arrowBrush = [System.Drawing.SolidBrush]::new($white)
        $g.FillPolygon($arrowBrush, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new((S 175), (S 103)),
            [System.Drawing.PointF]::new((S 205), (S 122)),
            [System.Drawing.PointF]::new((S 178), (S 143))
        ))
        $g.FillPolygon($arrowBrush, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new((S 81), (S 191)),
            [System.Drawing.PointF]::new((S 51), (S 172)),
            [System.Drawing.PointF]::new((S 78), (S 151))
        ))

        # Transparenz nur außerhalb des eigentlichen Logos:
        # alle gezeichneten Antialias-Pixel werden vollständig deckend.
        for ($y = 0; $y -lt $Size; $y++) {
            for ($x = 0; $x -lt $Size; $x++) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.A -gt 0 -and $c.A -lt 255) {
                    $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $c.R, $c.G, $c.B))
                }
            }
        }

        $ms = [System.IO.MemoryStream]::new()
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return $ms.ToArray()
    }
    finally {
        $g.Dispose()
        $bmp.Dispose()
    }
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    [PSCustomObject]@{
        Size = $size
        Bytes = New-LogoPngBytes -Size $size
    }
}

$directorySize = 6 + (16 * $frames.Count)
$offset = $directorySize

$dirEntries = @()
foreach ($frame in $frames) {
    $dirEntries += [PSCustomObject]@{
        Size = $frame.Size
        Length = $frame.Bytes.Length
        Offset = $offset
    }
    $offset += $frame.Bytes.Length
}

$outputFull = [System.IO.Path]::GetFullPath($OutputPath)
$outputDir = [System.IO.Path]::GetDirectoryName($outputFull)
[System.IO.Directory]::CreateDirectory($outputDir) | Out-Null

$stream = [System.IO.File]::Open($outputFull, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($stream)

try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$frames.Count)

    foreach ($entry in $dirEntries) {
        $dim = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([Byte]$dim)
        $writer.Write([Byte]$dim)
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$entry.Length)
        $writer.Write([UInt32]$entry.Offset)
    }

    foreach ($frame in $frames) {
        $writer.Write([Byte[]]$frame.Bytes)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Host "OrdnerSync icon generated: $outputFull"
