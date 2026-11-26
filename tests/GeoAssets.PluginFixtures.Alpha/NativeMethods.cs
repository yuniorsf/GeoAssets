using System.Runtime.InteropServices;

namespace GeoAssets.PluginFixtures.Alpha;

internal static partial class NativeMethods
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("e_sqlite3", EntryPoint = "sqlite3_libversion_number")]
    internal static partial int LibVersionNumber();
}
