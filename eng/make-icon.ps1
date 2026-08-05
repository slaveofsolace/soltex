# Generates the Soltex application icon as a multi-resolution PNG-compressed .ico.
# Palette matches the workspace theme: warm near-black ground, coral mark.

[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\src\Soltex.App\Soltex.ico')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$ground = [System.Drawing.Color]::FromArgb(255, 31, 31, 31)
$coral = [System.Drawing.Color]::FromArgb(255, 247, 111, 83)
$paper = [System.Drawing.Color]::FromArgb(255, 244, 240, 232)

function New-SoltexBitmap {
    param([int] $Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        # Rounded ground.
        $radius = [double] $Size * 0.22
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $radius * 2.0
        $path.AddArc(0, 0, $d, $d, 180, 90)
        $path.AddArc($Size - $d, 0, $d, $d, 270, 90)
        $path.AddArc($Size - $d, $Size - $d, $d, $d, 0, 90)
        $path.AddArc(0, $Size - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $groundBrush = New-Object System.Drawing.SolidBrush($ground)
        $g.FillPath($groundBrush, $path)
        $groundBrush.Dispose()

        # Solar arc: the mark reads as a horizon the coral rule crosses.
        $inset = [double] $Size * 0.24
        $arcBox = New-Object System.Drawing.RectangleF($inset, $inset, ($Size - ($inset * 2)), ($Size - ($inset * 2)))
        $penWidth = [Math]::Max(1.0, [double] $Size * 0.085)
        $arcPen = New-Object System.Drawing.Pen($coral, $penWidth)
        $arcPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $arcPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawArc($arcPen, $arcBox, 145, 250)
        $arcPen.Dispose()

        # Horizon rule.
        $ruleY = [double] $Size * 0.615
        $ruleInset = [double] $Size * 0.155
        $ruleWidth = [Math]::Max(1.0, [double] $Size * 0.065)
        $rulePen = New-Object System.Drawing.Pen($paper, $ruleWidth)
        $rulePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $rulePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($rulePen, [single] $ruleInset, [single] $ruleY, [single] ($Size - $ruleInset), [single] $ruleY)
        $rulePen.Dispose()

        $path.Dispose()
    }
    finally {
        $g.Dispose()
    }

    return $bitmap
}

function ConvertTo-IconDib {
    # Classic BITMAPINFOHEADER + bottom-up BGRA + AND mask. GDI+ and older icon
    # readers reject PNG-compressed entries below 256px, so only 256 uses PNG.
    param([System.Drawing.Bitmap] $Bitmap)

    $width = $Bitmap.Width
    $height = $Bitmap.Height
    $maskStride = [int](([Math]::Floor(($width + 31) / 32)) * 4)

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([uint32] 40)                       # biSize
        $writer.Write([int32] $width)                    # biWidth
        $writer.Write([int32] ($height * 2))             # biHeight: XOR + AND
        $writer.Write([uint16] 1)                        # biPlanes
        $writer.Write([uint16] 32)                       # biBitCount
        $writer.Write([uint32] 0)                        # biCompression: BI_RGB
        $writer.Write([uint32] (($width * $height * 4) + ($maskStride * $height)))
        $writer.Write([int32] 0)                         # biXPelsPerMeter
        $writer.Write([int32] 0)                         # biYPelsPerMeter
        $writer.Write([uint32] 0)                        # biClrUsed
        $writer.Write([uint32] 0)                        # biClrImportant

        for ($y = $height - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $width; $x++) {
                $pixel = $Bitmap.GetPixel($x, $y)
                $writer.Write([byte] $pixel.B)
                $writer.Write([byte] $pixel.G)
                $writer.Write([byte] $pixel.R)
                $writer.Write([byte] $pixel.A)
            }
        }

        # Alpha carries transparency, so the AND mask stays fully opaque.
        $maskRow = New-Object byte[] $maskStride
        for ($y = 0; $y -lt $height; $y++) {
            $writer.Write($maskRow)
        }

        $writer.Flush()
        # Comma operator: keep the byte[] intact instead of unrolling it.
        return , $stream.ToArray()
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$payloads = @()
foreach ($size in $sizes) {
    $bitmap = New-SoltexBitmap -Size $size
    try {
        if ($size -ge 256) {
            $stream = New-Object System.IO.MemoryStream
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $bytes = $stream.ToArray()
            }
            finally {
                $stream.Dispose()
            }
        }
        else {
            $bytes = ConvertTo-IconDib -Bitmap $bitmap
        }

        $payloads += , @{ Size = $size; Bytes = $bytes }
    }
    finally {
        $bitmap.Dispose()
    }
}

# ICONDIR + ICONDIRENTRY records, each payload stored as a PNG (Vista+ format).
$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($output)
try {
    $writer.Write([uint16] 0)                 # reserved
    $writer.Write([uint16] 1)                 # type: icon
    $writer.Write([uint16] $payloads.Count)

    $offset = 6 + (16 * $payloads.Count)
    foreach ($payload in $payloads) {
        $dimension = if ($payload.Size -ge 256) { 0 } else { $payload.Size }
        $writer.Write([byte] $dimension)      # width
        $writer.Write([byte] $dimension)      # height
        $writer.Write([byte] 0)               # palette colours
        $writer.Write([byte] 0)               # reserved
        $writer.Write([uint16] 1)             # colour planes
        $writer.Write([uint16] 32)            # bits per pixel
        $writer.Write([uint32] $payload.Bytes.Length)
        $writer.Write([uint32] $offset)
        $offset += $payload.Bytes.Length
    }

    foreach ($payload in $payloads) {
        $writer.Write($payload.Bytes)
    }

    $writer.Flush()

    $resolved = [System.IO.Path]::GetFullPath($OutputPath)
    $directory = [System.IO.Path]::GetDirectoryName($resolved)
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    [System.IO.File]::WriteAllBytes($resolved, $output.ToArray())
    Write-Output "Wrote $resolved ($($output.Length) bytes, $($payloads.Count) sizes)"
}
finally {
    $writer.Dispose()
    $output.Dispose()
}
