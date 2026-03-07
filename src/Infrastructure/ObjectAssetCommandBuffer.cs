using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetCommandBuffer
{
    private readonly List<IObjectAssetCommand> _commands = [];

    public void Stage(IObjectAssetCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command);
    }

    public IReadOnlyList<IObjectAssetCommand> Drain()
    {
        var drained = _commands.ToArray();
        _commands.Clear();
        return drained;
    }
}

internal interface IObjectAssetCommand;

internal sealed record SetSingleObjectAssetCommand(
    object Owner,
    ObjectAssetOwnerDefinition OwnerDefinition,
    ObjectAssetSlot Slot,
    ObjectAssetBufferedContent Content) : IObjectAssetCommand;

internal sealed record AddObjectAssetCommand(
    object Owner,
    ObjectAssetOwnerDefinition OwnerDefinition,
    ObjectAssetSlot Slot,
    ObjectAssetBufferedContent Content) : IObjectAssetCommand;

internal sealed record RemoveObjectAssetCommand(Guid AssetId) : IObjectAssetCommand;
