param(
    [string]$SourcePath = "src\\KassenSync.App\\Assets\\OrdnerSyncLogoFull.png",
    [string]$PngOutputPath = "src\\KassenSync.App\\Assets\\OrdnerSyncIcon.png",
    [string]$IcoOutputPath = "src\\KassenSync.App\\Assets\\OrdnerSync.ico"
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

$sourceFull = Resolve-RepoPath $SourcePath
$pngFull = Resolve-RepoPath $PngOutputPath
$icoFull = Resolve-RepoPath $IcoOutputPath

if (-not (Test-Path $sourceFull)) {
    throw "OrdnerSync logo source not found: $sourceFull"
}

[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($pngFull)) | Out-Null
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($icoFull)) | Out-Null

$source = [System.Drawing.Bitmap]::new($sourceFull)
try {
    # Symbol exakt aus dem freigegebenen vollständigen 1254x1254-Logo ausschneiden.
    # Der Textbereich darunter bleibt vollständig draußen.
    $sx = [int][Math]::Round($source.Width * 245.0 / 1254.0)
    $sy = [int][Math]::Round($source.Height * 195.0 / 1254.0)
    $sw = [int][Math]::Round($source.Width * 770.0 / 1254.0)
    $sh = [int][Math]::Round($source.Height * 650.0 / 1254.0)

    $masterSize = 1024
    $master = [System.Drawing.Bitmap]::new(
        $masterSize,
        $masterSize,
        [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)

    $g = [System.Drawing.Graphics]::FromImage($master)
    try {
        $g.Clear([System.Drawing.Color]::White)
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $dx = [int][Math]::Round(($masterSize - $sw) / 2.0)
        $dy = [int][Math]::Round(($masterSize - $sh) / 2.0)

        $g.DrawImage(
            $source,
            [System.Drawing.Rectangle]::new($dx, $dy, $sw, $sh),
            [System.Drawing.Rectangle]::new($sx, $sy, $sw, $sh),
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $g.Dispose()
    }

    try {
        # GUI-Icon als vollständig deckendes PNG.
        $master.Save($pngFull, [System.Drawing.Imaging.ImageFormat]::Png)

        # Windows-ICO mit allen üblichen Größen. Jede Ebene besitzt einen
        # vollständig deckenden weißen Hintergrund, also keinerlei Transparenz.
        $sizes = @(16, 24, 32, 48, 64, 128, 256)
        $frames = @()

        foreach ($size in $sizes) {
            $frame = [System.Drawing.Bitmap]::new(
                $size,
                $size,
                [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

            $fg = [System.Drawing.Graphics]::FromImage($frame)
            try {
                $fg.Clear([System.Drawing.Color]::White)
                $fg.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $fg.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $fg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $fg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $fg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $fg.DrawImage($master, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
            }
            finally {
                $fg.Dispose()
            }

            # Alpha zur Sicherheit auf 255 zwingen.
            for ($y = 0; $y -lt $size; $y++) {
                for ($x = 0; $x -lt $size; $x++) {
                    $p = $frame.GetPixel($x, $y)
                    if ($p.A -ne 255) {
                        $frame.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $p.R, $p.G, $p.B))
                    }
                }
            }

            $ms = [System.IO.MemoryStream]::new()
            try {
                $frame.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames += [PSCustomObject]@{
                    Size = $size
                    Bytes = $ms.ToArray()
                }
            }
            finally {
                $ms.Dispose()
                $frame.Dispose()
            }
        }

        $stream = [System.IO.File]::Open($icoFull, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
        $writer = [System.IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([UInt16]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]$frames.Count)

            $offset = 6 + (16 * $frames.Count)
            foreach ($item in $frames) {
                $dim = if ($item.Size -eq 256) { 0 } else { $item.Size }
                $writer.Write([Byte]$dim)
                $writer.Write([Byte]$dim)
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
        $master.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Host "OrdnerSync icon PNG generated: $pngFull"
Write-Host "OrdnerSync Windows ICO generated: $icoFull"
