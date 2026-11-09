# Ant

Grasshopper tools for Rhino 8 on Windows x64 / .NET 8: Gmsh-based planar
meshing, geometry and mesh cleanup, mesh inspection, user data and workflow utilities.

Current project version: **1.1.3**. Latest Git tag: **v1.1.0**;
version 1.1.3 is not tagged.
See [CHANGELOG.md](CHANGELOG.md) for release notes.
The plugin version is defined in `Grasshopper/Ant.Grasshopper.csproj` and is
included in the compiled assembly and Grasshopper plugin information.

Version 1.1.2 uses GMesh 1.2.0.1 on .NET 8 and fixes meshing constraints that
reach boundaries or cross holes. Version 1.1.1 fixes a Rhino hang in the shared load-time library check.
The check warns about builds from a different checkout commit and shared GPC
libraries that differ from the copies shipped beside Ant.

## Components

The **Ant** tab contains the following components, grouped by their actual
Grasshopper subcategory. Mesh workflows are described in detail below.

| Category | Components |
| --- | --- |
| Mesh | Planar Mesh; Mesh Refinement; Clean Geometry; Clean and Merge Mesh; Mesh Quality; Mesh Topology; Mesh Parts; Extract Mesh Faces; Match Mesh Nodes; Match Mesh Faces; Orient Mesh Normals; Triangulate Mesh |
| Data | Add User Data; Set User Data; Get User Data; Get User Values; Bake User Data; Update Geometry User Data |
| Query | Get Properties; Filter Properties; Filter User Data |
| Reference | Reference Objects |
| Utilities | Auto Orient; Get Document Path; Get Param Data; Grid Dispose; Persistent Counter; Set Gene Pool; SubString |
| Workflow | If; Switch; Stop; Recompute |
| Geometry | Join offset curves |

There are 34 visible components and one hidden **CleanGeometry (Legacy)**
component for loading existing definitions with their original ports.
Data, Query and Reference tools work with geometry attributes and Rhino document
objects. Workflow tools select values or input trees, gate data with Stop/Go,
and trigger recomputation. Utilities cover orientation, grid placement, document
and parameter access, counters, gene pools and string extraction.

## Grasshopper examples

Three self-contained `.gh` definitions include embedded geometry, Italian canvas
notes, adjustable tolerances and PNG previews: planar meshing with a hole and
refinement, mesh cleanup/quality/parts, and node/face matching with extraction.
See [examples/README.md](examples/README.md) for instructions and expected results.
The reproducible generator in `tools/Examples` saves, reopens and recomputes each
file, checking the resulting geometry and connections.

## Build

The sibling repositories `Geometry`, `Gmsh.Net` and `Utilities` must be present.
`Geometry` supplies GPCGeometry (netstandard2.0) and GMesh 1.2.0.1 (net8.0);
`Gmsh.Net` supplies the managed binding (netstandard2.0) and the native Windows DLL;
`Utilities` supplies GPCUtilities (netstandard2.0). Use the Geometry checkout
containing the GMesh .NET 8 migration and boundary-constraint fix, consolidated
on local master at commit `8926089` (GMesh 1.2.0.1); this commit has not yet been
pushed to the remote. Ant references Release
dependencies even when Ant itself is built in Debug.
Use a .NET 9 SDK compatible with Geometry's `global.json` and the .NET 8 x64
runtime. Visual Studio MSBuild and .NET Framework targeting packs are no longer
required for GMesh. Build `GPCUtilities` in Release before the first build.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build.ps1
```

This builds Gmsh.Net, GMesh and GPCGeometry before Ant. Geometry build outputs
are isolated in `Ant/bin/geometry` to preserve Geometry's committed release
binaries; Ant references the GMesh and GPCGeometry DLLs from that same build.
The plugin and its dependencies are
written to `bin/net8.0-windows`. Distribute the GHA together with the DLLs in that
directory. RhinoCommon and Grasshopper are supplied by Rhino itself.
The Gmsh native DLL version is read from the local Gmsh.Net project during build.
The verified native version is Gmsh 4.15.2 (`gmsh-4.15.dll`), with
Gmsh.Net 4.15.2.1 and GMesh 1.2.0.1.
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
user-installed plugins. The complete suite currently contains 85 checks,
including cleanup diagnostics, all eight mesh tools, indexed matching against
exhaustive search, source-index maps, vertex attributes, double precision,
embedded icons and loading components saved with their original ports.

## Icons

All 36 PNG resources are 24x24 pixels with transparent backgrounds, following
the [Grasshopper icon guidelines](https://developer.rhino3d.com/guides/grasshopper/grasshopper-icons/).
Editable SVGs are in `Grasshopper/Resources/Vector`; the reproducible drawing
source is `tools/Generate-Icons.ps1`. Regenerate with Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Generate-Icons.ps1
```

