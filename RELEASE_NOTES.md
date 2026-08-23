# PBR Reference Forge v0.2.0-alpha

This alpha makes the most common workflow the main screen: import a UV-mapped model, import one texture reference, and receive the six requested PBR maps in a ZIP.

## New in v0.2.0-alpha

- New Quick Texture Export is now the default screen
- Clear UV model and texture-reference selection cards with live validation
- One-click `Generate PBR ZIP` workflow
- ZIP contains exactly:
  - `Asset_Diffuse.png`
  - `Asset_Albedo.png`
  - `Asset_Roughness.png`
  - `Asset_Normal.png`
  - `Asset_Displacement.png`
  - `Asset_Metalness.png`
- Resolution selection for 1K, 2K, 4K and hardware-permitting 8K output
- Material classification selector on the quick screen
- Existing multi-reference 3D/UV workflow retained as an optional Advanced Workspace
- Release now provides both a directly runnable standalone EXE and a portable ZIP

## Existing pipeline

- OBJ, GLB and GLTF mesh import with explicit missing-UV rejection
- UV-space reference projection, coverage tracking and coherent PBR derivation
- Local processing with no paid API requirement
- Project save/open, realtime preview, logging and consent-gated Web Assist in Advanced Workspace

## Requirements

- Windows 10 22H2 or Windows 11, x64
- No separate .NET runtime or GPU required
- 8K output requires substantial system memory; 2K is the default

## Limitations / known issues

- Alpha-quality unsigned executable; Windows SmartScreen may warn
- Quick mode uses the selected reference as a Front projection
- Perspective camera solving and alignment gizmos remain in development
- Displacement is inferred 8-bit PNG rather than measured 16-bit/EXR geometry
- No FBX, UDIM, full paint-layer system, CUDA inference or local ML model manager yet
- GLTF sparse accessors and Draco/meshopt compression are unsupported

## Verification

- Release build: 0 warnings, 0 errors
- Automated tests: 13 passed, 0 failed
- Quick ZIP test verifies the exact six output maps
- Standalone EXE clean-directory startup and responsiveness smoke test passed
