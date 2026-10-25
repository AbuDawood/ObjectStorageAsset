using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Elf.ObjectStorageAsset.Application;
using FluentAssertions;

namespace Elf.ObjectStorageAsset.UnitTests;

[TestFixture]
public sealed class LegacyBinaryCompatibilityTests
{
    [Test, Category("LegacyInterop")]
    public void V2Payload_ShouldRoundTripThroughActualV016Assembly_AndV3ShouldBeRejected()
    {
        var directory = Environment.GetEnvironmentVariable("OSA_V016_ASSEMBLY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) Assert.Ignore("Set OSA_V016_ASSEMBLY_DIRECTORY to the pinned v0.1.6 build output.");
        var context = new LegacyContext(directory!);
        try
        {
            var application = context.LoadFromAssemblyPath(Path.Combine(directory!, "Elf.ObjectStorageAsset.Application.dll"));
            application.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Should().StartWith("0.1.6");
            var oldType = application.GetType("Elf.ObjectStorageAsset.Application.ObjectAssetDescriptor", true)!;
            var descriptor = new ObjectAssetDescriptor
            {
                AssetId = Guid.NewGuid(),
                FileName = "a.txt",
                ContentType = "text/plain",
                Length = 3,
                Hash = new string('a', 64),
                Bucket = "bucket",
                ObjectKey = "files/a.txt",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string> { ["purpose"] = "compatibility" }
            };
            var old = JsonSerializer.Deserialize(JsonSerializer.Serialize(descriptor), oldType)!;
            var infrastructure = context.LoadFromAssemblyPath(Path.Combine(directory!, "Elf.ObjectStorageAsset.Infrastructure.dll"));
            var registry = infrastructure.GetType("Elf.ObjectStorageAsset.Infrastructure.ObjectAssetRegistry", true)!;
            var validate = registry.GetMethod("ValidateDescriptor", BindingFlags.Static | BindingFlags.NonPublic)!;
            validate.Invoke(null, [old]);
            var returned = JsonSerializer.Deserialize<ObjectAssetDescriptor>(JsonSerializer.Serialize(old, oldType))!;
            returned.AssetId.Should().Be(descriptor.AssetId); returned.Metadata["purpose"].Should().Be("compatibility");
            returned.DescriptorVersion.Should().Be(2);
            oldType.GetProperty("DescriptorVersion")!.SetValue(old, 3);
            FluentActions.Invoking(() => validate.Invoke(null, [old])).Should().Throw<TargetInvocationException>()
                .Which.InnerException.Should().BeOfType<InvalidOperationException>();
        }
        finally { context.Unload(); }
    }

    private sealed class LegacyContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name!.StartsWith("Elf.ObjectStorageAsset", StringComparison.Ordinal))
            {
                var path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return LoadFromAssemblyPath(path);
            }
            return Default.Assemblies.FirstOrDefault(x => x.GetName().Name == name.Name);
        }
    }
}
