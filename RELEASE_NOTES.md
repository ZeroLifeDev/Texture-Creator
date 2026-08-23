# PBR Reference Forge v0.1.0-alpha

This is the first public alpha of PBR Reference Forge, a self-contained native Windows tool for projecting reference photos into the UV space of an existing mesh and deriving a coherent metallic/roughness texture set locally.

## Implemented

- OBJ, GLB and GLTF mesh import with explicit UV validation
- Multiple Front/Back/Left/Right/Top/Bottom reference views
- UV-space orthographic projection with grazing-angle weighting, blending and confidence coverage
- Locally corrected albedo plus related roughness, metallic, normal, height, AO and coverage maps
- Material-aware PBR defaults, topology-based seam detection and UV padding primitives
- Realtime lit 3D orbit/zoom preview, reference overlay, UV inspector and map soloing
- Non-destructive `.tforge` save/open, autosave recovery, logs and background generation
- Engine-friendly PNG export names
- Consent-gated, semi-automatic ChatGPT Web Assist adapter using only the normal user-facing website

## Requirements

- Windows 10 22H2 or Windows 11, x64
- No separate .NET runtime, paid API or GPU required
- A CUDA/NVIDIA GPU is not currently used by the deterministic alpha pipeline

## Limitations / known issues

- Alpha-quality, unsigned build; Windows SmartScreen may display a warning
- Role-based orthographic alignment only; perspective lens solving and alignment gizmos are limited
- No full paint layer system, cross-island Poisson blending, UDIM, FBX, local ML model manager or CUDA inference yet
- Height is an inferred 8-bit PNG, not measured geometry or 16-bit/EXR displacement
- GLTF sparse accessors and Draco/meshopt compression are unsupported
- Preview is a practical WPF lit viewport, not a path-traced PBR renderer
- Web Assist is deliberately semi-automatic and requires manual login/upload/download

## Verification

- Release build: 0 warnings, 0 errors
- Automated tests: 11 passed, 0 failed
- Self-contained packaged executable: clean-directory startup, window-title and responsiveness smoke test passed