`tools/icons-preview.png` shows the icons at native size and enlarged.

## Mesh input behavior

### Execution and timings

New Planar Mesh components default to **single-threaded GH solving**. The GH
task option remains available and is saved with the component. Existing files
without a saved task preference use the new default. The appended **I** output
reports preparation, Gmsh generation (including any wait), conversion and total
wall-clock timings. Original mesh output position and component GUID are unchanged.

The current GMesh library serializes native generation with a process-wide lock.
Several GH tasks can overlap preprocessing, but cannot run separate Gmsh models
concurrently in the same process. The conversion no longer creates a nested task
for every output mesh; source vertex IDs are mapped directly, with double precision.

Gmsh's own internal parallelism is a separate setting: its 1D/2D algorithms can
mesh multiple curves/surfaces concurrently. See the
[Gmsh parallel meshing documentation](https://gmsh.info/doc/texinfo/#Mesh-module).
Ant does not currently expose or override those native thread options. Independent
worker processes would be appropriate for large independent jobs, but require a
separate worker protocol and are not implemented here. Use the I timings on your
actual model before choosing GH task mode; it is not automatically faster.

### Spatial searches and measured performance

RTree broad-phase searches now cover boundary snapping, boundary T-junctions,
host-face candidates, constraint carriers/representatives, point-on-line queries,
constraint deduplication and Planar Mesh input assignment. Exact geometry checks
still decide whether a candidate is valid. Candidate IDs retain insertion order
where it affects snapping. Clean and Merge Mesh already used an RTree for nearby
vertices and now reduces diagnostic allocations with typed face signatures and
lightweight vertex/normal bookkeeping.

A local comparison on 2026-09-26 used the same Rhino test runner, 36 separated
panels and 216 projected lines, with one warm-up and the median of three runs:

| Clean Geometry | Time | Managed allocations on the calling thread |
| --- | ---: | ---: |
| Published 1.0.0 | 97.61 ms | 20,198,104 bytes |
| Indexed implementation | 11.92 ms | 2,421,080 bytes |

This example is approximately 8.2x faster with 88% fewer managed allocations;
it is not a speed guarantee for other geometries or total native memory usage.
Both runs returned 36 surfaces and 216 lines. Regression checks cover holes,
clipping, duplicate constraints and projections. To run this benchmark after
building the regression runner:

```powershell
$env:ANT_CLEANUP_BENCHMARK = '1'
try { dotnet run --project .\tests\Regression\Ant.Regression.csproj -c Release --no-build }
finally { Remove-Item Env:ANT_CLEANUP_BENCHMARK }
```

### Input preparation

Each planar Brep face produces its own shape and retains its inner loops.
An embedded curve must fit entirely on one face without crossing a hole or
leaving a concave boundary between its endpoints; partial matches are not
committed. Region refinements must also fit the host's trimmed area. Use
Clean Geometry to clip/split constraints before meshing. Unmatched geometry
produces a warning. Invalid surface preparation
stops mesh generation instead of returning partial results. Gmsh warnings are
reported without suppressing failed embedded-node recovery.

GMesh 1.2.0.1 fixes the former `Unknown OpenCASCADE point` failure for boundary
and hole constraints: native vertex coordinates are read directly, rather than
from tolerance-expanded bounding boxes. The updated first example includes a
boundary-to-boundary line and a line through the hole; Clean Geometry clips out
the hole portion before Planar Mesh. Verification checks area 92, an open hole
and continuous mesh-edge chains along all retained constraint segments.
The Geometry library adds 10 regression tests, each at scales 1, 0.01 and 0.001,
including direct GMesh inputs through one or two holes. See
[examples/README.md](examples/README.md).

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

## Mesh management tools

The following components are available under **Ant > Mesh**. Inputs are not
modified. All indices are zero-based and refer to the input mesh/list in the
current Grasshopper branch. Use one mesh per branch (Graft M) when correlating
per-mesh lists. Tree maps append the solve iteration and then the face, edge or
part index to the input branch, so multiple input meshes cannot overwrite maps.

| Component | Purpose and key outputs |
| --- | --- |
| Mesh Quality (MQ) | Per-face area, longest/shortest edge ratio, minimum angle and quad warpage. F contains flagged face indices; D previews problematic geometry. |
| Mesh Topology (MT) | Raw and topology vertices, face vertex indices, unique edges, edge-to-face adjacency, naked/non-manifold edges and joined boundary loops. |
| Mesh Parts (MP) | Split by shared topology edges, optionally largest area first. Each output part has source-face and source-vertex maps. A vertex-only contact remains separate. |
| Extract Mesh Faces (MEF) | Selected mesh and remainder from face indices, with optional inversion and source-face/source-vertex maps for both results. |
| Match Mesh Nodes (MMN) | Match query points to closest raw vertices across a mesh list using an RTree with a shrinking nearest-search radius. |
| Match Mesh Faces (MMF) | Closest points on actual mesh faces, with source mesh and face indices; RTree mesh filtering followed by Rhino's cached mesh search. |
| Orient Mesh Normals (MN) | Unify normals, orient each closed connected part outward independently, and optionally flip. Preserves vertex/face indices. |
| Triangulate Mesh (MTR) | Split quads along the shortest valid diagonal; preserve all vertices and return the original face index for every triangle. |

Mesh Quality thresholds default to edge ratio 10, minimum angle 10 degrees and
maximum warpage equal to document tolerance. Warpage is the maximum distance of
a quad vertex from the plane of the other three vertices, in model units.
Triangles have zero warpage. Concave, folded and degenerate faces are always
flagged. These are geometric indicators, not solver-specific FEM quality metrics.
Connect **MQ.F to MEF.F** to isolate flagged faces. MT.N and MT.NM expose boundary
and non-manifold edges independently of cleaning.

Both match components accept **M** (mesh list), **P** (query points) and **D**
(maximum distance, default document tolerance; zero accepts exact matches only).
Outputs stay aligned with query order: unmatched queries return indices -1,
a null closest point, NaN distance and OK=false. Node-distance ties choose the
first mesh and vertex; a face query on a shared edge returns the face chosen by
Rhino's closest-point search. These are nearest searches, not one-to-one assignments.

Extraction and splitting retain vertex colors, texture coordinates, available
vertex normals and mesh user strings; they omit orphan vertices and n-gon groups.
Triangulation retains vertex attributes and user strings, recomputes normals and
clears n-gon groups. Custom attached UserData and external face properties are
not remapped by these tools. Source-index maps describe that single operation;
they are not persistent provenance IDs across arbitrary downstream edits.

Open meshes have no inferred outside. Orient Mesh Normals reports such parts
and unifies their winding; use Flip to choose the opposite side. A closed cavity
shell is also oriented outward independently, so nested material/cavity semantics
must be supplied explicitly by the user.

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

## User data robustness (1.1.3)

Set User Data preserves Rhino dictionary value types and last-value precedence for duplicate keys. Null or unsupported values produce key-specific warnings without overwriting existing data. Update Geometry User Data checks missing document/object references, updates copies, verifies replacement and records an undoable operation. Existing component IDs, ports and archive formats remain unchanged. Six additional regression checks include a Rhino .3dm round-trip and Undo.

