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
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Remove-ConnectedWhiteBackground {
    param(
        [System.Drawing.Bitmap]$InputBitmap,
        [int]$Threshold = 65
    )

    $w = $InputBitmap.Width
    $h = $InputBitmap.Height
    $count = $w * $h
    $candidate = [bool[]]::new($count)
    $outside = [bool[]]::new($count)
    $queue = [System.Collections.Generic.Queue[int]]::new()

    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $p = $InputBitmap.GetPixel($x, $y)
            $dr = 255 - [int]$p.R
            $dg = 255 - [int]$p.G
            $db = 255 - [int]$p.B
            $candidate[($y * $w) + $x] = (($dr*$dr + $dg*$dg + $db*$db) -le ($Threshold*$Threshold))
        }
    }

    function Add-OutsidePixel {
        param([int]$X, [int]$Y)
        $i = ($Y * $w) + $X
        if ($candidate[$i] -and -not $outside[$i]) {
            $outside[$i] = $true
            $queue.Enqueue($i)
        }
    }

    for ($x = 0; $x -lt $w; $x++) {
        Add-OutsidePixel $x 0
        Add-OutsidePixel $x ($h - 1)
    }
    for ($y = 0; $y -lt $h; $y++) {
        Add-OutsidePixel 0 $y
        Add-OutsidePixel ($w - 1) $y
    }

    while ($queue.Count -gt 0) {
        $i = $queue.Dequeue()
        $x = $i % $w
        $y = [int][Math]::Floor($i / $w)

        if ($x -gt 0) {
            $n = $i - 1
            if ($candidate[$n] -and -not $outside[$n]) { $outside[$n] = $true; $queue.Enqueue($n) }
        }
        if ($x -lt ($w - 1)) {
            $n = $i + 1
            if ($candidate[$n] -and -not $outside[$n]) { $outside[$n] = $true; $queue.Enqueue($n) }
        }
        if ($y -gt 0) {
            $n = $i - $w
            if ($candidate[$n] -and -not $outside[$n]) { $outside[$n] = $true; $queue.Enqueue($n) }
        }
        if ($y -lt ($h - 1)) {
            $n = $i + $w
            if ($candidate[$n] -and -not $outside[$n]) { $outside[$n] = $true; $queue.Enqueue($n) }
        }
    }

    $result = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $i = ($y * $w) + $x
            $p = $InputBitmap.GetPixel($x, $y)
            if ($outside[$i]) {
                $result.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
            }
            else {
                # Alles am eigentlichen Logo bleibt vollständig deckend.
                $result.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $p.R, $p.G, $p.B))
            }
        }
    }
    return $result
}

$sourceFull = Resolve-RepoPath $SourcePath
$pngFull = Resolve-RepoPath $PngOutputPath
$icoFull = Resolve-RepoPath $IcoOutputPath

if (-not (Test-Path $sourceFull)) { throw "OrdnerSync logo source not found: $sourceFull" }

