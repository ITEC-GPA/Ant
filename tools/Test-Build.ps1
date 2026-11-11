$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$manifest=Join-Path $PSScriptRoot 'dependencies.json'
& "$PSScriptRoot/Test-Dependencies.ps1"
$passed=1
foreach($case in @('commit','missing','hash')) {
    $data=Get-Content $manifest -Raw | ConvertFrom-Json
    switch($case) {
        commit {$data.repositories[0].commit='0000000000000000000000000000000000000000'; $expected='commit mismatch'}
        missing {$data.files[0].path='Utilities/ant-missing-dependency-test.dll'; $expected='Missing dependency'}
        hash {$data.files[0].sha256='invalid'; $expected='SHA-256 mismatch'}
    }
    $path=Join-Path $root "bin/dependency-test-$case.json"
    $data | ConvertTo-Json -Depth 8 | Set-Content $path
    $rejected=$false
    try { & "$PSScriptRoot/Test-Dependencies.ps1" -ManifestPath $path }
    catch { if($_.Exception.Message -notlike "*$expected*"){throw}; $rejected=$true }
    if(!$rejected){throw "Invalid $case accepted"}
    $passed++
}
$package=Get-Content (Join-Path $root 'bin/last-package.txt')
$m=Get-Content (Join-Path $package 'build-manifest.json') -Raw | ConvertFrom-Json
foreach($file in $m.files) {
    if((Get-FileHash -LiteralPath (Join-Path $package $file.name)).Hash -ne $file.sha256){throw "Package hash mismatch: $($file.name)"}
}
foreach($name in @('Ant.Grasshopper.gha','GMesh.dll','GPCGeometry.dll','GPCUtilities.dll','GMsh.Net.dll','gmsh-4.15.dll')) {
    if($name -notin $m.files.name){throw "Missing packaged library: $name"}
}
if('RhinoCommon.dll' -in $m.files.name -or 'Grasshopper.dll' -in $m.files.name){throw 'Host libraries must not be shipped'}
$passed++
$geometryDependency = (Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json).files | Where-Object { $_.path -eq 'Geometry/GPCGeometry/bin/Release/netstandard2.0/GPCGeometry.dll' }
if (!$geometryDependency -or (Get-FileHash -LiteralPath (Join-Path $package 'GPCGeometry.dll')).Hash -ne $geometryDependency.sha256) { throw 'Package GPCGeometry must match the pinned shared release.' }
$passed++
Write-Host "$passed build checks passed."
