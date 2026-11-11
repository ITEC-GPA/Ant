param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$antRoot=Split-Path $PSScriptRoot -Parent
$repoRoot=Split-Path $antRoot -Parent
$gmshProject=Join-Path $repoRoot 'Gmsh.Net\src\GMsh.Net\GMsh.Net.csproj'
$gmeshProject=Join-Path $repoRoot 'Geometry\GMesh\GMesh.csproj'
$antProject=Join-Path $antRoot 'Grasshopper\Ant.Grasshopper.csproj'
$geometryArtifacts=Join-Path $antRoot 'bin\geometry'

& (Join-Path $PSScriptRoot 'Test-Dependencies.ps1') -RepositoryRoot $repoRoot

# Ant consumes the Release library outputs even in its Debug configuration.
& dotnet build $gmshProject -c Release --artifacts-path (Join-Path $antRoot 'bin\gmsh') -p:GeneratePackageOnBuild=false -p:NuGetAudit=false --ignore-failed-sources -v minimal
if($LASTEXITCODE) { throw 'Gmsh.Net build failed.' }
# Isolate GMesh outputs; Ant references the pinned released GPCGeometry to share one binary with the other plugins.
& dotnet build $gmeshProject -c Release --artifacts-path $geometryArtifacts -p:NuGetAudit=false --ignore-failed-sources -v minimal
if($LASTEXITCODE) { throw 'GMesh build failed.' }
& dotnet build $antProject -c $Configuration -p:NuGetAudit=false --ignore-failed-sources -v minimal
if($LASTEXITCODE) { throw 'Ant build failed.' }

$output=Join-Path $antRoot 'bin\net8.0-windows'
Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -in @('Ant.Grasshopper.gha','GMesh.dll','GMsh.Net.dll','GPCGeometry.dll') -or $_.Name -like 'gmsh-*.dll' } |
    Get-FileHash | Select-Object Path,Hash

# A fresh publish directory cannot inherit DLLs left by older local builds.
$package=Join-Path $antRoot ('bin\packages\Ant-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
& dotnet publish $antProject -c $Configuration --no-restore -o $package -v minimal
if($LASTEXITCODE) { throw 'Ant package publish failed.' }
$binding=[xml](Get-Content $gmshProject -Raw)
$native='gmsh-' + $binding.Project.PropertyGroup.GmshNativeVersion + '.dll'
Copy-Item -LiteralPath (Join-Path $repoRoot "Gmsh.Net\dll\windows\$native") -Destination $package
$git='C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe'
$commit=& $git -c safe.directory=* -C $antRoot rev-parse HEAD
$dirty=@(& $git -c safe.directory=* -C $antRoot status --porcelain).Count -gt 0
$files=Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object {
    @{name=$_.FullName.Substring($package.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
}
@{antCommit=$commit;workingTreeDirty=$dirty;configuration=$Configuration;dependencies=(Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json);files=@($files)} |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $package 'build-manifest.json') -Encoding UTF8
Set-Content (Join-Path $antRoot 'bin\last-package.txt') $package
Write-Host "Package: $package"
