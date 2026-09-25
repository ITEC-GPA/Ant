# Ant

Grasshopper tools for Rhino 8 on Windows x64 / .NET 8.

Current release: **1.0.0**. See [CHANGELOG.md](CHANGELOG.md) for release notes.
The plugin version is defined in `Grasshopper/Ant.Grasshopper.csproj` and is
included in the compiled assembly and Grasshopper plugin information.

## Build

The sibling repositories `Geometry`, `Gmsh.Net` and `Utilities` must be present.
The .NET 8+ SDK and Visual Studio MSBuild with .NET Framework 4.7.2 targeting
support are required. Build `GPCUtilities` in Release before the first build.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build.ps1
```

This builds Gmsh.Net and GMesh before Ant. The plugin and its dependencies are
written to `bin/net8.0-windows`. Distribute the GHA together with the DLLs in that
directory. RhinoCommon and Grasshopper are supplied by Rhino itself.
The Gmsh native DLL version is read from the local Gmsh.Net project during build.
The verified native version is Gmsh 4.15.2 (`gmsh-4.15.dll`), with
Gmsh.Net 4.15.2.1 and GMesh 1.0.1.3.
Restart Rhino after replacing assemblies that were already loaded.

## Regression checks

Run after building, on a machine with Rhino 8 installed and licensed:

```powershell
dotnet run --project .\tests\Regression\Ant.Regression.csproj -c Release
```

The runner starts Rhino without a window and uses temporary geometry/documents.
It checks multiface Breps and holes, constraint assignment to later faces,
atomic curve projection, failure handling, nonplanar face rejection, native mesh
generation and multiple Grasshopper branches with tasks enabled and disabled.
It also verifies refinement copies, transformations, validity and XML/binary
archive round-trips, including internalized data in a Grasshopper generic
parameter. Its isolated component server registers Ant without loading other
user-installed plugins. The complete suite currently contains 61 checks,
including cleanup diagnostics and loading components saved with their original ports.

## Icons

All 28 PNG resources are 24x24 pixels with transparent backgrounds, following
the [Grasshopper icon guidelines](https://developer.rhino3d.com/guides/grasshopper/grasshopper-icons/).
Editable SVGs are in `Grasshopper/Resources/Vector`; the reproducible drawing
source is `tools/Generate-Icons.ps1`. Regenerate with Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Generate-Icons.ps1
```

`tools/icons-preview.png` shows the icons at native size and enlarged.

## Mesh input behavior

Each planar Brep face produces its own shape and retains its inner loops.
An embedded curve must fit entirely on one face without crossing a hole or
leaving a concave boundary between its endpoints; partial matches are not
committed. Region refinements must also fit the host's trimmed area. Use
Clean Geometry to clip/split constraints before meshing. Unmatched geometry
produces a warning. Invalid surface preparation
stops mesh generation instead of returning partial results. Gmsh warnings are
reported without suppressing failed embedded-node recovery.

Point, line, polyline and planar region refinements are supported. Region holes
are preserved, and `Mesh Refinement` accepts both `GH_Line` and curve inputs.
All refinement types retain their type, mesh size and complete geometry when
copied or saved. Copies of curves, polylines and regions have independent
geometry. Geometry casts and bounding boxes are implemented; invalid geometry
or non-positive/non-finite sizes are reported as invalid data.

Transforms and morphs produce new refinements without changing their inputs.
Mesh size remains the explicitly specified value in model units, including
under scaling. Transformed objects lose their document reference ID; unchanged
duplicates and archives retain it. Morphs map vertices for points, lines and
polylines, and use Rhino's native morph for Breps. Nonplanar regions remain
unsuitable for Planar Mesh.

Polyline archives now store an explicit vertex count and indexed vertices,
preserving order, endpoints and closure. The previous writer reused a single
unindexed `Point` key and could not save a complete polyline. Such already
incomplete data cannot be reconstructed: loading reports an archive error with
instructions to recreate the refinement from its original curve. New archives
and the earlier point/line/region formats load normally.

## Clean Geometry (before Planar Mesh)

Add `Ant > Mesh > Clean Geometry` (`CG`) and connect its **S, E, R** outputs
to the corresponding inputs on `Planar Mesh`. **L** is a list of log messages.
The appended **C/D/U** outputs provide visual cleanup diagnostics (see below).
Inputs are copied; Grasshopper branches are processed separately. Flatten S/E/R
together when they belong to one model.

| Input | Meaning | Default when disconnected |
| --- | --- | --- |
| S | Planar Breps, including multiple faces and holes | Required |
| E | Embedded points, lines, curves or planar regions | Empty |
| R | Objects from Mesh Refinement, with their local mesh sizes | Empty |
| MT | Merge distance and minimum retained segment length, in model units | Rhino absolute tolerance, or 0.001 |
| PT | Projection distance to a surface/border, in model units | MT |
| CT | Chord deviation for curve approximation, in model units | MT / 10 |
| U | Boolean union of coplanar surfaces, removing internal panel boundaries | False |

All tolerances must be finite and positive. Start with the defaults, then
increase PT only as far as the intended gap/projection distance. CT controls
tessellation independently of vertex matching. Projection and merging are
successive operations, each with its own tolerance.

