param(
    [string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$ManifestPath = (Join-Path $PSScriptRoot 'dependencies.json')
)
$ErrorActionPreference = 'Stop'
$git = 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1) { throw 'Unsupported dependency manifest schema.' }
foreach ($repo in $manifest.repositories) {
    $path = Join-Path $RepositoryRoot $repo.name
    $head = & $git -c safe.directory=* -C $path rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $head -ne $repo.commit) { throw "Dependency commit mismatch: $($repo.name). Expected $($repo.commit), got $head." }
    $changes = & $git -c safe.directory=* -C $path status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0 -or $changes) { throw "Dependency checkout must be clean: $($repo.name)." }
}
foreach ($file in $manifest.files) {
    $path = Join-Path $RepositoryRoot $file.path
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing dependency: $path" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Dependency SHA-256 mismatch: $($file.path)" }
}
Write-Host 'Dependency commits and SHA-256 verified.'
