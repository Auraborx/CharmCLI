// CharmCLI (S.I.V.A.): a small binary FBX 7.4 writer in plain C# - thread-safe, so models can be written on all CPU cores.
// It writes the same content as FbxExporter.AddPart does through the Autodesk FBX SDK (which is not thread-safe):
// control points, triangles, uv0, normals, tangents, vertex colours, vertex AO, extra data layers, one Phong material per part,
// smoothing element, axis settings (Y up). Entities with a skeleton or dye slots still go through the FBX SDK (FbxExporter).
// File layout follows the public FBX binary format (as written by the FBX SDK 2019 and Blender's exporter).
using System.IO.Compression;
using System.Text;
using Tiger.Schema;
using Tiger.Schema.Entity;

namespace Tiger.Exporters;

public static class SivaFbxWriter
{
    // ---------------------------------------------------------------- node tree ---------------------------------------------------------------------
    sealed class Node
    {
        public readonly string Name;
        public readonly List<object> Props = new();
        public readonly List<Node> Children = new();
        public Node(string name, params object[] props) { Name = name; Props.AddRange(props); }
        public Node Add(string name, params object[] props) { var n = new Node(name, props); Children.Add(n); return n; }
        public Node Add(Node n) { Children.Add(n); return n; }
        // Properties70 "P" record
        public void P(string name, string type, string label, string flags, params object[] values)
        {
            var p = new Node("P", name, type, label, flags); p.Props.AddRange(values); Children.Add(p);
        }
    }

    const int Version = 7400;   // 32-bit offsets; 13-byte null records
    static readonly byte[] Sentinel = new byte[13];
    static readonly byte[] FootId = { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e };
    static readonly byte[] FootMagic = { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b };

    static void WriteProp(BinaryWriter w, object p)
    {
        switch (p)
        {
            case bool b: w.Write((byte)'C'); w.Write((byte)(b ? 1 : 0)); break;
            case byte c: w.Write((byte)'C'); w.Write(c); break;
            case short s: w.Write((byte)'Y'); w.Write(s); break;
            case int i: w.Write((byte)'I'); w.Write(i); break;
            case float f: w.Write((byte)'F'); w.Write(f); break;
            case double d: w.Write((byte)'D'); w.Write(d); break;
            case long l: w.Write((byte)'L'); w.Write(l); break;
            case string str: { byte[] bytes = Encoding.UTF8.GetBytes(str); w.Write((byte)'S'); w.Write(bytes.Length); w.Write(bytes); break; }
            case byte[] raw: w.Write((byte)'R'); w.Write(raw.Length); w.Write(raw); break;
            case double[] da: WriteArray(w, 'd', da.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(da.AsSpan()).ToArray()); break;
            case int[] ia: WriteArray(w, 'i', ia.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(ia.AsSpan()).ToArray()); break;
            case long[] la: WriteArray(w, 'l', la.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(la.AsSpan()).ToArray()); break;
            default: throw new ArgumentException($"FBX property type {p?.GetType().Name}");
        }
    }

