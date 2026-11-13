using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Elf.ObjectStorageAsset;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Elf.ObjectStorageAsset.Minio;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Minio.DataModel.Args;

var expectLegacyFailure = args.Contains("--expect-legacy-failure");
var expectedVersionIndex = Array.IndexOf(args, "--expected-version");
var database = "ObjectStorageAssetWorkflow_" + Guid.NewGuid().ToString("N");
var bucket = "osa-compat-" + Guid.NewGuid().ToString("N");
var server = Required("OSA_TEST_SQL_SERVER");
var endpoint = Required("OSA_E2E_MINIO_ENDPOINT");
Check(server.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase)
    || server.StartsWith("127.0.0.1", StringComparison.Ordinal) || server.StartsWith("localhost", StringComparison.Ordinal), "SQL must be local and disposable.");
Check(Uri.TryCreate("http://" + endpoint, UriKind.Absolute, out var storageUri) && storageUri.IsLoopback, "MinIO must be local and disposable.");
var connection = new SqlConnectionStringBuilder
{
    DataSource = server, InitialCatalog = database, TrustServerCertificate = true,
    ApplicationName = "OSA.PackageCompatibility", ConnectTimeout = 15,
    IntegratedSecurity = server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)
        || Environment.GetEnvironmentVariable("OSA_TEST_SQL_INTEGRATED_SECURITY") == "true"
};
if (!connection.IntegratedSecurity)
{ connection.UserID = Required("OSA_TEST_SQL_USER"); connection.Password = Required("OSA_TEST_SQL_PASSWORD"); }
var master = new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master", Enlist = false };
ServiceProvider? services = null;
string? objectKey = null;
var created = false;
try
{
    Check(Regex.IsMatch(database, "^ObjectStorageAssetWorkflow_[a-f0-9]{32}$"), "Unexpected test database namespace.");
    await using (var sql = new SqlConnection(master.ConnectionString))
    {
        await sql.OpenAsync(); await using var command = sql.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{database}];"; // Fails on collision; never reuses a database.
        await command.ExecuteNonQueryAsync(); created = true;
    }
    var registrations = new ServiceCollection();
    registrations.AddObjectStorageAsset(options =>
    {
        options.ConnectionString = connection.ConnectionString; options.Schema = "osa";
        options.DefaultStorageNamespace = "package-compatibility";
        options.LocalInstanceId = Guid.NewGuid(); options.StorageAuthorityId = Guid.NewGuid();
        options.EnableMaintenanceWorker = false;
        options.Integrity.FailureBehavior = Sha256FailureBehavior.Guard;
        options.Integrity.Triggers = Sha256VerificationTriggers.AfterUpload | Sha256VerificationTriggers.OnRead;
        options.AddBinding<QualificationFiles>();
    });
    registrations.AddObjectStorageAssetMinio(options =>
    {
        options.Endpoint = endpoint; options.UseSsl = false; options.AutoCreateBucket = true;
        options.BucketName = bucket; options.AccessKey = Required("OSA_E2E_MINIO_ACCESS_KEY");
        options.SecretKey = Required("OSA_E2E_MINIO_SECRET_KEY");
    });
    services = registrations.BuildServiceProvider(validateScopes: true);
    using var scope = services.CreateScope(); var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<ObjectStorageAssetDbContext>();
    var expectedEf = AppContext.TargetFrameworkName!.Contains("Version=v10.0", StringComparison.Ordinal) ? new Version(10, 0, 15, 0) : new Version(8, 0, 11, 0);
    foreach (var assembly in new[] { typeof(DbContext).Assembly, typeof(RelationalQueryableExtensions).Assembly, typeof(SqlServerDbContextOptionsExtensions).Assembly })
        Check(assembly.GetName().Version == expectedEf, "EF assemblies must use the qualified, consistent version.");
    var packageVersion = typeof(ObjectStorageAssetDbContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    if (expectedVersionIndex >= 0) Check(packageVersion.Split('+')[0] == args[expectedVersionIndex + 1], "Wrong OSA package loaded at runtime.");
    if (expectLegacyFailure) Check(packageVersion == "0.2.1+218b8917fb1baef22985007347558e9f7cf58424", "Wrong published baseline source commit.");
    Console.WriteLine($"Runtime={Environment.Version}; EF={expectedEf}; OSA={packageVersion}");
    await db.Database.MigrateAsync();
    var original = Encoding.UTF8.GetBytes("OSA exact original bytes / guard / package qualification");
    var expectedHash = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
    var binding = Guid.NewGuid();
    var upload = await sp.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<QualificationOwner>(binding)
        .AddAsync(QualificationFiles.Attachments, new MemoryStream(original, false), "qualified.txt", "text/plain");
    var records = sp.GetRequiredService<IObjectAssetPagedReader>();
    var staged = (await records.GetRecordAsync(upload.AssetId))!;
    objectKey = staged.File.ObjectKey;
    var owner = new QualificationOwner { Id = 42 };
    var finalizer = sp.GetRequiredService<IObjectAssetBindingFinalizer>();
    await finalizer.FinalizeTemporaryBindingAsync(binding, owner);
    await finalizer.FinalizeTemporaryBindingAsync(binding, owner); // Identical finalization replay.
    var registry = sp.GetRequiredService<IObjectAssetRegistry>();
    var descriptor = (await registry.GetDescriptorAsync(upload.AssetId))!;
    for (var i = 0; i < 2; i++)
    {
        var replay = await registry.RegisterDescriptorAsync(new() { Descriptor = descriptor, OwnershipMode = ObjectAssetOwnershipMode.Managed });
        Check(replay.Outcome == ObjectAssetDescriptorRegistrationOutcome.IgnoredIdentical, "Identical descriptor replay must be idempotent.");
    }
    var bound = (await records.GetRecordAsync(upload.AssetId))!;
    Check(bound.File.ObjectKey == objectKey && bound.File.Hash == expectedHash && bound.Origin == staged.Origin,
        "Finalization/replay must preserve content identity and origin.");
    var reader = sp.GetRequiredService<IObjectAssetContentReader>();
    async Task ReadOriginal()
    {
        var read = (await reader.OpenReadAsync(upload.AssetId))!;
        await using var content = read.Content;
        using var buffer = new MemoryStream(); await content.CopyToAsync(buffer);
        Check(read.Integrity?.Status == Sha256ValidationStatus.Valid && buffer.ToArray().SequenceEqual(original), "Verified read must return the exact original bytes.");
    }
    await ReadOriginal(); Passed("upload, finalization, identical replay and verified original read");
    var storage = sp.GetRequiredService<IObjectStorageProvider>();
    async Task Put(byte[] bytes)
    {
        await using var content = new MemoryStream(bytes, false);
        await storage.PutObjectAsync(new() { BucketName = bucket, ObjectKey = objectKey, Content = content,
            ContentLength = bytes.Length, ContentType = "text/plain",
            Metadata = ObjectStorageSystemMetadata.Create(upload.AssetId, "package-compatibility", expectedHash) });
    }
    var corrupt = original.ToArray(); corrupt[0] ^= 0xff;
    async Task RejectCorruption()
    {
        try
        {
            var read = await reader.OpenReadAsync(upload.AssetId); if (read is not null) await read.Content.DisposeAsync();
            throw new InvalidOperationException("Guard exposed corrupt bytes.");
        }
        catch (ObjectAssetIntegrityException ex) { Check(ex.Result.Status == Sha256ValidationStatus.Mismatch, "Corruption must be a mismatch."); }
    }
    await Put(corrupt); await RejectCorruption(); Passed("same-length corruption rejected by integrity guard");
    await storage.DeleteObjectAsync(new() { BucketName = bucket, ObjectKey = objectKey });
    var availability = sp.GetRequiredService<IObjectAssetAvailabilityService>();
    if (expectLegacyFailure)
    {
        async Task Reproduces(Func<Task> action, string path)
        {
            try { await action(); throw new InvalidOperationException("Expected published-package TypeLoadException was not reproduced."); }
            catch (TypeLoadException ex) when (ex.Message.Contains("SetPropertyCalls", StringComparison.Ordinal))
            { Console.WriteLine($"REPRODUCED {path}: {ex.GetType().FullName}: {ex.Message}"); }
        }
        await Reproduces(async () => { await reader.OpenReadAsync(upload.AssetId); }, "missing content");
        await Reproduces(async () => { await availability.RecheckAsync(upload.AssetId); }, "availability recheck");
        return;
    }
    for (var i = 0; i < 2; i++)
    {
        try { await reader.OpenReadAsync(upload.AssetId); throw new InvalidOperationException("Missing content was not rejected."); }
        catch (ObjectAssetContentMissingException ex) { Check(ex.Result.Availability == ObjectAssetContentAvailability.Missing, "Missing must have a typed result."); }
    }
    Check((await availability.GetAsync(upload.AssetId))?.Availability == ObjectAssetContentAvailability.Missing, "Missing observation must persist.");
    Check(await db.ContentStates.CountAsync() == 1, "Repeated missing reads must retain one observation.");
    Passed("confirmed missing content persisted and repeated reads rejected");
    await Put(corrupt);
    Check((await availability.RecheckAsync(upload.AssetId))?.Availability == ObjectAssetContentAvailability.Available, "Recheck must observe restored object availability.");
    await RejectCorruption();
    Check((await records.GetRecordAsync(upload.AssetId))!.File.Hash == expectedHash, "Recheck must never replace the expected SHA-256.");
    Passed("availability recheck cannot bypass integrity verification");
    await Put(original);
    await availability.RecheckAsync(upload.AssetId);
    Check((await sp.GetRequiredService<IObjectAssetIntegrityService>().VerifyAsync(upload.AssetId)).Status == Sha256ValidationStatus.Valid,
        "Exact-byte repair must verify.");
    await ReadOriginal();
    var repaired = (await records.GetRecordAsync(upload.AssetId))!;
    Check(await db.AssetIdentities.CountAsync() == 1 && repaired.File.ObjectKey == objectKey && repaired.File.Hash == expectedHash
        && repaired.Origin == staged.Origin && repaired.Asset.BindingKind == ObjectAssetBindingKind.LocalOwner,
        "Recovery must preserve stable identity, owner, origin and expected content identity.");
    Passed("exact-byte repair, verification and immutable identity preservation");
}
finally
{
    try
    {
        if (services is not null)
        {
            using var cleanup = services.CreateScope();
            var client = cleanup.ServiceProvider.GetRequiredService<IMinioClient>();
            if (objectKey is not null) await client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(objectKey));
            if (await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket)))
                await client.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(bucket));
        }
    }
    finally
    {
        if (services is not null) await services.DisposeAsync();
        if (created)
        {
            using (var pool = new SqlConnection(connection.ConnectionString)) SqlConnection.ClearPool(pool);
            var journal = new SqlConnectionStringBuilder(connection.ConnectionString) { Enlist = false, ConnectTimeout = 5, Pooling = true };
            using (var pool = new SqlConnection(journal.ConnectionString)) SqlConnection.ClearPool(pool);
            await using var sql = new SqlConnection(master.ConnectionString); await sql.OpenAsync();
            await using var command = sql.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];";
            await command.ExecuteNonQueryAsync(); Console.WriteLine("Deleted disposable database " + database);
        }
    }
}

static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Set {name} for isolated SQL/MinIO qualification.");
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Passed(string scenario) => Console.WriteLine("PASS " + scenario);

public sealed class QualificationOwner { public int Id { get; set; } }
public sealed class QualificationFiles : IObjectAssetBinding<QualificationOwner, int>
{
    public static readonly ObjectAssetSlot Attachments = ObjectAssetSlot.Many("attachments");
    public void Configure(IObjectAssetOwnerBuilder<QualificationOwner, int> builder)
        => builder.OwnerType("package-qualification").Key(x => x.Id).Slot(Attachments);
}
