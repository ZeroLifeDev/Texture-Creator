# PBR Reference Forge v0.3.1-alpha

This patch release replaces direct luminance displacement with a constrained, multi-scale relief estimator validated on detailed cracked concrete.

## Fixed and verified in v0.3.1-alpha

- Removes broad illumination gradients before deriving height
- Keeps dark cracks and cavities recessed instead of embossing them outward
- Separates structural relief from capped micro-detail to prevent geometry spikes
- Writes neutral mid-height outside observed UV islands
- Adds a depth-friendly ChatGPT Web Assist prompt for flat, shadow-neutral material references
- Adds regression tests for recessed cracks, gradient suppression and prompt requirements
- Validated the generated 2K maps with real subdivided geometry displacement in Blender 5.0
- Automated test suite: 16 passed, 0 failed

## Previously added in v0.3.0-alpha

This release adds UV layout images as a first-class alternative to 3D models and provides a safe browser-based ChatGPT account chooser.

- New Quick Texture Export is now the default screen
- UV Source card now accepts either a UV-mapped 3D model or a UV layout image
- Image-only projection detects boundaries, flood-fills enclosed UV islands and pads their edges
- Low-confidence UV layouts fall back visibly instead of silently pretending geometry exists
- Added **Sign in / Choose ChatGPT Account**, which opens normal ChatGPT web account selection and never accesses cookies, credentials or tokens
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
- Added a supported `--quick-export model reference output.zip [resolution]` executable route for repeatable full-pipeline validation and batch use
- Added `--quick-export-uv uv-layout.png reference.png output.zip [resolution]` for repeatable image-only validation and batch use
- Exercised the packaged pipeline on the bundled UV asset; all six output maps decoded successfully at the requested dimensions

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
- Image-only mode cannot infer 3D camera angles or occluded geometry; it fits the reference into detected UV islands
- Perspective camera solving and alignment gizmos remain in development
- Displacement is inferred 8-bit PNG rather than measured 16-bit/EXR geometry
- No FBX, UDIM, full paint-layer system, CUDA inference or local ML model manager yet
- GLTF sparse accessors and Draco/meshopt compression are unsupported

## Verification

- Release build: 0 warnings, 0 errors
- Automated tests: 14 passed, 0 failed
- Quick ZIP test verifies the exact six output maps
- UV layout closed-island detection/fill test passed
- Image-only executable export produced and decoded all six requested maps
- Standalone EXE clean-directory startup and responsiveness smoke test passed
