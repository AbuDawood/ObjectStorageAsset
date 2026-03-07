using FluentAssertions;
using Elf.ObjectStorageAsset.LiveE2E;
using Elf.ObjectStorageAsset.TestSupport.Docker;

namespace Elf.ObjectStorageAsset.DockerMinioE2ETests;

[TestFixture]
[NonParallelizable]
public sealed class DockerMinioE2ETests
{
    [Test]
    [Category("DockerMinio")]
    public async Task LiveE2E_ShouldPassAgainstDockerMinio_OnSafeHostPorts()
    {
        if (!await MinioDockerContainer.IsDockerAvailableAsync().ConfigureAwait(false))
        {
            Assert.Ignore("Docker CLI is not available on this machine.");
        }

        await using var minio = await MinioDockerContainer.StartAsync().ConfigureAwait(false);
        TestContext.WriteLine($"MinIO API: http://{minio.Endpoint}");
        TestContext.WriteLine($"MinIO console: http://{minio.HostAddress}:{minio.ConsolePort}");
        TestContext.WriteLine($"Container: {minio.ContainerName}");

        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["OSA_E2E_SQL_SERVER"] = Environment.GetEnvironmentVariable("OSA_E2E_SQL_SERVER") ?? "10.0.2.2,1433",
            ["OSA_E2E_SQL_USER"] = Environment.GetEnvironmentVariable("OSA_E2E_SQL_USER") ?? "sa",
            ["OSA_E2E_SQL_PASSWORD"] = Environment.GetEnvironmentVariable("OSA_E2E_SQL_PASSWORD") ?? "Password@123",
            ["OSA_E2E_SQL_DATABASE"] = $"ObjectStorageAsset_DockerMinioE2E_{Guid.NewGuid():N}",
            ["OSA_E2E_MINIO_ENDPOINT"] = minio.Endpoint,
            ["OSA_E2E_MINIO_ACCESS_KEY"] = minio.AccessKey,
            ["OSA_E2E_MINIO_SECRET_KEY"] = minio.SecretKey,
            ["OSA_E2E_BUCKET"] = $"osa-docker-e2e-{Guid.NewGuid():N}",
            ["OSA_E2E_MINIO_AUTO_CREATE_BUCKET"] = "true",
            ["OSA_E2E_MINIO_USE_SSL"] = "false"
        });

        var exitCode = await Program.Main().ConfigureAwait(false);
        exitCode.Should().Be(0);
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
