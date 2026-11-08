// S.I.V.A. command-line mode (CharmCLI fork, GPLv3 like Charm). Runs WITHOUT a window:
//   Charm.exe -siva list   -out maps.json              [-charmconfig <config.json>] [-strategy NAME]
//   Charm.exe -siva export -activity <hash> -bubble <hash|all> [-out <export folder>] [-charmconfig <config.json>] [-strategy NAME]
// Settings come from Charm's own config.json (the one the Charm window uses, or -charmconfig). For its own run the CLI forces what S.I.V.A. needs
// (shaders saved as HLSL, textures per map folder) in a temporary copy - the user's config.json is never changed by the CLI.
// Each bubble is exported into <export>/Maps/<Destination>/<Bubble>/ with a siva_export.json next to its files.
// stdout: "PROGRESS <done>/<total> <step>", "TIME <seconds> <step>", "DONE <folder>"; errors: "ERROR ..." on stderr; exit code 0 = ok.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tiger;
using Tiger.Exporters;
using Tiger.Schema;
using Tiger.Schema.Activity;

namespace Charm;

public static class SivaCli
{
    public const string CliVersion = "1";

    public static bool Wanted(string[] args) => args.Any(a => string.Equals(a, "-siva", StringComparison.OrdinalIgnoreCase));

