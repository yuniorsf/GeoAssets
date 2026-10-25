namespace GeoAssets.Core.Models;

public enum ProviderFieldType
{
    Text,
    Password,
    Url,
    Number,
    Checkbox,
    /// <summary>
    /// Renders an <c>InputFile</c> picker. After the user picks a file the
    /// component stores the raw text content under the key suffixed with
    /// <c>_content</c> (e.g. key "file" → config["file_content"]).
    /// </summary>
    File,
    Select,
    /// <summary>
    /// Renders an <c>InputFile</c> picker for binary files (e.g. Shapefile ZIP).
    /// Stores the raw bytes as a Base-64 string under <c>key_content</c> and
    /// the file name under <c>key_name</c>.
    /// </summary>
    BinaryFile
}
