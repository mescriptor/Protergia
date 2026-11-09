$ErrorActionPreference = 'Stop'
$sourceRoot = 'C:\Protergia\Tools\UnityBuild\Assets\ReferenceHouse'
$destinationAssets = 'C:\Protergia\Assets'
$sourceMeshRoot = (Resolve-Path -LiteralPath "$sourceRoot\Meshes").Path
$sourceMeshPrefix = $sourceMeshRoot.TrimEnd('\') + '\'
$manifest = Get-Content -LiteralPath 'C:\Protergia\Blender\ReferenceHouse\UnityExport\scene.json' -Raw | ConvertFrom-Json
if ($manifest.stage -notlike 'V6*') { throw 'Only the final V6 export may be published.' }
$expectedMeshes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
[void]$expectedMeshes.Add('PresentationBackdrop.asset')
for ($i=0; $i -lt $manifest.meshes.Count; $i++) {
    $safeName = [regex]::Replace($manifest.meshes[$i].name,'[^\p{L}\p{Nd}_-]','_')
    if ($safeName.Length -gt 100) { $safeName = $safeName.Substring(0,100) }
    [void]$expectedMeshes.Add(('{0:D5}_{1}.asset' -f $i,$safeName))
}
$removedUnused = 0
foreach ($meshFile in Get-ChildItem -LiteralPath $sourceMeshRoot -File -Filter '*.asset') {
    if (-not $expectedMeshes.Contains($meshFile.Name)) {
        $checked = (Resolve-Path -LiteralPath $meshFile.FullName).Path
        if (-not $checked.StartsWith($sourceMeshPrefix,[System.StringComparison]::OrdinalIgnoreCase)) { throw 'Mesh cleanup escaped its generated folder.' }
        Remove-Item -LiteralPath $checked
        if (Test-Path -LiteralPath ($checked+'.meta')) { Remove-Item -LiteralPath ($checked+'.meta') }
        $removedUnused++
    }
}
Copy-Item -LiteralPath $sourceRoot -Destination $destinationAssets -Recurse -Force
Copy-Item -LiteralPath 'C:\Protergia\Tools\UnityBuild\Assets\ReferenceHouse.meta' -Destination 'C:\Protergia\Assets\ReferenceHouse.meta' -Force
$targetRoot = 'C:\Protergia\Assets\ReferenceHouse'
foreach ($relative in @('Scenes\ReferenceHouse.unity','Prefabs\ReferenceHouse.prefab','Settings\ReferenceHouse_URP.asset','Editor\ReferenceHouseImporter.cs')) {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $sourceRoot $relative) -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath (Join-Path $targetRoot $relative) -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) { throw "Published file differs: $relative" }
}
$report = [ordered]@{
    stage = $manifest.stage
    source = $sourceRoot
    destination = $targetRoot
    meshes = @(Get-ChildItem -LiteralPath "$targetRoot\Meshes" -File -Filter '*.asset').Count
    textures = @(Get-ChildItem -LiteralPath "$targetRoot\Textures" -File -Filter '*.png').Count
    scene = "$targetRoot\Scenes\ReferenceHouse.unity"
    prefab = "$targetRoot\Prefabs\ReferenceHouse.prefab"
    unusedPreliminaryMeshesRemoved = $removedUnused
    sceneAndPrefabHashesVerified = $true
}
$report | ConvertTo-Json | Set-Content -LiteralPath 'C:\Protergia\Blender\ReferenceHouse\Unity_PublishReport.json' -Encoding utf8
$report | ConvertTo-Json -Compress
