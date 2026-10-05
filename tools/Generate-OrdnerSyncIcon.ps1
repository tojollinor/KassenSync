param(
    [string]$IconPath = "src\KassenSync.App\Assets\OrdnerSync.ico",
    [string]$PreviewPath = "src\KassenSync.App\Assets\OrdnerSync.png",
    [int]$PreviewSize = 512
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$iconFull = [System.IO.Path]::GetFullPath($IconPath)
if (-not (Test-Path $iconFull)) {
    throw "OrdnerSync master icon not found: $iconFull"
}

$bytes = [System.IO.File]::ReadAllBytes($iconFull)
if ($bytes.Length -lt 22) {
    throw "OrdnerSync master icon is invalid."
}

$count = [BitConverter]::ToUInt16($bytes, 4)
if ($count -lt 1) {
    throw "OrdnerSync master icon has no frames."
}

$best = $null
for ($i = 0; $i -lt $count; $i++) {
    $entry = 6 + (16 * $i)
    if ($entry + 16 -gt $bytes.Length) {
        throw "OrdnerSync master icon directory is truncated."
    }

    $width = if ($bytes[$entry] -eq 0) { 256 } else { [int]$bytes[$entry] }
    $height = if ($bytes[$entry + 1] -eq 0) { 256 } else { [int]$bytes[$entry + 1] }
    $length = [BitConverter]::ToUInt32($bytes, $entry + 8)
    $offset = [BitConverter]::ToUInt32($bytes, $entry + 12)

    if (($offset + $length) -gt $bytes.Length) {
        throw "OrdnerSync master icon frame is invalid."
    }

    $area = $width * $height
    if ($null -eq $best -or $area -gt $best.Area) {
        $best = [PSCustomObject]@{
            Width = $width
            Height = $height
            Length = [int]$length
            Offset = [int]$offset
            Area = $area
        }
    }
}

$frameBytes = [byte[]]::new($best.Length)
[Array]::Copy($bytes, $best.Offset, $frameBytes, 0, $best.Length)

$frameStream = [System.IO.MemoryStream]::new($frameBytes, $false)
$source = [System.Drawing.Image]::FromStream($frameStream)
$bitmap = [System.Drawing.Bitmap]::new(
    $PreviewSize,
    $PreviewSize,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)

try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $graphics.DrawImage(
        $source,
        [System.Drawing.Rectangle]::new(0, 0, $PreviewSize, $PreviewSize),
        0,
        0,
        $source.Width,
        $source.Height,
        [System.Drawing.GraphicsUnit]::Pixel)

    $previewFull = [System.IO.Path]::GetFullPath($PreviewPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($previewFull)) | Out-Null
    $bitmap.Save($previewFull, [System.Drawing.Imaging.ImageFormat]::Png)

    Write-Host "OrdnerSync master ICO preserved: $iconFull"
    Write-Host "OrdnerSync GUI preview generated from master ICO: $previewFull"
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
    $source.Dispose()
    $frameStream.Dispose()
}
