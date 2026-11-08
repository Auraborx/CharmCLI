using System.Collections.Concurrent;
using Tiger.Schema;

namespace Tiger.Exporters;

// TODO: Clean this up
public class MaterialExporter : AbstractExporter
{
    public override void Export(Exporter.ExportEventArgs args)
    {
        var _config = ConfigSubsystem.Get();

        var textures = new ConcurrentBag<(Texture, string)>();
        var materials = new ConcurrentBag<(ExportMaterial, string)>();

        Parallel.ForEach(args.Scenes, scene =>
        {
            string textureSaveDirectory;
            string shaderSaveDirectory;

            if (scene.DataType is DataExportType.Individual)
            {
                textureSaveDirectory = args.AggregateOutput ? args.OutputDirectory : Path.Join(args.OutputDirectory, scene.Name);
                shaderSaveDirectory = textureSaveDirectory;
            }
            else
            {
                textureSaveDirectory = args.AggregateOutput ? args.OutputDirectory : Path.Join(args.OutputDirectory, $"Maps");
                if (_config.GetSingleFolderMapAssetsEnabled())
                    textureSaveDirectory = $"{_config.GetExportSavePath()}/Maps/Assets/";
                shaderSaveDirectory = textureSaveDirectory;
            }

            textureSaveDirectory = Path.Combine(textureSaveDirectory, "Textures");

            Directory.CreateDirectory(textureSaveDirectory);
            Directory.CreateDirectory(shaderSaveDirectory);

            foreach (Texture texture in scene.ExternalTextures.Distinct())
            {
                if (texture is null) continue;
                string filePath = Path.Combine(textureSaveDirectory, texture.Hash);
                textures.Add((texture, filePath));
            }

            foreach (ExportMaterial material in scene.Materials.Distinct())
            {
                string filePath = shaderSaveDirectory;
                materials.Add((material, filePath));
            }
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();                     // CharmCLI: timed (SivaTimes)
        if (SivaParallel.Enabled)                                             // CharmCLI: materials first (they queue their textures), then all textures in parallel
        {
            // materials on all cores: copies of the same material (same folder) stay one by one in a group; materials sharing a shader take
            // that shader's lock, so it is decompiled / written by one thread at a time
            var groups = materials.GroupBy(m => Path.GetFullPath(m.Item2) + "|" + m.Item1.Material.Hash).ToList();
            Parallel.ForEach(groups, new ParallelOptions { MaxDegreeOfParallelism = SivaParallel.Threads }, g =>
            {
                foreach ((ExportMaterial material, string path) in g)
                {
                    var keys = new List<string>();
                    var ps = material.Material.Pixel.Shader; if (ps != null) keys.Add("ps" + ps.Hash);
                    var vs = material.Material.Vertex.Shader; if (vs != null) keys.Add("vs" + vs.Hash);
                    SivaParallel.WithLocks(keys, () => material.Material.Export(path));
                }
            });
            SivaTimes.Add($"materials ({materials.Count}: json + shader on {SivaParallel.Threads} cores, textures queued)", sw);
            sw.Restart();
            int n = SivaParallel.SaveAll(textures);
            SivaTimes.Add($"textures saved in parallel ({n} files, {SivaParallel.Threads} cores{(SivaParallel.NoTextureLock ? "" : ", encoder lock on")})", sw);
            return;
        }
        foreach ((Texture texture, string path) in textures)
        {
            texture.SavetoFile(path);
        }
        SivaTimes.Add($"textures of the scenes ({textures.Count}, one by one)", sw);

        sw.Restart();
        foreach ((ExportMaterial material, string path) in materials)
        {
            material.Material.Export(path);
        }
        SivaTimes.Add($"materials ({materials.Count}, one by one: json + shader + their textures)", sw);
    }
}
