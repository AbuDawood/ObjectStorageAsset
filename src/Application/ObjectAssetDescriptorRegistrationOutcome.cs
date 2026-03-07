namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Outcome for one descriptor registration attempt.
/// </summary>
public enum ObjectAssetDescriptorRegistrationOutcome
{
    Added = 1,
    IgnoredIdentical = 2,
    RejectedConflict = 3
}
