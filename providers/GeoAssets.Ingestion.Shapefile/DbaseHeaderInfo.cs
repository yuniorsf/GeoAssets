namespace GeoAssets.Ingestion.Shapefile;

/// <summary>The record count and field names of a <c>.dbf</c> table, read from its header.</summary>
internal sealed record DbaseHeaderInfo(long RecordCount, IReadOnlyList<string> FieldNames)
{
    private const byte HeaderTerminator = 0x0D;
    private const int DescriptorLength = 32;
    private const int NameLength = 11;

    public static DbaseHeaderInfo Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        if (stream.Length < DescriptorLength)
            return new DbaseHeaderInfo(0, []);

        reader.ReadBytes(4);
        var recordCount = reader.ReadUInt32();
        var headerLength = reader.ReadUInt16();
        reader.ReadBytes(DescriptorLength - 10);

        var names = new List<string>();
        while (stream.Position + DescriptorLength <= Math.Min(stream.Length, headerLength))
        {
            var descriptor = reader.ReadBytes(DescriptorLength);
            if (descriptor[0] == HeaderTerminator)
                break;

            var nameLength = Array.IndexOf(descriptor, (byte)0, 0, NameLength);
            var name = System.Text.Encoding.ASCII.GetString(descriptor, 0, nameLength < 0 ? NameLength : nameLength).Trim();
            if (name.Length > 0)
                names.Add(name);
        }

        return new DbaseHeaderInfo(recordCount, names);
    }
}
