using System.IO;
using System.Runtime.CompilerServices;

namespace Pho.Save.Tests
{
    /// <summary>
    /// Resolves paths under Assets/Scripts/Tests/SaveTests/Fixtures without
    /// depending on the process's current working directory (which differs
    /// between `dotnet test` and the Unity test runner). [CallerFilePath]
    /// captures the on-disk source path of the calling test file at compile
    /// time -- Fixtures/ is always a sibling of that file.
    /// </summary>
    static class TestPaths
    {
        public static string FixturePath(string fileName, [CallerFilePath] string callerFilePath = "")
        {
            var dir = Path.GetDirectoryName(callerFilePath);
            return Path.Combine(dir ?? ".", "Fixtures", fileName);
        }
    }
}
