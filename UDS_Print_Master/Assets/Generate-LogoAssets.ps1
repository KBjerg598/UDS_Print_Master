[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$assetsDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$sourcePath = Join-Path $assetsDirectory "UISI-Logo-BW.png"
$headerPath = Join-Path $assetsDirectory "UISI-Logo-Header.png"
$iconPath = Join-Path $assetsDirectory "UISI-Logo-BW.ico"
$iconSizes = @(16, 24, 32, 48, 64, 128, 256)

function New-ResizedPngBytes {
    param(
        [Drawing.Image]$Source,
        [int]$Size
    )

    $bitmap = New-Object Drawing.Bitmap(
        $Size,
        $Size,
        [Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $bitmap.SetResolution(96, 96)

    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::White)
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $attributes = New-Object Drawing.Imaging.ImageAttributes
        try {
            $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $destination = New-Object Drawing.Rectangle(0, 0, $Size, $Size)
            $graphics.DrawImage(
                $Source,
                $destination,
                0,
                0,
                $Source.Width,
                $Source.Height,
                [Drawing.GraphicsUnit]::Pixel,
                $attributes)
        }
        finally {
            $attributes.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    $stream = New-Object IO.MemoryStream
    try {
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

$source = [Drawing.Image]::FromFile($sourcePath)
try {
    [IO.File]::WriteAllBytes($headerPath, (New-ResizedPngBytes $source 256))

    $frames = foreach ($size in $iconSizes) {
        [pscustomobject]@{
            Size = $size
            Bytes = [byte[]](New-ResizedPngBytes $source $size)
        }
    }

    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)

        $offset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $writer.Write([byte]($(if ($frame.Size -eq 256) { 0 } else { $frame.Size })))
            $writer.Write([byte]($(if ($frame.Size -eq 256) { 0 } else { $frame.Size })))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }

        foreach ($frame in $frames) {
            $writer.Write([byte[]]$frame.Bytes)
        }

        [IO.File]::WriteAllBytes($iconPath, $stream.ToArray())
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Host "Logo assets generated."
