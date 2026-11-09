$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$visualRoot = 'C:/Users/Yannis/.codex/visualizations/2026/11/09/01a1c059-68a4-78a0-9ac2-aec06435026f'
$reportRoot = 'C:/Protergia/Blender/ReferenceHouse'
$fragmentPath = Join-Path $visualRoot 'lighting-comparison.html'
$templatePath = Join-Path $visualRoot 'lighting-comparison.template'
if (-not (Test-Path -LiteralPath $templatePath)) {
    Copy-Item -LiteralPath $fragmentPath -Destination $templatePath
}
$fragment = [IO.File]::ReadAllText($templatePath)
$codec = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
foreach ($variant in @('Before', 'After')) {
    $source = [Drawing.Image]::FromFile((Join-Path $reportRoot "Preview_Lighting_${variant}_v9.png"))
    $bitmap = [Drawing.Bitmap]::new(1024, 768)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    $parameters = [Drawing.Imaging.EncoderParameters]::new(1)
    try {
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($source, 0, 0, 1024, 768)
        $parameters.Param[0] = [Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality, [long]86)
        $bitmap.Save($stream, $codec, $parameters)
        $data = 'data:image/jpeg;base64,' + [Convert]::ToBase64String($stream.ToArray())
        $fragment = $fragment.Replace(($variant.ToUpperInvariant() + '_IMAGE_DATA'), $data)
    }
    finally {
        $parameters.Dispose()
        $stream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
        $source.Dispose()
    }
}
[IO.File]::WriteAllText($fragmentPath, $fragment, [Text.UTF8Encoding]::new($false))
$size = (Get-Item -LiteralPath $fragmentPath).Length
if ($size -ge 1000000) { throw "Comparison exceeds size budget: $size" }
if ($fragment.Contains('IMAGE_DATA') -or $fragment.Contains('\"')) { throw 'Unresolved fragment content' }
Write-Output "Comparison ready: $size bytes"
