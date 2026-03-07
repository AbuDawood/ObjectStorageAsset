namespace Elf.ObjectStorageAsset.TestSupport.Docker;

public sealed record MinioDockerContainerOptions
{
    public string Image { get; init; } = "quay.io/minio/minio:latest";

    public string HostAddress { get; init; } =
        Environment.GetEnvironmentVariable("OSA_DOCKER_MINIO_HOST_ADDRESS") ?? "10.0.2.2";

    public string RootUser { get; init; } = "minioadmin";

    public string RootPassword { get; init; } = "minioadmin";

    public string ContainerNamePrefix { get; init; } = "osa-test-minio";

    public int StartPort { get; init; } = 19100;

    public int MaxPort { get; init; } = 19999;

    public int? ApiPort { get; init; }

    public int? ConsolePort { get; init; }

    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(45);
}
