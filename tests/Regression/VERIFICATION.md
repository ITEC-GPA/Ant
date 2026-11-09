# Ant 1.1.6 verification — 2026-11-09

Four local steps, each tested before commit:

| Step | Version | Checks |
| --- | --- | --- |
| User data and Rhino document updates | 1.1.3 | 85 regression checks, including 6 new data/Undo/.3dm tests |
| Pinned dependency set and fresh publish package | 1.1.4 | 5 build checks, 85 regression checks against the package |
| Historical tolerance extraction | 1.1.5 | 90 regressions; 72 frozen IEEE754 values and geometric projection thresholds |
| Cooperative cancellation | 1.1.6 | 97 regressions, 5 build checks, 3 examples saved/reopened/recomputed |

Build: zero warnings/errors. Source repositories Geometry, Gmsh.Net and Utilities remained clean; their release bins were not rebuilt in place. No original example, component GUID, original port or archive format was changed.

The native cancellation gate is verified with controlled callbacks (including waiting and exceptions); these tests do not claim to interrupt Gmsh inside a native call. The actual native meshing and GH task/synchronous paths remain covered by the regression suite and examples. Already-running native work completes; its cancelled result is discarded.

Reproduce from the Ant root, with the pinned siblings and licensed Rhino 8 installed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-Build.ps1
$package = Get-Content bin/last-package.txt
dotnet run --project tests/Regression/Ant.Regression.csproj -c Release "-p:AntOutput=$package"
dotnet run --project tools/Examples/Ant.Examples.csproj -c Release "-p:AntOutput=$package" -- bin/verified-examples-1.1.6
```

Local evidence (ignored build artifacts): `bin/step1-tests.log`, `bin/step2-tests.log`, `bin/step3-tests.log`, `bin/step4-tests.log`, `bin/step4-examples.log`, `bin/step4-build.log`. The package's build-manifest.json identifies its source revision, dirty state and file hashes. The final clean-commit build is recorded separately in `bin/final-build.log` and `bin/final-tests.log`.
