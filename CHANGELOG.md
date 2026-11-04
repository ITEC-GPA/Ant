# Changelog

## 1.1.1 - 2026-11-04

- The load-time check (PluginBuildCheck.cs, identical to the copy in BridGHe)
  no longer hangs Rhino. A shared library (GPCGeometry, GPCUtilities) loaded
  first by a worker thread was reported with RhinoApp.WriteLine from that
  thread while the runtime held the load; WriteLine waits for Rhino's thread,
  and when that thread waited for the same library neither returned. The line
  is now queued to Rhino's thread without waiting. No component changes.

## 1.1.0 - 2026-09-26

- Added Mesh Quality, Mesh Topology, Mesh Parts, Extract Mesh Faces, Match Mesh
  Nodes, Match Mesh Faces, Orient Mesh Normals and Triangulate Mesh, with eight
  new transparent 24x24 icons and source-index maps where topology changes.
- Added RTree filtering to geometry cleanup and Planar Mesh input matching;
  retained exact geometry tests, tolerance semantics and deterministic candidate order.
- Reduced mesh cleanup diagnostic allocations and repeated full-mesh validation.
- Default new Planar Mesh instances to single-threaded GH solving, persist the
  task preference, append stage timings and remove nested conversion tasks.
- Map GMesh vertex IDs directly during conversion and preserve double precision.
- Correct the compiled resource namespace so all plugin icons can be loaded.
- Expanded regression checks from 61 to 79, including all components, resources,
  serialization, matching against exhaustive search and vertex attributes.
- On the documented 36-panel/216-line cleanup benchmark, median time fell from
  97.61 ms to 11.92 ms and managed allocations from 20,198,104 to 2,421,080 bytes.
  Performance depends on the input; this does not measure total native memory.

## 1.0.0 - 2026-09-25

First explicitly versioned release for Rhino 8 on Windows x64 / .NET 8.

### Added

- Clean Geometry before Planar Mesh, with separate surface, embedded geometry
  and refinement inputs; configurable merge, projection and chord tolerances;
  optional coplanar union; and a detailed log.
- Clean and Merge Mesh after the mesher, with duplicate vertex merging,
  short-edge collapse, small/degenerate face cleanup and a report.
- Colored Changed, Discarded and Unresolved diagnostic outputs on both cleaners,
  including source labels, clipped-away geometry and remaining mesh defects.
- 61 regression checks covering native meshing, geometry cleanup, mesh repair,
  refinement persistence, diagnostics and saved Grasshopper component ports.
- Build and icon-generation scripts, editable SVG icons and usage documentation.

### Fixed

- Multiface surface processing, holes, embedded constraint assignment and atomic
  failure handling during planar mesh preparation.
- Refinement validation, copying, transforms, geometry conversion and archives.
- Diagnostics on failed cleanup operations without exposing partial results.

### Updated

- All 28 plugin icons use transparent 24x24 PNGs.
- GMesh 1.0.1.3, Gmsh.Net 4.15.2.1 and native Gmsh 4.15.2.
- Native Gmsh copying follows the version declared by the sibling binding project.
- Assembly metadata and Grasshopper plugin information report version 1.0.0.

### Compatibility

- Existing component GUIDs and original output positions are preserved;
  diagnostic outputs are appended. The legacy Clean Geometry component remains
  available when loading existing definitions.
- Old incomplete polyline refinement archives cannot recover vertices that the
  previous writer never stored; recreate affected refinements from their source
  curves. New archives preserve all vertices and closure.
- Persistent geometry-to-mesh provenance is not part of this release.
