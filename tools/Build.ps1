param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$antRoot=Split-Path $PSScriptRoot -Parent
$repoRoot=Split-Path $antRoot -Parent
$gmshProject=Join-Path $repoRoot 'Gmsh.Net\src\GMsh.Net\GMsh.Net.csproj'
$gmeshProject=Join-Path $repoRoot 'Geometry\GMesh\GMesh.csproj'
$antProject=Join-Path $antRoot 'Grasshopper\Ant.Grasshopper.csproj'
$geometryArtifacts=Join-Path $antRoot 'bin\geometry'

# Ant consumes the Release library outputs even in its Debug configuration.
& dotnet build $gmshProject -c Release -p:GeneratePackageOnBuild=false -p:NuGetAudit=false --ignore-failed-sources -v minimal
if($LASTEXITCODE) { throw 'Gmsh.Net build failed.' }
# Isolate GMesh and GPCGeometry outputs from Geometry's committed release binaries.
& dotnet build $gmeshProject -c Release --artifacts-path $geometryArtifacts -p:NuGetAudit=false --ignore-failed-sources -v minimal
if($LASTEXITCODE) { throw 'GMesh build failed.' }
& dotnet build $antProject -c $Configuration -p:NuGetAudit=false --ignore-failed-sources -v minimal
if($LASTEXITCODE) { throw 'Ant build failed.' }

$output=Join-Path $antRoot 'bin\net8.0-windows'
Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -in @('Ant.Grasshopper.gha','GMesh.dll','GMsh.Net.dll','GPCGeometry.dll') -or $_.Name -like 'gmsh-*.dll' } |
    Get-FileHash | Select-Object Path,Hash
