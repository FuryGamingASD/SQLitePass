using System.Runtime.CompilerServices;

namespace SQLitePass.Tests;

internal static class TestEnvironment
{
    internal static string LogDirectory { get; } = Path.Combine(Path.GetTempPath(), "SQLitePass-test-logs-" + Guid.NewGuid().ToString("N"));
#pragma warning disable CA2255 // Isolate logging before any parallel test calls the Core assembly.
    [ModuleInitializer]
    internal static void Initialize() => AppContext.SetData("SQLitePass.LogDirectory", LogDirectory);
#pragma warning restore CA2255
}