$source = [System.Drawing.Bitmap]::new($sourceFull)
try {
    # 1) Vollständiges Logo: nur den verbundenen weißen Außenbereich entfernen.
    $fullTransparent = Remove-ConnectedWhiteBackground -InputBitmap $source
    try {
        $source.Dispose()
        $fullTransparent.Save($sourceFull, [System.Drawing.Imaging.ImageFormat]::Png)

        # 2) Symbol ohne Text aus genau demselben Original-Logo ausschneiden.
        $sx = [int][Math]::Round($fullTransparent.Width * 245.0 / 1254.0)
        $sy = [int][Math]::Round($fullTransparent.Height * 195.0 / 1254.0)
        $sw = [int][Math]::Round($fullTransparent.Width * 770.0 / 1254.0)
        $sh = [int][Math]::Round($fullTransparent.Height * 650.0 / 1254.0)

        $crop = [System.Drawing.Bitmap]::new($sw, $sh, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $cg = [System.Drawing.Graphics]::FromImage($crop)
        try {
            $cg.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $cg.DrawImage(
                $fullTransparent,
                [System.Drawing.Rectangle]::new(0, 0, $sw, $sh),
                [System.Drawing.Rectangle]::new($sx, $sy, $sw, $sh),
                [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally { $cg.Dispose() }

        try {
            # Engen sichtbaren Bereich bestimmen.
            $minX=$sw; $minY=$sh; $maxX=-1; $maxY=-1
            for ($y=0; $y -lt $sh; $y++) {
                for ($x=0; $x -lt $sw; $x++) {
                    if ($crop.GetPixel($x,$y).A -gt 0) {
                        if ($x -lt $minX) {$minX=$x}; if ($y -lt $minY) {$minY=$y}
                        if ($x -gt $maxX) {$maxX=$x}; if ($y -gt $maxY) {$maxY=$y}
                    }
                }
            }
            if ($maxX -lt $minX) { throw "No visible icon pixels found." }

            $vw=$maxX-$minX+1; $vh=$maxY-$minY+1
            $visible=[System.Drawing.Bitmap]::new($vw,$vh,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $vg=[System.Drawing.Graphics]::FromImage($visible)
            try {
                $vg.CompositingMode=[System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $vg.DrawImage($crop,[System.Drawing.Rectangle]::new(0,0,$vw,$vh),[System.Drawing.Rectangle]::new($minX,$minY,$vw,$vh),[System.Drawing.GraphicsUnit]::Pixel)
            } finally {$vg.Dispose()}

            try {
                # Transparente PNG für die GUI.
                $masterSize=512
                $master=[System.Drawing.Bitmap]::new($masterSize,$masterSize,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                $mg=[System.Drawing.Graphics]::FromImage($master)
                try {
                    $mg.Clear([System.Drawing.Color]::Transparent)
                    $mg.CompositingMode=[System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                    $mg.CompositingQuality=[System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                    $mg.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $mg.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $pad=24
                    $scale=[Math]::Min(($masterSize-2*$pad)/[double]$vw,($masterSize-2*$pad)/[double]$vh)
                    $dw=[int][Math]::Round($vw*$scale); $dh=[int][Math]::Round($vh*$scale)
                    $dx=[int](($masterSize-$dw)/2); $dy=[int](($masterSize-$dh)/2)
                    $mg.DrawImage($visible,[System.Drawing.Rectangle]::new($dx,$dy,$dw,$dh))
                } finally {$mg.Dispose()}
                $master.Save($pngFull,[System.Drawing.Imaging.ImageFormat]::Png)

                # ICO: außen transparent, Symbol selbst deckend.
                $sizes=@(16,24,32,48,64,128,256)
                $frames=@()
                foreach($size in $sizes) {
                    $frame=[System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                    $fg=[System.Drawing.Graphics]::FromImage($frame)
                    try {
                        $fg.Clear([System.Drawing.Color]::Transparent)
                        $fg.CompositingMode=[System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                        $fg.CompositingQuality=[System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                        $fg.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                        $fg.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                        $fg.DrawImage($master,[System.Drawing.Rectangle]::new(0,0,$size,$size))
                    } finally {$fg.Dispose()}

                    # Verhindert halbtransparentes "ausgewaschenes" Symbol:
                    # nur vollständig transparente Pixel bleiben transparent.
                    for ($y=0; $y -lt $size; $y++) {
                        for ($x=0; $x -lt $size; $x++) {
                            $p=$frame.GetPixel($x,$y)
                            if ($p.A -gt 0 -and $p.A -lt 255) {
                                $frame.SetPixel($x,$y,[System.Drawing.Color]::FromArgb(255,$p.R,$p.G,$p.B))
                            }
                        }
                    }

                    $ms=[System.IO.MemoryStream]::new()
                    try {
                        $frame.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png)
                        $frames += [PSCustomObject]@{Size=$size;Bytes=$ms.ToArray()}
                    } finally {$ms.Dispose();$frame.Dispose()}
                }

                $stream=[System.IO.File]::Open($icoFull,[System.IO.FileMode]::Create,[System.IO.FileAccess]::Write)
                $writer=[System.IO.BinaryWriter]::new($stream)
                try {
                    $writer.Write([UInt16]0);$writer.Write([UInt16]1);$writer.Write([UInt16]$frames.Count)
                    $offset=6+(16*$frames.Count)
                    foreach($item in $frames) {
                        $dim=if($item.Size -eq 256){0}else{$item.Size}
                        $writer.Write([Byte]$dim);$writer.Write([Byte]$dim);$writer.Write([Byte]0);$writer.Write([Byte]0)
                        $writer.Write([UInt16]1);$writer.Write([UInt16]32)
                        $writer.Write([UInt32]$item.Bytes.Length);$writer.Write([UInt32]$offset)
                        $offset += $item.Bytes.Length
                    }
                    foreach($item in $frames){$writer.Write([Byte[]]$item.Bytes)}
                } finally {$writer.Dispose();$stream.Dispose()}
                $master.Dispose()
            } finally {$visible.Dispose()}
        } finally {$crop.Dispose()}
    }
    finally { $fullTransparent.Dispose() }
}
finally {
    if ($source) { try { $source.Dispose() } catch {} }
}

Write-Host "OrdnerSync full logo background removed: $sourceFull"
Write-Host "OrdnerSync transparent icon PNG generated: $pngFull"
Write-Host "OrdnerSync transparent Windows ICO generated: $icoFull"
