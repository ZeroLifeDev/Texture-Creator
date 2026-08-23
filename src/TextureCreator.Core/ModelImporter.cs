using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace TextureCreator.Core;

public static class ModelImporter
{
    public static MeshData Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Model file was not found.", path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".obj" => LoadObj(path),
            ".gltf" => LoadGltf(path, File.ReadAllBytes(path), null),
            ".glb" => LoadGlb(path),
            _ => throw new NotSupportedException("Supported model formats are OBJ, GLTF and GLB.")
        };
    }

    public static MeshData LoadObj(string path)
    {
        var mesh = new MeshData { Name = Path.GetFileNameWithoutExtension(path) };
        var map = new Dictionary<MeshVertex, int>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "v" when p.Length >= 4: mesh.Positions.Add(new(F(p[1]), F(p[2]), F(p[3]))); break;
                case "vt" when p.Length >= 3: mesh.TexCoords.Add(new(F(p[1]), 1 - F(p[2]))); break;
                case "vn" when p.Length >= 4: mesh.Normals.Add(Vector3.Normalize(new(F(p[1]), F(p[2]), F(p[3])))); break;
                case "usemtl" when p.Length > 1:
                    if (!mesh.MaterialSlots.Contains(p[1])) mesh.MaterialSlots.Add(p[1]);
                    break;
                case "f" when p.Length >= 4:
                    var face = p.Skip(1).Select(x => ParseVertex(x, mesh)).ToArray();
                    for (var i = 1; i < face.Length - 1; i++)
                        foreach (var v in new[] { face[0], face[i], face[i + 1] })
                        {
                            if (!map.TryGetValue(v, out var ix)) { ix = mesh.Vertices.Count; map[v] = ix; mesh.Vertices.Add(v); }
                            mesh.Indices.Add(ix);
                        }
                    break;
            }
        }
        Validate(mesh);
        return mesh;
    }

    private static MeshVertex ParseVertex(string value, MeshData mesh)
    {
        var p = value.Split('/');
        int Resolve(string s, int count) { if (string.IsNullOrWhiteSpace(s)) return -1; var i = int.Parse(s, CultureInfo.InvariantCulture); return i < 0 ? count + i : i - 1; }
        return new(Resolve(p[0], mesh.Positions.Count), p.Length > 1 ? Resolve(p[1], mesh.TexCoords.Count) : -1, p.Length > 2 ? Resolve(p[2], mesh.Normals.Count) : -1);
    }
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static MeshData LoadGlb(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 20 || Encoding.ASCII.GetString(data, 0, 4) != "glTF") throw new InvalidDataException("Invalid GLB header.");
        var offset = 12; byte[]? json = null; byte[]? bin = null;
        while (offset + 8 <= data.Length)
        {
            var len = BitConverter.ToInt32(data, offset); var type = BitConverter.ToUInt32(data, offset + 4); offset += 8;
            if (len < 0 || offset + len > data.Length) throw new InvalidDataException("Invalid GLB chunk.");
            if (type == 0x4E4F534A) json = data.AsSpan(offset, len).ToArray();
            if (type == 0x004E4942) bin = data.AsSpan(offset, len).ToArray();
            offset += len;
        }
        if (json is null) throw new InvalidDataException("GLB has no JSON chunk.");
        return LoadGltf(path, json, bin);
    }

    private static MeshData LoadGltf(string path, byte[] jsonBytes, byte[]? glbBin)
    {
        using var doc = JsonDocument.Parse(jsonBytes);
        var root = doc.RootElement;
        var buffers = new List<byte[]>();
        var baseDir = Path.GetDirectoryName(path)!;
        var bufferIndex = 0;
        foreach (var b in root.GetProperty("buffers").EnumerateArray())
        {
            if (b.TryGetProperty("uri", out var uri))
            {
                var u = uri.GetString()!;
                buffers.Add(u.StartsWith("data:") ? Convert.FromBase64String(u[(u.IndexOf(',') + 1)..]) : File.ReadAllBytes(Path.Combine(baseDir, Uri.UnescapeDataString(u))));
            }
            else if (bufferIndex == 0 && glbBin is not null) buffers.Add(glbBin);
            else throw new InvalidDataException("Missing GLTF buffer data.");
            bufferIndex++;
        }
        var views = root.GetProperty("bufferViews").EnumerateArray().ToArray();
        var accessors = root.GetProperty("accessors").EnumerateArray().ToArray();
        var mesh = new MeshData { Name = Path.GetFileNameWithoutExtension(path) };
        foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
        {
            var attrs = primitive.GetProperty("attributes");
            var pos = ReadFloats(attrs.GetProperty("POSITION").GetInt32(), 3);
            var uv = attrs.TryGetProperty("TEXCOORD_0", out var ue) ? ReadFloats(ue.GetInt32(), 2) : [];
            var normals = attrs.TryGetProperty("NORMAL", out var ne) ? ReadFloats(ne.GetInt32(), 3) : [];
            var posBase = mesh.Positions.Count; var uvBase = mesh.TexCoords.Count; var normBase = mesh.Normals.Count; var vertexBase = mesh.Vertices.Count;
            for (var i = 0; i < pos.Length; i += 3) mesh.Positions.Add(new(pos[i], pos[i + 1], pos[i + 2]));
            for (var i = 0; i < uv.Length; i += 2) mesh.TexCoords.Add(new(uv[i], uv[i + 1]));
            for (var i = 0; i < normals.Length; i += 3) mesh.Normals.Add(new(normals[i], normals[i + 1], normals[i + 2]));
            var count = pos.Length / 3;
            for (var i = 0; i < count; i++) mesh.Vertices.Add(new(posBase + i, uv.Length > 0 ? uvBase + i : -1, normals.Length > 0 ? normBase + i : -1));
            if (primitive.TryGetProperty("indices", out var ie)) foreach (var ix in ReadIndices(ie.GetInt32())) mesh.Indices.Add(vertexBase + ix);
            else for (var i = 0; i < count; i++) mesh.Indices.Add(vertexBase + i);
        }
        Validate(mesh); return mesh;

        (JsonElement a, JsonElement v, byte[] buf, int start, int stride) Info(int accessor)
        {
            var a = accessors[accessor]; if (a.TryGetProperty("sparse", out _)) throw new NotSupportedException("Sparse GLTF accessors are not supported in this release.");
            var v = views[a.GetProperty("bufferView").GetInt32()]; var buf = buffers[v.GetProperty("buffer").GetInt32()];
            var start = (v.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
            var stride = v.TryGetProperty("byteStride", out var st) ? st.GetInt32() : 0; return (a, v, buf, start, stride);
        }
        float[] ReadFloats(int accessor, int comps)
        {
            var (a, _, buf, start, stride) = Info(accessor); if (a.GetProperty("componentType").GetInt32() != 5126) throw new NotSupportedException("Only float vertex attributes are supported.");
            var count = a.GetProperty("count").GetInt32(); stride = stride == 0 ? comps * 4 : stride; var result = new float[count * comps];
            for (var i = 0; i < count; i++) for (var c = 0; c < comps; c++) result[i * comps + c] = BitConverter.ToSingle(buf, start + i * stride + c * 4); return result;
        }
        int[] ReadIndices(int accessor)
        {
            var (a, _, buf, start, stride) = Info(accessor); var count = a.GetProperty("count").GetInt32(); var type = a.GetProperty("componentType").GetInt32(); var size = type switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw new NotSupportedException("Unsupported index type.") }; stride = stride == 0 ? size : stride;
            var result = new int[count]; for (var i = 0; i < count; i++) result[i] = type switch { 5121 => buf[start + i * stride], 5123 => BitConverter.ToUInt16(buf, start + i * stride), _ => checked((int)BitConverter.ToUInt32(buf, start + i * stride)) }; return result;
        }
    }

    private static void Validate(MeshData mesh)
    {
        if (mesh.Positions.Count == 0 || mesh.Indices.Count < 3) throw new InvalidDataException("The model contains no triangle geometry.");
        if (mesh.Indices.Count % 3 != 0) throw new InvalidDataException("The model index count is not triangular.");
        if (mesh.Vertices.Any(v => v.Position < 0 || v.Position >= mesh.Positions.Count || v.TexCoord >= mesh.TexCoords.Count || v.Normal >= mesh.Normals.Count)) throw new InvalidDataException("The model contains invalid indices.");
    }
}

