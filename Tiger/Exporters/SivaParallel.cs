// CharmCLI (S.I.V.A.): save textures on several CPU cores instead of one by one.
// Off unless the -siva CLI turns it on (the Charm window keeps upstream behaviour).
// While on, Material.Export only QUEUES its textures; MaterialExporter saves the queue (plus the scene textures) in parallel afterwards.
using System.Collections.Concurrent;
using System.Diagnostics;
using Arithmic;
using Tiger.Schema;

namespace Tiger.Exporters;

public static class SivaParallel
{
    public static bool Enabled;                 // -siva CLI: -threads N (N > 1, default = number of CPU cores)
    public static bool NoTextureLock;           // TextureExporter skips its global lock (-texlock keeps it)
    public static int Threads = Math.Max(1, Environment.ProcessorCount);
    // -fbxthreads N: EXPERIMENTAL, CRASHES (2026-11-07: AccessViolation in FbxMesh.Create with one FbxManager per thread - the FBX SDK has
    // global state). Default 1 = models written one by one, as upstream.
    public static int FbxThreads = 1;
    // -fbxrecycle N: instead of clearing every model's FBX scene one by one (14 s for BrayTech), drop the whole FBX manager every N files (0 = upstream Clear)
    public static int FbxRecycle = 500;
    // -fbxwriter sdk: write every model with the Autodesk FBX SDK (one by one, upstream). Default: CharmCLI's own writer (SivaFbxWriter) on all cores
    // for models without skeleton / dye slots.
    public static bool OwnFbxWriter = true;
    // -png wic: Charm's WIC PNG encoder. Default in the -siva CLI: CharmCLI's own fast writer (SivaPng, same pixels)
    public static bool FastPng;
    // -siva CLI: skip the textures S.I.V.A. never reads (cubemaps, atmosphere, light cookies, LUTs; their json files stay). -alltextures keeps them.
    public static bool SkipUnusedTextures;
    // -maxtex N: textures larger than N pixels (longest side) are shrunk to N while exporting (512 / 1024 / 2048; 0 = original size, the default)
    public static int MaxTex;
    // -texfile dds: textures that need no shrinking are written as DDS exactly as the game stores them (S.I.V.A. 2.17+ reads DDS); default png
    public static bool RawDds;
    // -d1shaders (CLI) or environment variable CHARM_D1SHADERS=1 (also for the Charm window): D1 (PS4) materials write their raw shader files
    // to Shaders/PS4/ next to the export (D1 shaders cannot be decompiled; for research on reading them)
    public static readonly bool DumpD1ShadersEnv = Environment.GetEnvironmentVariable("CHARM_D1SHADERS") == "1";
    public static bool DumpD1Shaders = DumpD1ShadersEnv;
    // set per bubble by the CLI: the folder holds textures saved at another -maxtex -> save them again instead of keeping the existing files
    public static bool RewriteTextures;

    // per-key locks (e.g. one per shader hash: materials sharing a shader must not decompile / write it at the same time)
    private static readonly ConcurrentDictionary<string, object> _keyLocks = new();
    public static void WithLocks(IEnumerable<string> keys, Action body)
    {
        var objs = keys.Distinct().OrderBy(k => k, StringComparer.Ordinal).Select(k => _keyLocks.GetOrAdd(k, _ => new object())).ToList();
        int taken = 0;
        try { foreach (object o in objs) { Monitor.Enter(o); taken++; } body(); }
        finally { for (int i = taken - 1; i >= 0; i--) Monitor.Exit(objs[i]); }
    }

    /// <summary>foreach that runs on several cores when the -siva CLI is on (the Charm window: one by one, as upstream).</summary>
    public static void ForEach<T>(IEnumerable<T> items, Action<T> body)
    {
        if (Enabled) Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = Threads }, body);
        else foreach (T item in items) body(item);
    }

    // full save path (no extension) -> texture; one entry per file, so a texture shared by many materials is saved once
    private static readonly ConcurrentDictionary<string, Texture> _queue = new(StringComparer.OrdinalIgnoreCase);

    static string Key(string savePath) => Path.GetFullPath(savePath);

    public static void Queue(Texture texture, string savePath) => _queue.TryAdd(Key(savePath), texture);

    /// <summary>Saves the queued textures plus 'more' (deduplicated) on Threads cores; returns how many were saved.</summary>
    public static int SaveAll(IEnumerable<(Texture, string)> more)
    {
        foreach ((Texture t, string p) in more)
            if (t is not null) _queue.TryAdd(Key(p), t);
        var jobs = _queue.ToList();
        _queue.Clear();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Threads }, job =>
        {
            var sw = Stopwatch.StartNew();
            try { job.Value.SavetoFile(job.Key); }
            catch (Exception e) { Log.Error($"[CharmCLI] texture {job.Value.Hash}: {e.Message}"); }
            SivaTimes.Add("  texture save (each; summed over all cores)", sw);
        });
        return jobs.Count;
    }
}
