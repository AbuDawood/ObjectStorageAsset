using System.Linq.Expressions;
using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset;

internal static class BindingCatalogFactory
{
    public static IObjectAssetBindingCatalog Create(IReadOnlyCollection<Type> bindingTypes)
    {
        var owners = new List<ObjectAssetOwnerDefinition>(bindingTypes.Count);

        foreach (var bindingType in bindingTypes)
        {
            var binding = Activator.CreateInstance(bindingType)
                ?? throw new InvalidOperationException(
                    $"Failed to create object asset binding '{bindingType.FullName}'.");

            var contractInterface = bindingType.GetInterfaces()
                .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IObjectAssetBinding<,>))
                ?? throw new InvalidOperationException(
                    $"Binding '{bindingType.FullName}' must implement IObjectAssetBinding<T, TKey>.");

            var ownerClrType = contractInterface.GetGenericArguments()[0];
            var ownerKeyType = contractInterface.GetGenericArguments()[1];
            var builderType = typeof(ObjectAssetOwnerBuilder<,>).MakeGenericType(ownerClrType, ownerKeyType);
            var builder = Activator.CreateInstance(builderType)
                ?? throw new InvalidOperationException($"Failed to create builder for '{bindingType.FullName}'.");

            try
            {
                bindingType.GetMethod(nameof(IObjectAssetBinding<object, object>.Configure))!
                    .Invoke(binding, [builder]);
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }

            ObjectAssetOwnerDefinition definition;
            try
            {
                definition = (ObjectAssetOwnerDefinition)builderType
                    .GetMethod(nameof(ObjectAssetOwnerBuilder<object, object>.Build))!
                    .Invoke(builder, null)!;
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }

            owners.Add(definition);
        }

        return new ObjectAssetBindingCatalog(owners);
    }

    private sealed class ObjectAssetOwnerBuilder<T, TKey> : IObjectAssetOwnerBuilder<T, TKey>
    {
        private string? _ownerType;
        private Expression<Func<T, TKey>>? _keySelector;
        private readonly List<ObjectAssetSlotDefinition> _slots = [];

        public IObjectAssetOwnerBuilder<T, TKey> OwnerType(string ownerType)
        {
            if (string.IsNullOrWhiteSpace(ownerType))
            {
                throw new InvalidOperationException("Object asset owner type is required.");
            }

            _ownerType = ownerType.Trim();
            return this;
        }

        public IObjectAssetOwnerBuilder<T, TKey> Key(Expression<Func<T, TKey>> keySelector)
        {
            _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
            return this;
        }

        public IObjectAssetOwnerBuilder<T, TKey> Slot(ObjectAssetSlot slot)
        {
            ArgumentNullException.ThrowIfNull(slot);
            _slots.Add(new ObjectAssetSlotDefinition
            {
                Name = slot.Name,
                Multiplicity = slot.Multiplicity
            });
            return this;
        }

        public ObjectAssetOwnerDefinition Build()
        {
            if (string.IsNullOrWhiteSpace(_ownerType))
            {
                throw new InvalidOperationException($"OwnerType is required for '{typeof(T).FullName}'.");
            }

            if (_keySelector is null)
            {
                throw new InvalidOperationException($"Key selector is required for '{typeof(T).FullName}'.");
            }

            if (_slots.Count == 0)
            {
                throw new InvalidOperationException($"At least one slot is required for '{typeof(T).FullName}'.");
            }

            var duplicateSlot = _slots
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => x.Count() > 1);
            if (duplicateSlot is not null)
            {
                throw new InvalidOperationException(
                    $"Slot '{duplicateSlot.Key}' is duplicated for '{typeof(T).FullName}'.");
            }

            return new ObjectAssetOwnerDefinition
            {
                OwnerType = _ownerType,
                OwnerClrType = typeof(T),
                OwnerKeyType = typeof(TKey),
                KeySelector = _keySelector,
                Slots = _slots
            };
        }
    }

    private sealed class ObjectAssetBindingCatalog(IReadOnlyCollection<ObjectAssetOwnerDefinition> owners)
        : IObjectAssetBindingCatalog
    {
        private readonly Dictionary<Type, ObjectAssetOwnerDefinition> _ownersByClrType = owners
            .GroupBy(x => x.OwnerClrType)
            .ToDictionary(
                x => x.Key,
                x => x.Single());

        public IReadOnlyCollection<ObjectAssetOwnerDefinition> GetOwners()
        {
            return _ownersByClrType.Values.OrderBy(x => x.OwnerType, StringComparer.Ordinal).ToArray();
        }

        public ObjectAssetOwnerDefinition GetOwner<T>()
        {
            return GetOwner(typeof(T));
        }

        public ObjectAssetOwnerDefinition GetOwner(Type ownerClrType)
        {
            ArgumentNullException.ThrowIfNull(ownerClrType);

            if (_ownersByClrType.TryGetValue(ownerClrType, out var definition))
            {
                return definition;
            }

            throw new InvalidOperationException(
                $"No object asset binding was registered for '{ownerClrType.FullName}'.");
        }
    }
}
