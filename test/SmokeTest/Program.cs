using Elf.ObjectStorageAsset.LiveE2E;

namespace Elf.ObjectStorageAsset.SmokeTest;

public static class Program
{
    public static Task<int> Main()
    {
        var smokeDatabaseName = Environment.GetEnvironmentVariable("OSA_SMOKE_SQL_DATABASE");
        if (string.IsNullOrWhiteSpace(smokeDatabaseName))
        {
            smokeDatabaseName = $"ObjectStorageAsset_SmokeTest_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        }

        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["OSA_E2E_SQL_SERVER"] = ReadSetting("OSA_SMOKE_SQL_SERVER", "OSA_E2E_SQL_SERVER") ?? "10.0.2.2,1433",
            ["OSA_E2E_SQL_USER"] = ReadSetting("OSA_SMOKE_SQL_USER", "OSA_E2E_SQL_USER") ?? "sa",
            ["OSA_E2E_SQL_PASSWORD"] = ReadSetting("OSA_SMOKE_SQL_PASSWORD", "OSA_E2E_SQL_PASSWORD") ?? "Password@123",
            ["OSA_E2E_SQL_DATABASE"] = smokeDatabaseName,
            ["OSA_E2E_STORAGE_NAMESPACE"] = ReadSetting("OSA_SMOKE_STORAGE_NAMESPACE", "OSA_E2E_STORAGE_NAMESPACE") ?? "smoke-test",
            ["OSA_E2E_MINIO_ENDPOINT"] = ReadSetting("OSA_SMOKE_MINIO_ENDPOINT", "OSA_E2E_MINIO_ENDPOINT") ?? "10.0.2.2:19000",
            ["OSA_E2E_MINIO_ACCESS_KEY"] = ReadSetting("OSA_SMOKE_MINIO_ACCESS_KEY", "OSA_E2E_MINIO_ACCESS_KEY") ?? "minioadmin",
            ["OSA_E2E_MINIO_SECRET_KEY"] = ReadSetting("OSA_SMOKE_MINIO_SECRET_KEY", "OSA_E2E_MINIO_SECRET_KEY") ?? "minioadmin",
            ["OSA_E2E_BUCKET"] = ReadSetting("OSA_SMOKE_BUCKET", "OSA_E2E_BUCKET") ?? "osa-smoke-test",
            ["OSA_E2E_MINIO_AUTO_CREATE_BUCKET"] = ReadSetting("OSA_SMOKE_MINIO_AUTO_CREATE_BUCKET", "OSA_E2E_MINIO_AUTO_CREATE_BUCKET") ?? "true",
            ["OSA_E2E_MINIO_USE_SSL"] = ReadSetting("OSA_SMOKE_MINIO_USE_SSL", "OSA_E2E_MINIO_USE_SSL") ?? "false",
            ["OSA_E2E_PRESERVE_DATABASE"] = "true",
            ["OSA_E2E_PRESERVE_OBJECTS"] = ReadSetting("OSA_SMOKE_PRESERVE_OBJECTS", "OSA_E2E_PRESERVE_OBJECTS") ?? "true",
            ["OSA_E2E_SKIP_DELETE_WORKFLOW"] = ReadSetting("OSA_SMOKE_SKIP_DELETE_WORKFLOW", "OSA_E2E_SKIP_DELETE_WORKFLOW") ?? "true"
        });

        Console.WriteLine($"Smoke test database: {smokeDatabaseName}");
        return LiveE2E.Program.Main();
    }

    private static string? ReadSetting(string primaryKey, string fallbackKey)
    {
        return Environment.GetEnvironmentVariable(primaryKey)
            ?? Environment.GetEnvironmentVariable(fallbackKey);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues;

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> variables)
        {
            _originalValues = new Dictionary<string, string?>(StringComparer.Ordinal);

            foreach (var variable in variables)
            {
                _originalValues[variable.Key] = Environment.GetEnvironmentVariable(variable.Key);
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            }
        }

        public void Dispose()
        {
            foreach (var variable in _originalValues)
            {
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            }
        }
    }
}
