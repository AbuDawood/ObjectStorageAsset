using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetCommandBuffer : IDisposable
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

    public void Dispose()
    {
        foreach (var command in Drain())
            switch (command)
            {
                case SetSingleObjectAssetCommand set: set.Content.Content.Dispose(); break;
                case AddObjectAssetCommand add: add.Content.Content.Dispose(); break;
            }
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
