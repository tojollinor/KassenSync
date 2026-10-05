param(
    [string]$SourcePath = "src\KassenSync.App\Assets\OrdnerSyncLogoFull.png",
    [string]$PngOutputPath = "src\KassenSync.App\Assets\OrdnerSyncIcon.png",
    [string]$IcoOutputPath = "src\KassenSync.App\Assets\OrdnerSync.ico"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Resolve-RepoPath {
    param([string]$Path)
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Render-Square {
    param(
        [System.Drawing.Bitmap]$Source,
        [int]$Size
    )

    $result = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $graphics = [System.Drawing.Graphics]::FromImage($result)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $pad = [Math]::Max(1, [int][Math]::Round($Size * 24.0 / 512.0))
        $available = $Size - (2 * $pad)
        $scale = [Math]::Min(
            $available / [double]$Source.Width,
            $available / [double]$Source.Height)

        $drawWidth = [Math]::Max(1, [int][Math]::Round($Source.Width * $scale))
        $drawHeight = [Math]::Max(1, [int][Math]::Round($Source.Height * $scale))
        $drawX = [int][Math]::Floor(($Size - $drawWidth) / 2.0)
        $drawY = [int][Math]::Floor(($Size - $drawHeight) / 2.0)

        $graphics.DrawImage(
            $Source,
            [System.Drawing.Rectangle]::new($drawX, $drawY, $drawWidth, $drawHeight),
            [System.Drawing.Rectangle]::new(0, 0, $Source.Width, $Source.Height),
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $graphics.Dispose()
    }

    return $result
}

$sourceFull = Resolve-RepoPath $SourcePath
$pngFull = Resolve-RepoPath $PngOutputPath
$icoFull = Resolve-RepoPath $IcoOutputPath

if (-not (Test-Path $sourceFull)) {
    throw "OrdnerSync master logo not found: $sourceFull"
}

$source = [System.Drawing.Bitmap]::new($sourceFull)
try {
    $width = $source.Width
    $height = $source.Height
    $rowVisible = [bool[]]::new($height)
    $hasVisible = $false
    $hasTransparency = $false

    # The approved master owns the alpha mask. Never derive transparency from RGB colors.
    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $alpha = $source.GetPixel($x, $y).A
            if ($alpha -gt 0) {
                $rowVisible[$y] = $true
                $hasVisible = $true
            }
            if ($alpha -lt 255) {
                $hasTransparency = $true
            }
        }
    }

    if (-not $hasVisible) {
        throw "OrdnerSync master logo has no visible pixels."
    }
    if (-not $hasTransparency) {
        throw "OrdnerSync master logo has no alpha transparency."
    }

    $firstVisible = 0
    while ($firstVisible -lt $height -and -not $rowVisible[$firstVisible]) {
        $firstVisible++
    }

    $lastVisible = $height - 1
    while ($lastVisible -ge 0 -and -not $rowVisible[$lastVisible]) {
        $lastVisible--
    }

    # The longest transparent horizontal gap separates symbol and wordmark.
    $bestGapStart = -1
    $bestGapLength = 0
    $gapStart = -1
    for ($y = $firstVisible; $y -le $lastVisible; $y++) {
        if (-not $rowVisible[$y]) {
            if ($gapStart -lt 0) {
                $gapStart = $y
            }
        }
        elseif ($gapStart -ge 0) {
            $gapLength = $y - $gapStart
            if ($gapLength -gt $bestGapLength) {
                $bestGapStart = $gapStart
                $bestGapLength = $gapLength
            }
            $gapStart = -1
        }
    }

    if ($bestGapStart -lt 0 -or $bestGapLength -lt 2) {
        throw "Could not locate the transparent separator between OrdnerSync symbol and wordmark."
    }

    $symbolLastRow = $bestGapStart - 1
    $minX = $width
    $minY = $height
    $maxX = -1
    $maxY = -1

    # Crop only by the existing alpha mask. No white/black/color threshold is used.
    for ($y = $firstVisible; $y -le $symbolLastRow; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            if ($source.GetPixel($x, $y).A -gt 0) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }

    if ($maxX -lt $minX -or $maxY -lt $minY) {
        throw "No visible symbol pixels found in OrdnerSync master logo."
    }

    $symbolWidth = $maxX - $minX + 1
    $symbolHeight = $maxY - $minY + 1
    $symbol = [System.Drawing.Bitmap]::new(
        $symbolWidth,
        $symbolHeight,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $cropGraphics = [System.Drawing.Graphics]::FromImage($symbol)
    try {
        $cropGraphics.Clear([System.Drawing.Color]::Transparent)
        $cropGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $cropGraphics.DrawImage(
            $source,
            [System.Drawing.Rectangle]::new(0, 0, $symbolWidth, $symbolHeight),
            [System.Drawing.Rectangle]::new($minX, $minY, $symbolWidth, $symbolHeight),
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $cropGraphics.Dispose()
    }

    try {
        $pngMaster = Render-Square -Source $symbol -Size 512
        try {
            $pngMaster.Save($pngFull, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $pngMaster.Dispose()
        }

        $sizes = @(16, 24, 32, 48, 64, 128, 256)
        $frames = @()

        foreach ($size in $sizes) {
            $frame = Render-Square -Source $symbol -Size $size
            try {
                $memory = [System.IO.MemoryStream]::new()
                try {
                    $frame.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
                    $frames += [PSCustomObject]@{
                        Size = $size
                        Bytes = $memory.ToArray()
                    }
                }
                finally {
                    $memory.Dispose()
                }
            }
            finally {
                $frame.Dispose()
            }
        }

        $stream = [System.IO.File]::Open(
            $icoFull,
            [System.IO.FileMode]::Create,
            [System.IO.FileAccess]::Write)

        $writer = [System.IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([UInt16]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]$frames.Count)

            $offset = 6 + (16 * $frames.Count)
            foreach ($item in $frames) {
                $dimension = if ($item.Size -eq 256) { 0 } else { $item.Size }
                $writer.Write([Byte]$dimension)
                $writer.Write([Byte]$dimension)
                $writer.Write([Byte]0)
                $writer.Write([Byte]0)
                $writer.Write([UInt16]1)
                $writer.Write([UInt16]32)
                $writer.Write([UInt32]$item.Bytes.Length)
                $writer.Write([UInt32]$offset)
                $offset += $item.Bytes.Length
            }

            foreach ($item in $frames) {
                $writer.Write([Byte[]]$item.Bytes)
            }
        }
        finally {
            $writer.Dispose()
            $stream.Dispose()
        }
    }
    finally {
        $symbol.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Host "OrdnerSync full logo kept unchanged: $sourceFull"
Write-Host "OrdnerSync symbol PNG generated from master alpha: $pngFull"
Write-Host "OrdnerSync Windows ICO generated in 16/24/32/48/64/128/256 px: $icoFull"
