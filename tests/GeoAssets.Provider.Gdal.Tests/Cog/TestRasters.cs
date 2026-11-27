using OSGeo.GDAL;
using OSGeo.OSR;

namespace GeoAssets.Provider.Gdal.Tests.Cog;

/// <summary>Writes tiny Cloud Optimized GeoTIFFs with GDAL for the tests, and reads PNG tiles back.</summary>
internal static class TestRasters
{
    /// <summary>
    /// A 200 × 200 COG in EPSG:4326 over longitude/latitude −10…10 (0.1° pixels): an RGB raster of one color, or —
    /// <paramref name="float32"/> — one Float32 band ramping 0…199 from west to east.
    /// </summary>
    public static string WriteCog(string directory, (byte R, byte G, byte B) color, bool float32 = false)
    {
        GdalNativeInitializer.EnsureInitialized();

        const int size = 200;
        using var memDriver = OSGeo.GDAL.Gdal.GetDriverByName("MEM");
        using var dataset = memDriver.Create(string.Empty, size, size, float32 ? 1 : 3, float32 ? DataType.GDT_Float32 : DataType.GDT_Byte, []);
        dataset.SetGeoTransform([-10d, 0.1d, 0d, 10d, 0d, -0.1d]);
        using (var wgs84 = new SpatialReference(null))
        {
            wgs84.ImportFromEPSG(4326);
            wgs84.ExportToWkt(out var wkt, []);
            dataset.SetProjection(wkt);
        }

        if (float32)
        {
            var ramp = new float[size * size];
            for (var row = 0; row < size; row++)
            {
                for (var column = 0; column < size; column++)
                    ramp[(row * size) + column] = column;
            }

            using var band = dataset.GetRasterBand(1);
            band.WriteRaster(0, 0, size, size, ramp, size, size, 0, 0);
        }
        else
        {
            byte[] values = [color.R, color.G, color.B];
            for (var i = 0; i < 3; i++)
            {
                using var band = dataset.GetRasterBand(i + 1);
                band.WriteRaster(0, 0, size, size, Enumerable.Repeat(values[i], size * size).ToArray(), size, size, 0, 0);
            }
        }

        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tif");
        using var cogDriver = OSGeo.GDAL.Gdal.GetDriverByName("COG");
        using var cog = cogDriver.CreateCopy(path, dataset, 0, ["BLOCKSIZE=64", "OVERVIEWS=AUTO"], null, null);
        cog.FlushCache();
        return path;
    }

    /// <summary>The decoded pixels of a PNG tile: <c>[band][row * width + column]</c>.</summary>
    public static byte[][] DecodePng(ReadOnlyMemory<byte> png, out int width, out int height)
    {
        var path = $"/vsimem/test-{Guid.NewGuid():N}.png";
        OSGeo.GDAL.Gdal.FileFromMemBuffer(path, png.ToArray());
        try
        {
            using var dataset = OSGeo.GDAL.Gdal.Open(path, Access.GA_ReadOnly);
            width = dataset.RasterXSize;
            height = dataset.RasterYSize;
            var bands = new byte[dataset.RasterCount][];
            for (var i = 0; i < bands.Length; i++)
            {
                using var band = dataset.GetRasterBand(i + 1);
                bands[i] = new byte[width * height];
                band.ReadRaster(0, 0, width, height, bands[i], width, height, 0, 0);
            }

            return bands;
        }
        finally
        {
            OSGeo.GDAL.Gdal.Unlink(path);
        }
    }
}
