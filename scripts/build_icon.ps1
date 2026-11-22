param(
    [string]$Source = (Join-Path $PSScriptRoot '../src/Mizu.Widget/Assets/mizu-source.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '../src/Mizu.Widget/Assets/mizu.ico')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$sourcePath = [System.IO.Path]::GetFullPath($Source)
$outputPath = [System.IO.Path]::GetFullPath($Destination)
$image = [System.Windows.Media.Imaging.BitmapDecoder]::Create(
    [Uri]$sourcePath, [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
    [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad).Frames[0]
$image = [System.Windows.Media.Imaging.FormatConvertedBitmap]::new($image, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
$stride = $image.PixelWidth * 4
$pixels = [byte[]]::new($stride * $image.PixelHeight)
$image.CopyPixels($pixels, $stride, 0)
$left = $image.PixelWidth; $top = $image.PixelHeight; $right = -1; $bottom = -1
for ($y = 0; $y -lt $image.PixelHeight; $y++) {
    for ($x = 0; $x -lt $image.PixelWidth; $x++) {
        if ($pixels[$y * $stride + $x * 4 + 3] -ge 8) {
            $left = [Math]::Min($left, $x); $right = [Math]::Max($right, $x)
            $top = [Math]::Min($top, $y); $bottom = [Math]::Max($bottom, $y)
        }
    }
}
if ($right -lt $left) { throw 'The source icon is fully transparent.' }
$crop = [System.Windows.Media.Imaging.CroppedBitmap]::new($image,
    [System.Windows.Int32Rect]::new($left, $top, $right - $left + 1, $bottom - $top + 1))
$sizes = @(16,20,24,32,40,48,64,96,128,256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $scale = $size * .9 / [Math]::Max($crop.PixelWidth, $crop.PixelHeight)
    $width = $crop.PixelWidth * $scale; $height = $crop.PixelHeight * $scale
    $visual = [System.Windows.Media.DrawingVisual]::new()
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [System.Windows.Media.BitmapScalingMode]::HighQuality)
    $drawing = $visual.RenderOpen()
    try { $drawing.DrawImage($crop, [System.Windows.Rect]::new(($size - $width) / 2, ($size - $height) / 2, $width, $height)) }
    finally { $drawing.Close() }
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $buffer = [System.IO.MemoryStream]::new()
    try { $encoder.Save($buffer); $frames.Add($buffer.ToArray()) }
    finally { $buffer.Dispose() }
}
$writer = [System.IO.BinaryWriter]::new([System.IO.File]::Create($outputPath))
try {
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $encodedSize = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$encodedSize); $writer.Write([byte]$encodedSize)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([UInt16]1); $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frames[$i].Length); $writer.Write([UInt32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose() }
Write-Output ("Created {0}: {1}" -f $outputPath, ($sizes -join ', '))
