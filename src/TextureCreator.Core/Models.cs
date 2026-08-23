using System.Numerics;
using System.Text.Json.Serialization;

namespace TextureCreator.Core;

public sealed class MeshData
{
    public string Name { get; set; } = "Mesh";
    public List<Vector3> Positions { get; } = [];
    public List<Vector3> Normals { get; } = [];
    public List<Vector2> TexCoords { get; } = [];
    public List<MeshVertex> Vertices { get; } = [];
    public List<int> Indices { get; } = [];
    public List<string> MaterialSlots { get; } = [];
    [JsonIgnore] public bool HasUvs => Vertices.Count > 0 && Vertices.All(v => v.TexCoord >= 0);
    [JsonIgnore] public int TriangleCount => Indices.Count / 3;
}

public readonly record struct MeshVertex(int Position, int TexCoord, int Normal);

public enum ReferenceRole { Custom, Front, Back, Left, Right, Top, Bottom }
public enum MaterialKind { Dielectric, Fabric, Leather, Plastic, Rubber, Wood, Stone, PaintedMetal, BareMetal, Skin, Glass, Custom }
public enum MapKind { Diffuse, Albedo, Roughness, Normal, Height, Metalness, AmbientOcclusion, Coverage }

public sealed class ReferenceImage
{
    public string Path { get; set; } = "";
    public ReferenceRole Role { get; set; }
    public float Priority { get; set; } = 1;
    public float OverlayOpacity { get; set; } = .55f;
    public CameraAlignment Alignment { get; set; } = new();
}

public sealed class CameraAlignment
{
    public Vector3 Rotation { get; set; }
    public Vector3 Translation { get; set; }
    public float Scale { get; set; } = 1;
    public float FieldOfView { get; set; } = 45;
    public Matrix4x4 ViewProjection { get; set; } = Matrix4x4.Identity;
}

public sealed class ForgeProject
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "Untitled";
    public string? ModelPath { get; set; }
    public List<ReferenceImage> References { get; set; } = [];
    public int TextureResolution { get; set; } = 2048;
    public MaterialKind DefaultMaterial { get; set; } = MaterialKind.Dielectric;
    public float Roughness { get; set; } = .55f;
    public float Metalness { get; set; }
    public float NormalStrength { get; set; } = 1;
    public Dictionary<string, string> GeneratedMaps { get; set; } = [];
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ImageBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public ImageBuffer(int width, int height, byte[]? rgba = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height; Pixels = rgba ?? new byte[checked(width * height * 4)];
        if (Pixels.Length != width * height * 4) throw new ArgumentException("RGBA buffer size does not match dimensions.");
    }
    public int Offset(int x, int y) => (y * Width + x) * 4;
    public ImageBuffer Clone() => new(Width, Height, (byte[])Pixels.Clone());
}

public sealed class TextureSet
{
    public Dictionary<MapKind, ImageBuffer> Maps { get; } = [];
    public ImageBuffer this[MapKind kind] => Maps[kind];
}

public readonly record struct SeamPair(Vector2 A0, Vector2 A1, Vector2 B0, Vector2 B1);
