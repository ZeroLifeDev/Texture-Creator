# Architecture

PBR Reference Forge uses native WPF on .NET 8 for a small Windows distribution and predictable desktop integration. The core library owns model parsing, UV topology, project persistence, image correction, shared-surface PBR derivation, seam discovery, and export. The UI uses WPF `Viewport3D`, async background generation, and Windows Imaging Component codecs.

Every generated map derives from one corrected albedo/luminance representation. Metalness is driven by material classification rather than a grayscale photograph. Coverage remains a separate evidence map. The app labels height as inferred relief.

Quick mode has two projection adapters. Geometry mode rasterizes model triangles into UV space. Image-only mode classifies UV outline pixels against the border background, flood-fills the exterior, treats enclosed regions as UV islands, fits the reference into the target canvas, and preserves an explicit coverage mask. When a layout cannot be reliably enclosed, it uses a visible weak-confidence full-canvas fallback rather than failing silently.

External generation is hidden behind `IImageGenerationProvider`. The experimental ChatGPT adapter only prepares files and opens the normal user-facing site; the user logs in and attaches/downloads files manually. It never reads browser cookies, storage, tokens, or private APIs.

## v0.1 boundaries

OBJ and common interleaved GLTF/GLB primitives are supported. The alpha performs multi-view role-based orthographic UV reprojection with grazing-angle weighting and explicit confidence coverage. Sparse and compressed GLTF accessors, FBX, measured inverse rendering, EXR height export, automatic perspective camera calibration, on-mesh painting, CUDA inference, and automated Web Assist result retrieval are not yet implemented.