The cleaner aligns nearby parallel planes, matches adjacent boundary vertices
and edges, inserts boundary T-junctions and removes duplicate faces even when
their straight edges have different subdivisions. U optionally unions adjacent
or overlapping coplanar panels; holes remain holes unless covered by another
input surface. Nonparallel panels retain their planes.

Embedded/refinement curves are projected and split at face boundaries, holes,
crossings and overlaps. Nearby endpoints/points snap to vertices and segments;
nearly collinear overlapping lines share a carrier. Short and duplicate segments
are removed. Planar regions are clipped to the host faces, preserving holes.
Refinements retain their sizes; coincident geometry uses the smallest supplied
size, and a refinement takes precedence over an identical ordinary constraint.

The log reports tolerances, retained parts, excluded geometry/area, counts and
warnings with input indices. Geometry outside the faces, inside holes or beyond
PT is excluded and reported. Invalid/nonplanar surfaces or a collapsed or
self-intersecting boundary abort the operation with an error log instead of a
partial surface model. Inputs with unresolved constraint errors are reported
individually. This is preparation for planar meshing; it does not repair curved
surfaces or perform a general 3D solid Boolean union.

Existing files keep the old CleanGeometry GUID and S/E/T -> S/E/Info wiring
through a hidden compatibility component using the new cleanup engine. Place a
new **Clean Geometry** component to use R and the separate tolerances.

Regression checks cover border projection, duplicate/short geometry, holes,
panel gaps, optional union, angled planes, curve approximation, T-junctions,
overlaps, tolerance limits, retained refinement sizes, serialization, component
ports and actual native GMesh generation from the cleaned results.

## Clean and Merge Mesh

`Ant > Mesh > Clean and Merge Mesh` (`CMM`) is intended after `Planar Mesh`.
Connect its `M` input to a list of meshes. Grasshopper processes each branch
separately; flatten `M` to combine all branches into one output mesh.

| Input | Meaning | Default when disconnected |
| --- | --- | --- |
| M | List of meshes, copied before cleanup | Required |
| VT | Distance for merging nearby vertices, in model units | Rhino document absolute tolerance (0.001 without a document) |
| EL | Collapse edges shorter than this length, in model units | VT |
| FA | Remove faces with area at or below this value, in squared model units | VT squared |

All thresholds must be finite and non-negative. With VT = 0 only exactly
coincident vertices are merged. EL = 0 disables short-edge collapse. FA = 0
retains nonzero-area faces. Invalid/degenerate faces, duplicate faces (including
reversed duplicates) and unused vertices are cleaned independently of these
thresholds. Repairable degenerate quads become triangles. Face orientation and
normals are updated at the end.

The original outputs remain the cleaned mesh `M` and cleanup report `I`.
The appended **C/D/U** outputs provide visual cleanup diagnostics. The original input
meshes are not modified. Meshes are appended and their matching vertices are
merged; disconnected parts remain disconnected. This is not a Boolean union or
an intersection repair. Removing small faces can leave open boundaries.
Remaining short or non-manifold edges are reported as component warnings.

Nine additional regression checks cover seam merging, preservation of input
geometry and healthy quads, reversed duplicate faces, degenerate triangles/quads,
minimum area, short edges, disabled thresholds, invalid parameters, empty results
and the component's list input and mesh/report outputs.

## Visible cleanup diagnostics

Both Clean Geometry and Clean and Merge Mesh now append three geometry outputs.
All previous inputs, output indices and component GUIDs are preserved, including
those of the hidden legacy CleanGeometry component. Existing saved components
acquire the extra outputs when loaded.

| Output | Automatic wire color | Content |
| --- | --- | --- |
| C - Changed | Orange | Geometry after projection, snapping, splitting, repair or merging |
| D - Discarded | Red | Duplicate/short geometry, clipped curve or region portions, removed faces and unused points |
| U - Unresolved | Purple | Inputs that could not be processed and remaining short/non-manifold mesh edges |

The component draws the diagnostic colors automatically when its preview is on.
The standard green preview of these three output parameters is disabled to avoid
covering those colors. The outputs still contain ordinary Rhino geometry and can
be connected to separate parameters or Custom Preview. Turn off the Clean
component's preview when using your own preview setup. Diagnostics beyond the
cleaned geometry are included in the component's viewport bounding box.

Log entries such as `D[2] E[4]: ...` identify the diagnostic output item, its input
or cleanup stage and the reason. Indices are local to the Grasshopper branch.
Diagnostic geometry also carries `Ant.Diagnostic.Source`, `.Reason` and `.Kind`
user strings. These labels describe the current cleanup; they are not persistent
source-to-node/face provenance maps.

Changed geometry is shown at its resulting position; discarded geometry is
captured at the operation that removed it. Clipping exposes the excluded pieces,
including regions cut out by holes. Completely unmatched inputs remain at their
original positions in U. Surface errors return an empty primary result with U
and an error log. Null data has no drawable position and is described in the log.
Invalid meshes/Breps show available finite vertices or valid boundary fragments.

For mesh repairs or edge collapses, D can include an old face replaced by a new
face in C; that does not necessarily mean a hole remains in the final mesh.
Normal-only changes appear in C. Mesh face patches may combine multiple affected
faces from the same operation for an efficient preview; the log lists stage-local
face indices (abbreviated after 24). Diagnostic snapshots are independent copies:
editing them does not change the input or the primary cleaned outputs.