    static void WriteArray(BinaryWriter w, char type, int count, byte[] data)
    {
        w.Write((byte)type); w.Write(count);
        if (data.Length > 128)   // compressed like the FBX SDK does for bigger arrays
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(data);
            byte[] c = ms.ToArray();
            w.Write(1); w.Write(c.Length); w.Write(c);
        }
        else { w.Write(0); w.Write(data.Length); w.Write(data); }
    }

    static void WriteNode(BinaryWriter w, Node n)
    {
        long start = w.BaseStream.Position;
        w.Write(0u); w.Write((uint)n.Props.Count); w.Write(0u);   // end offset, property count, property bytes (patched below)
        byte[] name = Encoding.ASCII.GetBytes(n.Name); w.Write((byte)name.Length); w.Write(name);
        long propStart = w.BaseStream.Position;
        foreach (object p in n.Props) WriteProp(w, p);
        long propEnd = w.BaseStream.Position;
        foreach (Node c in n.Children) WriteNode(w, c);
        if (n.Children.Count > 0 || n.Props.Count == 0) w.Write(Sentinel);
        long end = w.BaseStream.Position;
        w.BaseStream.Position = start;
        w.Write((uint)end); w.Write((uint)n.Props.Count); w.Write((uint)(propEnd - propStart));
        w.BaseStream.Position = end;
    }

    static string ObjName(string name, string cls) => $"{name}\0\u0001{cls}";

    // ---------------------------------------------------------------- public ------------------------------------------------------------------------
    /// <summary>True when FbxExporter.AddEntity would write nothing more than AddPart does (no skeleton, no dye slot colours).</summary>
    public static bool CanWriteEntity(ExporterEntity entity)
    {
        if (entity.BoneNodes != null) return false;
        foreach (ExporterPart part in entity.Mesh.Parts)
            if (part.MeshPart is not DynamicMeshPart dmp || dmp.VertexColourSlots.Count > 0 || dmp.GearDyeChangeColorIndex != 0xFF)
                return false;
        return true;
    }

    /// <summary>Writes outputPath + ".fbx" with one mesh node per part (nothing when there are no parts, like the SDK path).</summary>
    public static void Write(string outputPath, IEnumerable<ExporterPart> partsIn)
    {
        List<ExporterPart> parts = partsIn.ToList();
        if (parts.Count == 0) return;
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        long nextId = 1_000_000_000;
        var objects = new Node("Objects");
        var connections = new Node("Connections");
        int nMat = 0;
        foreach (ExporterPart part in parts)
        {
            long geomId = ++nextId, modelId = ++nextId;
            objects.Add(Geometry(part, geomId));

            var model = objects.Add("Model", modelId, ObjName(part.Name, "Model"), "Mesh");
            model.Add("Version", 232);
            var mp = model.Add("Properties70");
            mp.P("ScalingMax", "Vector3D", "Vector", "", 0.0, 0.0, 0.0);
            mp.P("DefaultAttributeIndex", "int", "Integer", "", 0);
            model.Add("Shading", (byte)'T');
            model.Add("Culling", "CullingOff");

            connections.Add("C", "OO", modelId, 0L);
            connections.Add("C", "OO", geomId, modelId);
            if (part.Material != null)
            {
                long matId = ++nextId; nMat++;
                var mat = objects.Add("Material", matId, ObjName(part.Material.Hash.ToString(), "Material"), "");
                mat.Add("Version", 102);
                mat.Add("ShadingModel", "phong");
                mat.Add("MultiLayer", 0);
                var p = mat.Add("Properties70");
                p.P("Emissive", "Vector3D", "Vector", "", 0.0, 0.0, 0.0);
                p.P("Ambient", "Vector3D", "Vector", "", 0.2, 0.2, 0.2);
                p.P("Diffuse", "Vector3D", "Vector", "", 0.8, 0.8, 0.8);
                p.P("Specular", "Vector3D", "Vector", "", 0.2, 0.2, 0.2);
                p.P("Shininess", "double", "Number", "", 20.0);
                p.P("Opacity", "double", "Number", "", 1.0);
                p.P("Reflectivity", "double", "Number", "", 0.0);
                connections.Add("C", "OO", matId, modelId);
            }
        }

        DateTime now = DateTime.Now;
        var top = new List<Node>();
        var hdr = new Node("FBXHeaderExtension");
        hdr.Add("FBXHeaderVersion", 1003);
        hdr.Add("FBXVersion", Version);
        hdr.Add("EncryptionType", 0);
        var ts = hdr.Add("CreationTimeStamp");
        ts.Add("Version", 1000); ts.Add("Year", now.Year); ts.Add("Month", now.Month); ts.Add("Day", now.Day);
        ts.Add("Hour", now.Hour); ts.Add("Minute", now.Minute); ts.Add("Second", now.Second); ts.Add("Millisecond", now.Millisecond);
        hdr.Add("Creator", "CharmCLI (S.I.V.A.) FBX writer");
        top.Add(hdr);
        top.Add(new Node("FileId", Guid.NewGuid().ToByteArray()));
        top.Add(new Node("CreationTime", now.ToString("yyyy-MM-dd HH:mm:ss:fff")));
        top.Add(new Node("Creator", "CharmCLI (S.I.V.A.) FBX writer"));

        var gs = new Node("GlobalSettings");
        gs.Add("Version", 1000);
        var g = gs.Add("Properties70");
        g.P("UpAxis", "int", "Integer", "", 1); g.P("UpAxisSign", "int", "Integer", "", 1);
        g.P("FrontAxis", "int", "Integer", "", 2); g.P("FrontAxisSign", "int", "Integer", "", 1);
        g.P("CoordAxis", "int", "Integer", "", 0); g.P("CoordAxisSign", "int", "Integer", "", 1);
        g.P("OriginalUpAxis", "int", "Integer", "", -1); g.P("OriginalUpAxisSign", "int", "Integer", "", 1);
        g.P("UnitScaleFactor", "double", "Number", "", 1.0); g.P("OriginalUnitScaleFactor", "double", "Number", "", 1.0);
        g.P("AmbientColor", "ColorRGB", "Color", "", 0.0, 0.0, 0.0);
        g.P("DefaultCamera", "KString", "", "", "Producer Perspective");
        g.P("TimeMode", "enum", "", "", 0); g.P("TimeProtocol", "enum", "", "", 2); g.P("SnapOnFrameMode", "enum", "", "", 0);
        g.P("TimeSpanStart", "KTime", "Time", "", 0L); g.P("TimeSpanStop", "KTime", "Time", "", 46186158000L);
        g.P("CustomFrameRate", "double", "Number", "", -1.0);
        g.P("TimeMarker", "Compound", "", "");
        g.P("CurrentTimeMarker", "int", "Integer", "", -1);
        top.Add(gs);

        var docs = new Node("Documents");
        docs.Add("Count", 1);
        var doc = docs.Add("Document", ++nextId, Path.GetFileName(outputPath), "Scene");
        var dp = doc.Add("Properties70");
        dp.P("SourceObject", "object", "", "");
        dp.P("ActiveAnimStackName", "KString", "", "", "");
        doc.Add("RootNode", 0L);
        top.Add(docs);
        top.Add(new Node("References"));

        var defs = new Node("Definitions");
        defs.Add("Version", 100);
        defs.Add("Count", 1 + 2 * parts.Count + nMat);
        defs.Add("ObjectType", "GlobalSettings").Add("Count", 1);
        defs.Add("ObjectType", "Model").Add("Count", parts.Count);
        defs.Add("ObjectType", "Geometry").Add("Count", parts.Count);
        if (nMat > 0) defs.Add("ObjectType", "Material").Add("Count", nMat);
        top.Add(defs);
        top.Add(objects);
        top.Add(connections);
        var takes = new Node("Takes"); takes.Add("Current", ""); top.Add(takes);

        using var fs = new FileStream(outputPath + ".fbx", FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16);
        using var w = new BinaryWriter(fs);
        w.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  ")); w.Write((byte)0); w.Write((byte)0x1A); w.Write((byte)0);
        w.Write(Version);
        foreach (Node n in top) WriteNode(w, n);
        w.Write(Sentinel);
        // footer, as the FBX SDK / Blender write it
        w.Write(FootId);
        w.Write(0);
        long ofs = fs.Position; int pad = (int)(((ofs + 15) & ~15L) - ofs); if (pad == 0) pad = 16;
        w.Write(new byte[pad]);
        w.Write(Version);
        w.Write(new byte[120]);
        w.Write(FootMagic);
    }

    // ---------------------------------------------------------------- geometry ----------------------------------------------------------------------
    sealed class Elem { public string Type = ""; public int TypedIndex; public Node Node = null!; }

    static Node Geometry(ExporterPart part, long id)
    {
        MeshPart m = part.MeshPart;
        var geo = new Node("Geometry", id, ObjName(part.Name, "Geometry"), "Mesh");
        geo.Add("Smoothness", 3);   // eFine, set by AddSmoothing
        geo.Add("PreviewDivisionLevels", 0); geo.Add("RenderDivisionLevels", 0); geo.Add("DisplaySubdivisions", 0);
        geo.Add("BoundaryRule", 0); geo.Add("PreserveBorders", 0); geo.Add("PreserveHardEdges", 0); geo.Add("PropagateEdgeHardness", 0);

        // control points / polygons exactly as AddVerticesAndIndices: lookup[vertexIndex] = last position i in VertexIndices
        var lookup = new Dictionary<uint, int>();
        for (int i = 0; i < m.VertexIndices.Count; i++) lookup[m.VertexIndices[i]] = i;
        int cpCount = m.VertexIndices.Count;
        double[] verts = new double[cpCount * 3];
        foreach (int i in lookup.Values)
        {
            Vector4 p = m.VertexPositions[i];
            verts[i * 3] = p.X; verts[i * 3 + 1] = p.Y; verts[i * 3 + 2] = p.Z;
        }
        int[] poly = new int[m.Indices.Count * 3];
        for (int f = 0; f < m.Indices.Count; f++)
        {
            UIntVector3 face = m.Indices[f];
            poly[f * 3] = lookup[face.X]; poly[f * 3 + 1] = lookup[face.Y]; poly[f * 3 + 2] = -lookup[face.Z] - 1;
        }
        geo.Add("Vertices", verts);
        geo.Add("PolygonVertexIndex", poly);
        geo.Add("GeometryVersion", 124);

        // layers as the SDK builds them: layer 0 from CreateLayer(); [layer][elementType] = element
        var layers = new List<Dictionary<string, Elem>> { new() };
        var typeCount = new Dictionary<string, int>();
        Elem NewElem(string type, string name, int version, string mapping, string reference)
        {
            var n = new Node(type, 0);   // TypedIndex patched when written
            n.Add("Version", version); n.Add("Name", name);
            n.Add("MappingInformationType", mapping); n.Add("ReferenceInformationType", reference);
            return new Elem { Type = type, Node = n };
        }
        double[] V3(List<Vector4> src, Func<Vector4, (double, double, double)> f)
        {
            double[] a = new double[src.Count * 3];
            for (int i = 0; i < src.Count; i++) { var (x, y, z) = f(src[i]); a[i * 3] = x; a[i * 3 + 1] = y; a[i * 3 + 2] = z; }
            return a;
        }
        double[] Ones(int n) { double[] a = new double[n]; Array.Fill(a, 1.0); return a; }
        double[] Rgba(List<Vector4> src)
        {
            double[] a = new double[src.Count * 4];
            // FbxColor (FBX SDK) clamps every channel to 0..1 - do the same so the files match (e.g. -1 in some GripStock colours -> 0)
            for (int i = 0; i < src.Count; i++)
            {
                a[i * 4] = Math.Clamp((double)src[i].X, 0, 1); a[i * 4 + 1] = Math.Clamp((double)src[i].Y, 0, 1);
                a[i * 4 + 2] = Math.Clamp((double)src[i].Z, 0, 1); a[i * 4 + 3] = Math.Clamp((double)src[i].W, 0, 1);
            }
            return a;
        }
        bool newer = Strategy.CurrentStrategy > TigerStrategy.DESTINY2_SHADOWKEEP_2999;

        // AddTexcoords0
        if (m.VertexTexcoords0.Any())
        {
            var e = NewElem("LayerElementUV", "uv0", 101, "ByVertice", "Direct");
            double[] uv = new double[m.VertexTexcoords0.Count * 2];
            for (int i = 0; i < m.VertexTexcoords0.Count; i++) { uv[i * 2] = m.VertexTexcoords0[i].X; uv[i * 2 + 1] = m.VertexTexcoords0[i].Y; }
            e.Node.Add("UV", uv);
            layers[0]["LayerElementUV"] = e;
        }
        // AddNormals
        if (m.VertexNormals.Any())
        {
            var e = NewElem("LayerElementNormal", "normalLayerName", 102, "ByVertice", "Direct");
            e.Node.Add("Normals", V3(m.VertexNormals, n =>
            {
                if (newer)
                {
                    Vector3 eu = m is DynamicMeshPart ? new Vector3(n.X, n.Y, n.Z) : Vector4.ConsiderQuatToEulerConvert(n);
                    return (eu.X, eu.Y, eu.Z);
                }
                return (n.X, n.Y, n.Z);
            }));
            e.Node.Add("NormalsW", Ones(m.VertexNormals.Count));
            layers[0]["LayerElementNormal"] = e;
        }
        // AddTangents
        if (m.VertexTangents.Any())
        {
            var e = NewElem("LayerElementTangent", "tangentLayerName", 102, "ByVertice", "Direct");
            e.Node.Add("Tangents", V3(m.VertexTangents, t =>
            {
                if (newer) { Vector3 eu = Vector4.QuaternionToEulerAngles(t); return (eu.X, eu.Y, eu.Z); }
                return (t.X, t.Y, t.Z);
            }));
            e.Node.Add("TangentsW", Ones(m.VertexTangents.Count));
            layers[0]["LayerElementTangent"] = e;
        }
        // AddColours
        if (m.VertexColours.Any())
        {
            var e = NewElem("LayerElementColor", "colourLayerName", 101, "ByVertice", "Direct");
            e.Node.Add("Colors", Rgba(m.VertexColours));
            layers[0]["LayerElementColor"] = e;
        }
        // AddVertexAO -> layer 1
        if (m.VertexAO.Any())
        {
            var e = NewElem("LayerElementColor", "VertexAO", 101, "ByVertice", "Direct");
            e.Node.Add("Colors", Rgba(m.VertexAO));
            if (layers.Count < 2) layers.Add(new());
            layers[1]["LayerElementColor"] = e;
        }
        // AddExtraData -> layer key+1, only when that layer did not exist yet (as upstream)
        foreach (KeyValuePair<int, List<Vector4>> entry in m.VertexExtraData)
        {
            if (layers.Count > entry.Key + 1) continue;
            layers.Add(new());
            if (layers.Count > entry.Key + 1)
            {
                var e = NewElem("LayerElementColor", $"data{entry.Key}", 101, "ByVertice", "Direct");
                e.Node.Add("Colors", Rgba(entry.Value));
                layers[entry.Key + 1]["LayerElementColor"] = e;
            }
        }
        // AddMaterial
        if (part.Material != null)
        {
            var e = NewElem("LayerElementMaterial", $"matlayer_{part.Name}_{part.Index}", 101, "AllSame", "IndexToDirect");
            e.Node.Add("Materials", new[] { 0 });
            layers[0]["LayerElementMaterial"] = e;
        }
        // AddSmoothing (no edge array is built, so no values - same as the SDK files)
        layers[0]["LayerElementSmoothing"] = NewElem("LayerElementSmoothing", "smoothingLayer", 102, "ByEdge", "Direct");

        // element nodes, then Layer nodes
        string[] elemOrder = { "LayerElementNormal", "LayerElementTangent", "LayerElementColor", "LayerElementUV", "LayerElementSmoothing", "LayerElementMaterial" };
        foreach (string type in elemOrder)
            foreach (var layer in layers)
                if (layer.TryGetValue(type, out Elem? e))
                {
                    int idx = typeCount.GetValueOrDefault(type); typeCount[type] = idx + 1;
                    e.TypedIndex = idx; e.Node.Props[0] = idx;
                    geo.Add(e.Node);
                }
        string[] layerOrder = { "LayerElementNormal", "LayerElementTangent", "LayerElementMaterial", "LayerElementColor", "LayerElementSmoothing", "LayerElementUV" };
        for (int li = 0; li < layers.Count; li++)
        {
            var ln = geo.Add("Layer", li);
            ln.Add("Version", 100);
            foreach (string type in layerOrder)
                if (layers[li].TryGetValue(type, out Elem? e))
                {
                    var le = ln.Add("LayerElement");
                    le.Add("Type", type); le.Add("TypedIndex", e.TypedIndex);
                }
        }
        return geo;
    }
}
