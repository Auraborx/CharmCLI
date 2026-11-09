using System.ComponentModel;
using Arithmic;
using DirectXTexNet;
using Tiger.Schema;

namespace Tiger.Exporters;

public class TextureExporter
{
    private static TextureExportFormat _format = TextureExportFormat.PNG;
    private static readonly object _lock = new();

    public static void SetTextureFormat(TextureExportFormat textureExportFormat) => _format = textureExportFormat;

    public static void ExportTexture(FileHash fileHash, int index = 0)
    {
        var config = TigerInstance.GetSubsystem<ConfigSubsystem>();
        var pkgName = PackageResourcer.Get()
            .GetPackage(fileHash.PackageId)
            .GetPackageMetadata()
            .Name.Split('.')[0];

        var savePath = Path.Combine(config.GetExportSavePath(), "Textures", pkgName);
        Directory.CreateDirectory(savePath);

        var texture = FileResourcer.Get().GetFile<Texture>(fileHash);
        texture.SavetoFile(Path.Combine(savePath, fileHash.ToString()), index);
    }

    public static bool SaveTextureToFile(string savePath, ScratchImage scratchImage,
        TextureDimension dimension = TextureDimension.D2, TextureExportFormat? overrideFormat = null)
    {
        try
        {
            lock (SivaParallel.NoTextureLock ? new object() : _lock)   // CharmCLI: a fresh object = no lock (each texture has its own image + file)
            {
                if (savePath.Contains('.')) // TODO: Figure this out
                {
                    Log.Error($"[BUG] Save Path {savePath} contains a period, cant export texture!");
                    scratchImage.Dispose();
                    return false;
                }

                var format = overrideFormat ?? _format;

                switch (format)
                {
                    case TextureExportFormat.DDS_BGRA_UNCOMP_DX10:
                        scratchImage.SaveToDDSFile(DDS_FLAGS.FORCE_DX10_EXT, savePath + ".dds");
                        break;

                    case TextureExportFormat.DDS_BGRA_BC3_DX10:
                        scratchImage = CompressBC3(scratchImage);
                        scratchImage.SaveToDDSFile(DDS_FLAGS.FORCE_DX9_LEGACY, savePath + ".dds");
                        break;

                    case TextureExportFormat.PNG:
                        SavePNGTexture(scratchImage, savePath, dimension, WICCodecs.PNG);
                        break;

                    case TextureExportFormat.TGA:
                        SaveTGATexture(scratchImage, savePath, dimension);
                        break;
                }

                scratchImage.Dispose();
                return true;
            }
        }
        catch (Exception e)
        {
            Log.Error($"{Path.GetFileName(savePath)} : {e.Message}");
            scratchImage.Dispose();
            return false;
        }
    }

    public static string GetExtension(TextureExportFormat exportFormat) => exportFormat switch
    {
        TextureExportFormat.DDS_BGRA_UNCOMP_DX10 or
        TextureExportFormat.DDS_BGRA_BC3_DX10 => "dds",
        TextureExportFormat.PNG => "png",
        TextureExportFormat.TGA => "tga",
        _ => string.Empty
    };

    #region Helpers

    private static ScratchImage CompressBC3(ScratchImage scratchImage)
    {
        var metadata = scratchImage.GetMetadata();
        return TexHelper.Instance.IsSRGB(metadata.Format)
            ? scratchImage.Compress(DXGI_FORMAT.BC3_UNORM_SRGB, TEX_COMPRESS_FLAGS.SRGB, 0)
            : scratchImage.Compress(DXGI_FORMAT.BC3_UNORM, TEX_COMPRESS_FLAGS.DEFAULT, 0);
    }

    private static void SavePNGTexture(ScratchImage scratchImage, string savePath, TextureDimension dimension, WICCodecs codec)
    {
        var guid = TexHelper.Instance.GetWICCodec(codec);

        if (dimension == TextureDimension.CUBE && scratchImage.GetImageCount() == 6)
        {
            using var flattened = Texture.FlattenCubemap(scratchImage);
            flattened.SaveToWICFile(0, WIC_FLAGS.FORCE_SRGB, guid, savePath + ".png");

            if (ConfigSubsystem.Get().GetExportEquirectCubemaps())
            {
                using var projected = Texture.CubemapCrossToEquirectangular(flattened);
                projected.SaveToWICFile(0, WIC_FLAGS.FORCE_SRGB, guid, savePath + "_Projected.png");
            }

        }
        else
        {
            if (SivaParallel.FastPng && TrySivaPng(scratchImage, savePath + ".png")) return;   // CharmCLI: same pixels, much faster (SivaPng.cs)
            scratchImage.SaveToWICFile(0, WIC_FLAGS.FORCE_SRGB, guid, savePath + ".png");
        }
    }

    // CharmCLI: 8-bit BGRA / RGBA 2D images through SivaPng; anything else -> false (WIC as before)
    private static unsafe bool TrySivaPng(ScratchImage scratchImage, string file)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (scratchImage.GetImageCount() < 1) return false;
        DirectXTexNet.Image img = scratchImage.GetImage(0);
        bool bgra = img.Format is DXGI_FORMAT.B8G8R8A8_UNORM or DXGI_FORMAT.B8G8R8A8_UNORM_SRGB;
        bool rgba = img.Format is DXGI_FORMAT.R8G8B8A8_UNORM or DXGI_FORMAT.R8G8B8A8_UNORM_SRGB;
        if (!bgra && !rgba || img.Width <= 0 || img.Height <= 0 || img.Pixels == IntPtr.Zero) return false;
        SivaPng.Write(file, (byte*)img.Pixels, img.Width, img.Height, img.RowPitch, bgra);
        SivaTimes.Add("    texture: png encode + write (own writer)", sw);
        return true;
    }

    private static void SaveTGATexture(ScratchImage scratchImage, string savePath, TextureDimension dimension)
    {
        if (dimension == TextureDimension.CUBE && scratchImage.GetImageCount() == 6)
        {
            using var flattened = Texture.FlattenCubemap(scratchImage);
            ScratchImage finalImage = flattened;

            if (flattened.GetMetadata().Format == DXGI_FORMAT.R16G16B16A16_UNORM)
            {
                finalImage = flattened.Convert(DXGI_FORMAT.R8G8B8A8_UNORM, 0, 0);
            }

            using (finalImage)
            {
                finalImage.SaveToTGAFile(0, savePath + ".tga");

                if (ConfigSubsystem.Get().GetExportEquirectCubemaps())
                {
                    using var projected = Texture.CubemapCrossToEquirectangular(finalImage);
                    projected.SaveToTGAFile(0, savePath + "_Projected.tga");
                }
            }
        }
        else
        {
            scratchImage.SaveToTGAFile(0, savePath + ".tga");
        }
    }

    #endregion
}

public enum TextureExportFormat
{
    [Description("DDS Uncompressed")]
    DDS_BGRA_UNCOMP_DX10,
    [Description("DDS")]
    DDS_BGRA_BC3_DX10,
    PNG,
    TGA
}
