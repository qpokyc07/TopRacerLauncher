# Генерация иконки TRLauncher: «стеклянный» тайл с монограммой TR и play-иконкой.
# Запуск: powershell -ExecutionPolicy Bypass -File tools\gen_icon.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$out = Join-Path $root "src\TopRacerLauncher\Assets\app.ico"
$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [double]([Math]::Max(1, [int]($size * 0.03)))
    $r = [double]([Math]::Max(2, [int]($size * 0.24)))
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc(($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
    $path.AddArc(($rect.Right - $d), ($rect.Bottom - $d), $d, $d, 0, 90)
    $path.AddArc($rect.X, ($rect.Bottom - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    # Градиент: синий → фиолетовый по диагонали.
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 64, 132, 255),
        [System.Drawing.Color]::FromArgb(255, 126, 74, 255),
        45)
    $g.FillPath($brush, $path)

    # Стеклянный блик сверху.
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(90, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
        90)
    $g.FillPath($hl, $path)

    # Тонкая светлая кромка.
    $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 255, 255, 255), [Math]::Max(1.0, $size * 0.012))
    $g.DrawPath($edge, $path)

    # Монограмма TR.
    $fontSize = [double]($size * 0.48)
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, ([System.Drawing.FontStyle]::Bold), [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF 0, ($size * 0.03), $size, $size
    $g.DrawString("TR", $font, [System.Drawing.Brushes]::White, $textRect, $sf)

    $g.Dispose()
    return $bmp
}

# Готовим PNG для каждого размера.
$images = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += [pscustomobject]@{ Size = $s; Data = $ms.ToArray() }
    $ms.Dispose()
    $bmp.Dispose()
}

# Собираем ICO (PNG-записи поддерживаются Windows Vista+).
$stream = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $stream
$w.Write([UInt16]0)
$w.Write([UInt16]1)
$w.Write([UInt16]$images.Count)

$offset = 6 + 16 * $images.Count
foreach ($e in $images) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $w.Write([byte]$dim)
    $w.Write([byte]$dim)
    $w.Write([byte]0)
    $w.Write([byte]0)
    $w.Write([UInt16]1)
    $w.Write([UInt16]32)
    $w.Write([UInt32]$e.Data.Length)
    $w.Write([UInt32]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $images) { $w.Write($e.Data) }
$w.Close()

Write-Host ("Иконка: {0} ({1:N0} KB, {2} размеров)" -f $out, ((Get-Item $out).Length / 1KB), $images.Count) -ForegroundColor Green
