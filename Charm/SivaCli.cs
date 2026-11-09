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
    public const string CliVersion = "11";

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
            foreach (string l in SivaTimes.TakeLines()) Out("  " + l);
            switch (cmd.ToLowerInvariant())
            {
                case "list": return List(args);
                case "export": return Export(args);
                case "bubbles": return Bubbles(args);
                case "serve": return Serve(args);
                default: Err($"unknown command '{cmd}' (list, export, bubbles, serve)"); return 1;
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

        // -oodle <folder>: load Oodle (oo2core_*_win64.dll, needed to read the game's packages) from that folder instead of ThirdParty\ -
        // S.I.V.A. never ships Oodle; it points CharmCLI at the copy that comes with the game (bin\x64) or with the user's own Charm.
        string? oodle = Arg(args, "oodle");
        if (!string.IsNullOrEmpty(oodle))
        {
            if (!Directory.Exists(oodle)) { Err($"Oodle folder not found: {oodle}"); return false; }
            System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(FileHash).Assembly, (name, asm, path) =>
            {
                if (name.IndexOf("oo2core", StringComparison.OrdinalIgnoreCase) < 0) return IntPtr.Zero;
                string f = Path.Combine(oodle, Path.GetFileName(name));
                if (!f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) f += ".dll";
                return File.Exists(f) ? System.Runtime.InteropServices.NativeLibrary.Load(f) : IntPtr.Zero;
            });
        }
        var swi = Stopwatch.StartNew();
        MainWindow.InitialiseStrategistSingletons();
        SivaTimes.Add("start-up: game-version singletons (find + register)", swi); swi.Restart();
        TigerInstance.InitialiseSubsystems();
        SivaTimes.Add("start-up: subsystems (settings, packages, strings)", swi);
        ConfigSubsystem config = TigerInstance.GetSubsystem<ConfigSubsystem>();
        TextureExporter.SetTextureFormat(config.GetOutputTextureFormat());
        ApplyOptions(args);
        if (Strategy.CurrentStrategy == TigerStrategy.NONE) { Err("no game version chosen in Charm's settings"); return false; }
        if (config.GetPackagesPath(Strategy.CurrentStrategy) == "") { Err($"no packages folder set for {Strategy.CurrentStrategy}"); return false; }
        return true;
    }

    // the per-run switches (also per command in -siva serve: each command starts from the defaults)
    static void ApplyOptions(string[] args)
    {
        SivaParallel.Threads = Math.Max(1, Environment.ProcessorCount); SivaParallel.FbxThreads = 1; SivaParallel.FbxRecycle = 500; SivaParallel.OwnFbxWriter = true;
        SivaParallel.MaxTex = 0; SivaParallel.RawDds = false;
        // -threads N: cores for saving textures (default: all; 1 = Charm's original one-by-one order). -texlock: keep Charm's global encoder lock.
        if (int.TryParse(Arg(args, "threads"), out int th) && th > 0) SivaParallel.Threads = th;
        SivaParallel.Enabled = SivaParallel.Threads > 1;
        if (int.TryParse(Arg(args, "fbxthreads"), out int fth) && fth > 0) SivaParallel.FbxThreads = fth;
        if (int.TryParse(Arg(args, "fbxrecycle"), out int frc) && frc >= 0) SivaParallel.FbxRecycle = frc;
        if (string.Equals(Arg(args, "fbxwriter"), "sdk", StringComparison.OrdinalIgnoreCase)) SivaParallel.OwnFbxWriter = false;
        // -png fast: CharmCLI's own PNG writer. Measured 2026-11-08 (Io): no faster than Charm's WIC encoder (75 vs 78 s summed, same file sizes) -> off by default
        SivaParallel.FastPng = string.Equals(Arg(args, "png"), "fast", StringComparison.OrdinalIgnoreCase);
        if (int.TryParse(Arg(args, "maxtex"), out int mt) && mt >= 0) SivaParallel.MaxTex = mt;
        SivaParallel.RawDds = string.Equals(Arg(args, "texfile"), "dds", StringComparison.OrdinalIgnoreCase);   // S.I.V.A. 2.17+ passes -texfile dds
        SivaParallel.SkipUnusedTextures = !args.Any(a => string.Equals(a, "-alltextures", StringComparison.OrdinalIgnoreCase));
        SivaParallel.NoTextureLock = !args.Any(a => string.Equals(a, "-texlock", StringComparison.OrdinalIgnoreCase));
    }

    // ---- -siva serve (S.I.V.A. 2.17+): start once, then run one command per line on stdin (a JSON array: ["export", "-activity", "...", ...]),
    // each answered with its usual lines and "END <exit code>".  The game data stays loaded between commands (start-up ~3-5 s saved each time).
    // stdin closed or "quit" -> exit.
    static int Serve(string[] startArgs)
    {
        Out("READY " + CliVersion);
        string? line;
        while ((line = Console.In.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line == "quit") break;
            int rc;
            try
            {
                string[] a = JArray.Parse(line).Select(t => t.ToString()).ToArray();
                string c = a.Length > 0 ? a[0].ToLowerInvariant() : "";
                string[] full = new[] { "-siva", c }.Concat(a.Skip(1)).ToArray();
                ApplyOptions(full);
                rc = c switch { "list" => List(full), "export" => Export(full), "bubbles" => Bubbles(full), _ => Fail($"unknown command '{c}' (list, export, bubbles)") };
            }
            catch (Exception e) { Err(e.ToString()); rc = 1; }
            foreach (string l in SivaTimes.TakeLines()) Out("  " + l);
            Out($"END {rc}");
        }
        return 0;
    }
    static int Fail(string m) { Err(m); return 1; }

    // ---- names: Charm's window loads every activity's own string table when its activity list opens; bubble names such as "Aurora Reach" live there
    // and not in the global strings, so without this the CLI printed their hash (D8E3AE56) instead of the name.
    static bool _activityStrings;
    static void LoadActivityStrings()
    {
        if (_activityStrings) return;
        _activityStrings = true;
        Stopwatch sw = Stopwatch.StartNew();
        GlobalStrings gs = GlobalStrings.Get();
        try
        {
            switch (Strategy.CurrentStrategy)
            {
                case TigerStrategy.DESTINY1_RISE_OF_IRON:
                    System.Threading.Tasks.Parallel.ForEach(PackageResourcer.Get().GetD1Activities(), a =>
                    {
                        if (a.Value != 0x80800616) return;
                        try { gs.AddStrings(FileResourcer.Get().GetSchemaTag<Tiger.Schema.Activity.DESTINY1_RISE_OF_IRON.SUnkActivity_ROI>(a.Key).TagData.LocalizedStrings); } catch { }
                    });
                    break;
                case TigerStrategy.DESTINY2_SHADOWKEEP_2601 or TigerStrategy.DESTINY2_SHADOWKEEP_2999:
                    System.Threading.Tasks.Parallel.ForEach(PackageResourcer.Get().GetAllHashes<Tiger.Schema.Activity.DESTINY2_SHADOWKEEP_2601.SUnkActivity_SK>(), h =>
                    {
                        try { gs.AddStrings(FileResourcer.Get().GetSchemaTag<Tiger.Schema.Activity.DESTINY2_SHADOWKEEP_2601.SUnkActivity_SK>(h).TagData.LocalizedStrings); } catch { }
                    });
                    break;
                default:
                    System.Threading.Tasks.Parallel.ForEach(PackageResourcer.Get().GetAllHashes<Tiger.Schema.Activity.DESTINY2_BEYONDLIGHT_3402.S80808E8B>(), h =>
                    {
                        try { gs.AddStrings(FileResourcer.Get().GetSchemaTag<Tiger.Schema.Activity.DESTINY2_BEYONDLIGHT_3402.S80808E8B>(h).TagData.StringContainer); } catch { }
                    });
                    break;
            }
        }
        catch (Exception e) { Out($"SKIP activity strings: {e.Message}"); }
        Out($"TIME {sw.Elapsed.TotalSeconds:F1} activity strings");
    }

    // ---- list: every activity with its destination and bubbles -----------------------------------------------------------------------------------
    static int List(string[] args)
    {
        string outPath = Arg(args, "out") ?? "maps.json";
        Stopwatch sw = Stopwatch.StartNew();
        LoadActivityStrings();
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

    // ---- bubbles: what the game stores about an activity's bubbles (research: how bubbles connect / when the game switches between them) ----------
    //   Charm.exe -siva bubbles -activity <hash|all> -out bubbles.json
    // Per activity (Shadowkeep data): the bubble name table (S808098C4), the per-bubble lists Charm does not use (S808098C2 Unk08 / Unk18), the raw
    // bytes of those tags (for the fields nobody has named yet), and per bubble its map containers -> data tables -> entries (resource class, entity,
    // position), so rooms / triggers shared or placed between bubbles can be studied outside Charm.
    static int Bubbles(string[] args)
    {
        string actArg = Arg(args, "activity") ?? "all", outPath = Arg(args, "out") ?? "bubbles.json";
        Stopwatch sw = Stopwatch.StartNew();
        LoadActivityStrings();
        var hashes = new List<FileHash>();
        if (actArg.Equals("all", StringComparison.OrdinalIgnoreCase)) hashes = PackageResourcer.Get().GetAllHashes<IActivity>().ToList();
        else
        {
            if (!uint.TryParse(actArg, System.Globalization.NumberStyles.HexNumber, null, out uint h32)) { Err($"bad -activity hash: {actArg}"); return 1; }
            hashes.Add(new FileHash(h32));
        }
        bool entries = !args.Any(a => string.Equals(a, "-noentries", StringComparison.OrdinalIgnoreCase));
        var acts = new List<object>(); int n = 0;
        foreach (FileHash h in hashes)
        {
            Out($"PROGRESS {n++}/{hashes.Count} {h}");
            try
            {
                IActivity act = FileResourcer.Get().GetFileInterface<IActivity>(h);
                var o = new Dictionary<string, object?>
                {
                    ["hash"] = h.ToString(), ["name"] = PackageResourcer.Get().GetActivityName(h), ["destination"] = act.DestinationName,
                };
                if (act is Tiger.Schema.Activity.DESTINY2_SHADOWKEEP_2601.Activity sk)
                {
                    var t = sk.TagData;
                    o["activity_raw"] = Convert.ToHexString(sk.GetData());
                    if (t.LocationNames is not null)
                    {
                        var ln = t.LocationNames;
                        o["location_names_hash"] = ln.Hash.ToString();
                        o["location_names_raw"] = Convert.ToHexString(ln.GetData());
                        o["dev_name"] = ln.TagData.ActivityDevName?.Value;
                        o["bubble_names"] = ln.TagData.BubbleNames.Select(b => new
                        {
                            name = GlobalStrings.Get().GetString(b.BubbleName), name_hash = b.BubbleName.ToString(), unk00 = b.Unk00.ToString(), unk04 = b.Unk04.ToString(),
                            unk0c = b.Unk0C, this_index = b.ThisIndex, bubble_index = b.BubbleIndex
                        }).ToList();
                        o["unk30"] = ln.TagData.Unk30.Select(u => new
                        {
                            name = GlobalStrings.Get().GetString(u.BubbleName), name_hash = u.BubbleName.ToString(), cumulative_offset = u.CumulativeOffset,
                            unk08 = u.Unk08.Select(x => (int)x.Unk00).ToList(), unk18 = u.Unk18.Select(x => (int)x.Unk00).ToList(), unk28 = u.Unk28, this_index = u.ThisIndex
                        }).ToList();
                    }
                    var bl = new List<object>();
                    for (int i = 0; i < t.Bubbles.Count; i++)
                    {
                        var mr = t.Bubbles[i].MapReference;
                        var b = new Dictionary<string, object?> { ["index"] = i, ["parent"] = mr?.Hash.ToString() };
                        if (mr is not null)
                        {
                            b["parent_raw"] = Convert.ToHexString(mr.GetData());
                            b["map_name"] = GlobalStrings.Get().GetString(mr.TagData.MapName);
                            var def = mr.TagData.ChildMapReference;
                            if (def is not null)
                            {
                                b["definition"] = def.Hash.ToString();
                                b["maps"] = def.TagData.MapResources.Where(m => m.MapContainer is not null).Select(m => new
                                {
                                    container = m.MapContainer.Hash.ToString(),
                                    container_unk08 = m.MapContainer.TagData.Unk08,
                                    tables = m.MapContainer.TagData.MapDataTables.Where(d => d.MapDataTable is not null).Select(d => new
                                    {
                                        table = d.MapDataTable.Hash.ToString(),
                                        entries = entries ? d.MapDataTable.TagData.DataEntries.Select(e => new
                                        {
                                            cls = e.DataResource.ResourceClassHash.ToString("X8"),
                                            type = SchemaDeserializer.Get().TryGetSchemaType(e.DataResource.ResourceClassHash, out Type ty) ? ty.Name : "",
                                            entity = e.Entity?.Hash.ToString(),
                                            pos = new[] { e.Transfrom.Translation.X, e.Transfrom.Translation.Y, e.Transfrom.Translation.Z },
                                            rot = new[] { e.Transfrom.Rotation.X, e.Transfrom.Rotation.Y, e.Transfrom.Rotation.Z, e.Transfrom.Rotation.W },
                                            unk68 = e.Unk68, world_id = e.WorldID.ToString("X16")
                                        }).ToList() : null,
                                        count = d.MapDataTable.TagData.DataEntries.Count
                                    }).ToList()
                                }).ToList();
                            }
                        }
                        bl.Add(b);
                    }
                    o["bubbles"] = bl;
                    if (t.LocationNames is not null) o["links"] = Links(t);
                }
                else o["bubbles_simple"] = act.EnumerateBubbles().Select(b => new { name = b.Name, definition = b.ChildMapReference?.Hash.ToString() }).ToList();
                acts.Add(o);
            }
            catch (Exception e) { Out($"SKIP {h}: {e.GetType().Name}: {e.Message}"); }
        }
        File.WriteAllText(outPath, JsonConvert.SerializeObject(new { cli = CliVersion, game = Strategy.CurrentStrategy.ToString(), activities = acts }, Formatting.Indented));
        Out($"TIME {sw.Elapsed.TotalSeconds:F1} bubbles ({acts.Count} activities)");
        Out($"DONE {Path.GetFullPath(outPath)}");
        return 0;
    }

    // ---- links: which bubbles you can walk between, and where the doorway is -----------------------------------------------------------------------
    // Found on Hellas Basin and checked on all 36 Shadowkeep activities that have them: every bubble's Unk30 row lists (Unk08) rows of the bubble name
    // table with Unk0C == 1 and no bubble (BubbleIndex -1). Each such row is one connection and exactly two bubbles list it. The doorway itself is the
    // map container(s) loaded by those two bubbles and by no other one (the room both bubbles keep loaded while you walk through it); "middle" is the
    // median position of everything placed in it, "min"/"max" its extent (game units, Charm's axes).
    static List<object> Links(Tiger.Schema.Activity.DESTINY2_SHADOWKEEP_2601.SActivity_SK t)
    {
        var ln = t.LocationNames.TagData;
        var rowToBubble = new Dictionary<int, int>();          // name-table row -> bubble index
        foreach (var r in ln.BubbleNames) if (r.BubbleIndex >= 0) rowToBubble[r.ThisIndex] = r.BubbleIndex;
        string BubbleName(int bi) { var r = ln.BubbleNames.FirstOrDefault(x => x.BubbleIndex == bi); return GlobalStrings.Get().GetString(r.BubbleName); }
        var users = new Dictionary<int, List<int>>();          // connection row -> bubbles that list it
        foreach (var u in ln.Unk30)
        {
            if (!rowToBubble.TryGetValue(u.CumulativeOffset, out int bi)) continue;
            foreach (var c in u.Unk08) { if (!users.TryGetValue(c.Unk00, out var l)) users[c.Unk00] = l = new List<int>(); if (!l.Contains(bi)) l.Add(bi); }
        }
        // containers per bubble
        var byContainer = new Dictionary<string, HashSet<int>>();
        var points = new Dictionary<string, List<System.Numerics.Vector3>>();
        for (int i = 0; i < t.Bubbles.Count; i++)
        {
            var def = t.Bubbles[i].MapReference?.TagData.ChildMapReference;
            if (def is null) continue;
            foreach (var m in def.TagData.MapResources)
            {
                if (m.MapContainer is null) continue;
                string key = m.MapContainer.Hash.ToString();
                if (!byContainer.TryGetValue(key, out var set)) byContainer[key] = set = new HashSet<int>();
                set.Add(i);
                if (points.ContainsKey(key)) continue;
                var pts = new List<System.Numerics.Vector3>();
                try
                {
                    foreach (var d in m.MapContainer.TagData.MapDataTables)
                        if (d.MapDataTable is not null)
                            foreach (var e in d.MapDataTable.TagData.DataEntries)
                            {
                                var p = e.Transfrom.Translation;
                                if (p.X != 0 || p.Y != 0 || p.Z != 0) pts.Add(new System.Numerics.Vector3(p.X, p.Y, p.Z));
                            }
                }
                catch { }
                points[key] = pts;
            }
        }
        float Median(IEnumerable<float> v) { var a = v.OrderBy(x => x).ToList(); return a.Count == 0 ? 0 : a[a.Count / 2]; }
        var res = new List<object>();
        foreach (var kv in users.OrderBy(k => k.Key))
        {
            var row = ln.BubbleNames.FirstOrDefault(x => x.ThisIndex == kv.Key);
            var bs = kv.Value;
            var shared = bs.Count == 2 ? byContainer.Where(c => c.Value.SetEquals(bs)).Select(c => c.Key).ToList() : new List<string>();
            var pts = shared.SelectMany(c => points[c]).ToList();
            res.Add(new
            {
                link = kv.Key, link_hash = row.BubbleName.ToString(),
                bubbles = bs.Select(b => new { index = b, name = BubbleName(b), definition = t.Bubbles[b].MapReference?.TagData.ChildMapReference?.Hash.ToString() }).ToList(),
                containers = shared, objects = pts.Count,
                middle = pts.Count == 0 ? null : new[] { Median(pts.Select(p => p.X)), Median(pts.Select(p => p.Y)), Median(pts.Select(p => p.Z)) },
                min = pts.Count == 0 ? null : new[] { pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z) },
                max = pts.Count == 0 ? null : new[] { pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z) },
            });
        }
        return res;
    }

    // ---- export: one folder per bubble (statics + terrain, then the map resources: entities, sky, decorators, decals, lights ...) -----------------
    static int Export(string[] args)
    {
        string? actArg = Arg(args, "activity"), bubArg = Arg(args, "bubble");
        if (actArg == null) { Err("-activity <hash> is needed"); return 1; }
        // the hash as the list printed it (Hash32 in hex).  NOT new FileHash(string): that swaps the bytes of any text ending in 80 / 81
        // (it takes it for a file-name style hash) - map:tangled_shore 80FCF281 became 81F2FC80 ('package id not in the package paths cache').
        if (!uint.TryParse(actArg, System.Globalization.NumberStyles.HexNumber, null, out uint actHash)) { Err($"bad -activity hash: {actArg}"); return 1; }
        LoadActivityStrings();
        IActivity act = FileResourcer.Get().GetFileInterface<IActivity>(new FileHash(actHash));
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
            // -maxtex: an earlier export of this bubble at another texture size -> its textures are saved again (Charm keeps existing files otherwise)
            int prevMax = 0; bool prevExport = false;
            try
            {
                string pj = Path.Combine(savePath, "siva_export.json");
                if (File.Exists(pj)) { prevExport = true; prevMax = JObject.Parse(File.ReadAllText(pj)).Value<int?>("maxtex") ?? 0; }
            }
            catch (Exception) { prevExport = true; prevMax = -1; }
            SivaParallel.RewriteTextures = prevExport && prevMax != SivaParallel.MaxTex;
            Tag<SBubbleDefinition> def = FileResourcer.Get().GetSchemaTag<SBubbleDefinition>(b.ChildMapReference.Hash);

            // 1. static map (statics, terrain) - as the window's "All Map" first half
            Stopwatch sw = Stopwatch.StartNew();
            Out($"PROGRESS {step}/{total} {bname}: static map");
            Exporter.Get().GetOrCreateGlobalScene();
            SivaParallel.ForEach(def.TagData.MapResources, m =>   // maps read on several cores
            {
                var swm = Stopwatch.StartNew();
                MapView.ExportFullMap(m.MapContainer, savePath);
                SivaTimes.Add("reading: maps (each; summed over cores)", swm);
            });
            Out($"TIME {sw.Elapsed.TotalSeconds:F1} {bname}: static map - reading the game data");
            var swx = Stopwatch.StartNew();
            Exporter.Get().Export(savePath);
            Out($"TIME {swx.Elapsed.TotalSeconds:F1} {bname}: static map - writing files");
            foreach (string l in SivaTimes.TakeLines()) Out("  " + l);
            step++; Out($"TIME {sw.Elapsed.TotalSeconds:F1} {bname}: static map");

            // 2. map resources (entities, sky objects, decorators, speedtrees, decals, water, lights, atmosphere ...)
            sw.Restart();
            Out($"PROGRESS {step}/{total} {bname}: map resources");
            Exporter.Get().GetOrCreateGlobalScene();
            SivaParallel.ForEach(def.TagData.MapResources, m =>   // maps read on several cores
            {
                List<FileHash> tables = new();
                foreach (SMapDataTableEntry dt in m.MapContainer.TagData.MapDataTables)
                    if (dt.MapDataTable is not null && !tables.Contains(dt.MapDataTable.Hash)) tables.Add(dt.MapDataTable.Hash);
                var swm = Stopwatch.StartNew();
                ActivityMapEntityView.ExportFull(tables, m.MapContainer.Hash.ToString(), savePath);
                SivaTimes.Add($"reading: map {m.MapContainer.Hash} ({tables.Count} data tables)", swm);
            });
            Out($"TIME {sw.Elapsed.TotalSeconds:F1} {bname}: map resources - reading the game data");
            var swy = Stopwatch.StartNew();
            Exporter.Get().Export(savePath);
            Out($"TIME {swy.Elapsed.TotalSeconds:F1} {bname}: map resources - writing files");
            foreach (string l in SivaTimes.TakeLines()) Out("  " + l);
            step++; Out($"TIME {sw.Elapsed.TotalSeconds:F1} {bname}: map resources");

            File.WriteAllText(Path.Combine(savePath, "siva_export.json"), JsonConvert.SerializeObject(new
            {
                cli = CliVersion, charm = CharmApp.CurrentVersion.Id, game = Strategy.CurrentStrategy.ToString(),
                destination = act.DestinationName, activity = PackageResourcer.Get().GetActivityName(act.FileHash), activity_hash = act.FileHash.ToString(),
                bubble = b.Name, bubble_hash = b.ChildMapReference.Hash.ToString(), exported = DateTime.UtcNow.ToString("o"),
                maxtex = SivaParallel.MaxTex, texfile = SivaParallel.RawDds ? "dds" : "png"
            }, Formatting.Indented));
            Out($"DONE {savePath}");
        }
        return 0;
    }
}
