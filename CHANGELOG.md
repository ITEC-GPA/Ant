# Changelog

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
