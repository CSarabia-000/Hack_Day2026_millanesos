param(
    [string]$Source = (Join-Path $PSScriptRoot '..\Mindie\Assets\LogoMark.png'),
    [string]$Output = (Join-Path $PSScriptRoot '..\Mindie\Assets\Mindie.ico')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourceImage = [Drawing.Bitmap]::FromFile([IO.Path]::GetFullPath($Source))
$sizes = @(16,24,32,48,64,128,256)
$frames = [Collections.Generic.List[byte[]]]::new()
try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($sourceImage,[Drawing.Rectangle]::new(0,0,$size,$size))
        } finally { $graphics.Dispose() }
        $memory = [IO.MemoryStream]::new()
        try {
            if ($size -eq 256) {
                $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
            } else {
                # Small icon frames use a DIB with an AND mask for Windows shell compatibility.
                $writer = [IO.BinaryWriter]::new($memory,[Text.Encoding]::UTF8,$true)
                try {
                    $maskStride = [int]([Math]::Ceiling($size/32.0)*4)
                    $writer.Write([uint32]40); $writer.Write([int32]$size); $writer.Write([int32]($size*2))
                    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
                    $writer.Write([uint32]($size*$size*4+$maskStride*$size))
                    foreach ($unused in 1..4) { $writer.Write([uint32]0) }
                    for ($y=$size-1; $y -ge 0; $y--) {
                        for ($x=0; $x -lt $size; $x++) {
                            $color = $bitmap.GetPixel($x,$y)
                            $writer.Write([byte[]]@($color.B,$color.G,$color.R,$color.A))
                        }
                    }
                    for ($y=$size-1; $y -ge 0; $y--) {
                        $mask = [byte[]]::new($maskStride)
                        for ($x=0; $x -lt $size; $x++) {
                            if ($bitmap.GetPixel($x,$y).A -eq 0) {
                                $index = [int][Math]::Floor($x/8)
                                $mask[$index] = $mask[$index] -bor (128 -shr ($x%8))
                            }
                        }
                        $writer.Write($mask)
                    }
                } finally { $writer.Dispose() }
            }
            $frames.Add($memory.ToArray())
        } finally { $memory.Dispose(); $bitmap.Dispose() }
    }
    $file = [IO.File]::Create([IO.Path]::GetFullPath($Output))
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6+16*$sizes.Count
        for ($i=0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose(); $file.Dispose() }
} finally { $sourceImage.Dispose() }
