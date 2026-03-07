using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace Elf.ObjectStorageAsset.TestSupport.Docker;

public sealed class MinioDockerContainer : IAsyncDisposable
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly HashSet<int> ReservedPorts =
    [
        9000,
        9001,
        1433,
        5672,
        15672,
        6379
    ];

    private MinioDockerContainer(
        string containerName,
        string hostAddress,
        int apiPort,
        int consolePort,
        string accessKey,
        string secretKey)
    {
        ContainerName = containerName;
        HostAddress = hostAddress;
        ApiPort = apiPort;
        ConsolePort = consolePort;
        AccessKey = accessKey;
        SecretKey = secretKey;
    }

    public string ContainerName { get; }

    public string HostAddress { get; }

    public int ApiPort { get; }

    public int ConsolePort { get; }

    public string AccessKey { get; }

    public string SecretKey { get; }

    public string Endpoint => $"{HostAddress}:{ApiPort}";

    public static async Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunProcessAsync(
                "docker",
                ["version", "--format", "{{.Server.Version}}"],
                cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<MinioDockerContainer> StartAsync(
        MinioDockerContainerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MinioDockerContainerOptions();

        if (!await IsDockerAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Docker CLI is not available.");
        }

        var usedPorts = GetUnavailablePorts();
        var apiPort = options.ApiPort ?? FindAvailablePort(usedPorts, options.StartPort, options.MaxPort);
        usedPorts.Add(apiPort);
        var consolePort = options.ConsolePort ?? FindAvailablePort(usedPorts, apiPort + 1, options.MaxPort);
        var containerName = $"{options.ContainerNamePrefix}-{Guid.NewGuid():N}";

        var runResult = await RunProcessAsync(
            "docker",
            [
                "run",
                "-d",
                "--name", containerName,
                "-p", $"{apiPort}:9000",
                "-p", $"{consolePort}:9001",
                "-e", $"MINIO_ROOT_USER={options.RootUser}",
                "-e", $"MINIO_ROOT_PASSWORD={options.RootPassword}",
                options.Image,
                "server",
                "/data",
                "--console-address",
                ":9001"
            ],
            cancellationToken).ConfigureAwait(false);

        if (runResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to start MinIO docker container. {runResult.StandardError}".Trim());
        }

        var container = new MinioDockerContainer(
            containerName,
            options.HostAddress,
            apiPort,
            consolePort,
            options.RootUser,
            options.RootPassword);

        try
        {
            await container.WaitUntilReadyAsync(options.ReadyTimeout, cancellationToken).ConfigureAwait(false);
            return container;
        }
        catch
        {
            await container.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await RunProcessAsync(
            "docker",
            ["rm", "-f", ContainerName],
            CancellationToken.None,
            throwOnNonZeroExit: false).ConfigureAwait(false);
    }

    private async Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var healthUri = new Uri($"http://{HostAddress}:{ApiPort}/minio/health/live");

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await HttpClient.GetAsync(healthUri, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch
            {
                // Keep polling until timeout.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"MinIO container '{ContainerName}' did not become ready on {healthUri} within {timeout.TotalSeconds} seconds.");
    }

    private static HashSet<int> GetUnavailablePorts()
    {
        var ports = new HashSet<int>(ReservedPorts);
        foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
        {
            ports.Add(endpoint.Port);
        }

        return ports;
    }

    private static int FindAvailablePort(HashSet<int> unavailablePorts, int startPort, int maxPort)
    {
        for (var port = startPort; port <= maxPort; port++)
        {
            if (!unavailablePorts.Contains(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException(
            $"Unable to allocate a free MinIO test port in the range {startPort}-{maxPort}.");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnNonZeroExit = true)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);
        var result = new ProcessResult(process.ExitCode, standardOutput.Trim(), standardError.Trim());

        if (throwOnNonZeroExit && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process '{fileName}' exited with code {result.ExitCode}. {result.StandardError}".Trim());
        }

        return result;
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