    static string? Arg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], "-" + name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    static void Out(string s) { Console.Out.WriteLine(s); Console.Out.Flush(); }
    static void Err(string s) { Console.Error.WriteLine("ERROR " + s); Console.Error.Flush(); }

    public static int Run(string[] args)
    {
        try
        {
            string cmd = Arg(args, "siva") ?? "";
            Stopwatch sw = Stopwatch.StartNew();
            if (!Init(args)) return 1;
            Out($"TIME {sw.Elapsed.TotalSeconds:F1} start-up (game version {Strategy.CurrentStrategy})");
            switch (cmd.ToLowerInvariant())
            {
                case "list": return List(args);
                case "export": return Export(args);
                default: Err($"unknown command '{cmd}' (list, export)"); return 1;
            }
        }
        catch (Exception e)
        {
            Err(e.ToString());
            return 1;
        }
    }

    // ---- start-up: Charm's settings (a temporary copy with S.I.V.A.'s must-haves), the game-version singletons, the subsystems --------------------
    static bool Init(string[] args)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string cfgPath = Arg(args, "charmconfig") ?? Path.Combine(baseDir, "config.json");
        if (!File.Exists(cfgPath)) { Err($"Charm settings not found: {cfgPath} (set the game version, packages folder and export folder first)"); return false; }
        JObject cfg = JObject.Parse(File.ReadAllText(cfgPath));
        JObject common = (JObject?)cfg["Common"] ?? new JObject();
        common["SaveShaderHLSL"] = true;                    // S.I.V.A. reads Shaders/HLSL
        common["SingleFolderMapAssetsEnabled"] = false;     // textures in each map folder
        string? tex = Arg(args, "textures");                // png (default for S.I.V.A.) / tga / dds
        if (tex != null)
            common["OutputTextureFormat"] = tex.ToLowerInvariant() switch { "tga" => "TGA", "dds" => "DDS_BGRA_BC3_DX10", _ => "PNG" };
        cfg["Common"] = common;
        string tmp = Path.Combine(Path.GetTempPath(), $"charmcli_config_{Environment.ProcessId}.json");
        File.WriteAllText(tmp, cfg.ToString(Formatting.Indented));

        List<string> a2 = args.ToList();
        int ci = a2.FindIndex(x => string.Equals(x, "-config", StringComparison.OrdinalIgnoreCase));
        if (ci >= 0 && ci + 1 < a2.Count) a2.RemoveRange(ci, 2);
        a2.Add("-config"); a2.Add(tmp);
        TigerInstance.Args = new TigerArgs(a2.ToArray());

        MainWindow.InitialiseStrategistSingletons();
        TigerInstance.InitialiseSubsystems();
        ConfigSubsystem config = TigerInstance.GetSubsystem<ConfigSubsystem>();
        TextureExporter.SetTextureFormat(config.GetOutputTextureFormat());
        if (Strategy.CurrentStrategy == TigerStrategy.NONE) { Err("no game version chosen in Charm's settings"); return false; }
        if (config.GetPackagesPath(Strategy.CurrentStrategy) == "") { Err($"no packages folder set for {Strategy.CurrentStrategy}"); return false; }
        return true;
    }

    // ---- list: every activity with its destination and bubbles -----------------------------------------------------------------------------------
    static int List(string[] args)
    {
        string outPath = Arg(args, "out") ?? "maps.json";
        Stopwatch sw = Stopwatch.StartNew();
        var hashes = PackageResourcer.Get().GetAllHashes<IActivity>().ToList();
        var rows = new List<object>(); int n = 0;
        foreach (FileHash h in hashes)
        {
            n++;
            if (n % 50 == 0) Out($"PROGRESS {n}/{hashes.Count} reading activities");
            try
            {
                string name = PackageResourcer.Get().GetActivityName(h);
                IActivity act = FileResourcer.Get().GetFileInterface<IActivity>(h);
                var bubbles = new List<object>();
                foreach (Bubble b in act.EnumerateBubbles())
                    if (b.ChildMapReference is not null) bubbles.Add(new { name = b.Name, hash = b.ChildMapReference.Hash.ToString() });
                rows.Add(new { hash = h.ToString(), name, destination = act.DestinationName, bubbles });
            }
            catch (Exception e) { Out($"SKIP {h}: {e.Message}"); }
        }
        File.WriteAllText(outPath, JsonConvert.SerializeObject(new
        {
            cli = CliVersion, charm = CharmApp.CurrentVersion.Id, game = Strategy.CurrentStrategy.ToString(), activities = rows
        }, Formatting.Indented));
        Out($"TIME {sw.Elapsed.TotalSeconds:F1} list ({rows.Count} activities)");
        Out($"DONE {Path.GetFullPath(outPath)}");
        return 0;
    }

    // ---- export: one folder per bubble (statics + terrain, then the map resources: entities, sky, decorators, decals, lights ...) -----------------
    static int Export(string[] args)
    {
        string? actArg = Arg(args, "activity"), bubArg = Arg(args, "bubble");
        if (actArg == null) { Err("-activity <hash> is needed"); return 1; }
        IActivity act = FileResourcer.Get().GetFileInterface<IActivity>(new FileHash(actArg));
        string root = Arg(args, "out") ?? TigerInstance.GetSubsystem<ConfigSubsystem>().GetExportSavePath();
        string dest = Helpers.SanitizeString(act.DestinationName ?? "Map");
        var bubbles = act.EnumerateBubbles().Where(b => b.ChildMapReference is not null)
            .Where(b => bubArg == null || bubArg.Equals("all", StringComparison.OrdinalIgnoreCase) || b.ChildMapReference.Hash.ToString().Equals(bubArg, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (bubbles.Count == 0) { Err("no such bubble in this activity"); return 1; }
        int step = 0, total = bubbles.Count * 2;
        foreach (Bubble b in bubbles)
        {
            string bname = Helpers.SanitizeString(string.IsNullOrWhiteSpace(b.Name) ? b.ChildMapReference.Hash.ToString() : b.Name);
            string savePath = Path.Combine(root, "Maps", dest, bname) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(savePath);
            Tag<SBubbleDefinition> def = FileResourcer.Get().GetSchemaTag<SBubbleDefinition>(b.ChildMapReference.Hash);

            // 1. static map (statics, terrain) - as the window's "All Map" first half
            Stopwatch sw = Stopwatch.StartNew();
            Out($"PROGRESS {step}/{total} {bname}: static map");
            Exporter.Get().GetOrCreateGlobalScene();
            foreach (var m in def.TagData.MapResources)
                MapView.ExportFullMap(m.MapContainer, savePath);
            Exporter.Get().Export(savePath);
            step++; Out($"TIME {sw.Elapsed.TotalSeconds:F1} {bname}: static map");

            // 2. map resources (entities, sky objects, decorators, speedtrees, decals, water, lights, atmosphere ...)
            sw.Restart();
            Out($"PROGRESS {step}/{total} {bname}: map resources");
            Exporter.Get().GetOrCreateGlobalScene();
            foreach (var m in def.TagData.MapResources)
            {
                List<FileHash> tables = new();
                foreach (SMapDataTableEntry dt in m.MapContainer.TagData.MapDataTables)
                    if (dt.MapDataTable is not null && !tables.Contains(dt.MapDataTable.Hash)) tables.Add(dt.MapDataTable.Hash);
                ActivityMapEntityView.ExportFull(tables, m.MapContainer.Hash.ToString(), savePath);
            }
            Exporter.Get().Export(savePath);
            step++; Out($"TIME {sw.Elapsed.TotalSeconds:F1} {bname}: map resources");

            File.WriteAllText(Path.Combine(savePath, "siva_export.json"), JsonConvert.SerializeObject(new
            {
                cli = CliVersion, charm = CharmApp.CurrentVersion.Id, game = Strategy.CurrentStrategy.ToString(),
                destination = act.DestinationName, activity = PackageResourcer.Get().GetActivityName(act.FileHash), activity_hash = act.FileHash.ToString(),
                bubble = b.Name, bubble_hash = b.ChildMapReference.Hash.ToString(), exported = DateTime.UtcNow.ToString("o")
            }, Formatting.Indented));
            Out($"DONE {savePath}");
        }
        return 0;
    }
}
