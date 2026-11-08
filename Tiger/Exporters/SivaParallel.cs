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
