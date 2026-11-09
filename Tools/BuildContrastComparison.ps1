param(
    [string]$AfterImage = 'Preview_Contrast_After_v10.png'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$visualRoot = 'C:/Users/Yannis/.codex/visualizations/2026/11/09/01a1c188-52f9-7163-8b3c-1b2e7a713777'
$reportRoot = 'C:/Protergia/Blender/ReferenceHouse'
$fragmentPath = Join-Path $visualRoot 'house-contrast.html'
$templatePath = Join-Path $visualRoot 'house-contrast.template'
$fragment = [IO.File]::ReadAllText($templatePath)
$codec = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
$sources = [ordered]@{ BEFORE = 'Preview_Contrast_Before_v10.png'; AFTER = $AfterImage }
foreach ($variant in $sources.Keys) {
    $sourcePath = if ([IO.Path]::IsPathRooted($sources[$variant])) { $sources[$variant] } else { Join-Path $reportRoot $sources[$variant] }
    $source = [Drawing.Image]::FromFile($sourcePath)
    $bitmap = [Drawing.Bitmap]::new(1024, 768)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    $parameters = [Drawing.Imaging.EncoderParameters]::new(1)
    try {
        if ([Math]::Abs($source.Width / $source.Height - 4 / 3) -gt 0.001) { throw "Unexpected image framing: $sourcePath" }
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($source, 0, 0, 1024, 768)
        $parameters.Param[0] = [Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality, [long]86)
        $bitmap.Save($stream, $codec, $parameters)
        $data = 'data:image/jpeg;base64,' + [Convert]::ToBase64String($stream.ToArray())
        $fragment = $fragment.Replace(($variant + '_IMAGE_DATA'), $data)
    }
    finally {
        $parameters.Dispose()
        $stream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
        $source.Dispose()
    }
}
if ($fragment.Contains('IMAGE_DATA') -or $fragment.Contains('\"')) { throw 'Unresolved or escaped fragment content' }
$size = [Text.Encoding]::UTF8.GetByteCount($fragment)
if ($size -ge 1000000) { throw "Comparison exceeds size budget: $size" }
[IO.File]::WriteAllText($fragmentPath, $fragment, [Text.UTF8Encoding]::new($false))
Write-Output "Comparison ready: $size bytes; after image: $AfterImage"
