namespace Elf.ObjectStorageAsset.Domain.Enums;

public enum ObjectAssetBindingKind { LegacyUnresolved = 0, LocalOwner = 1, ExternalOwner = 2, Temporary = 3 }
public enum ObjectAssetOriginKind { LegacyUnknown = 0, Local = 1, ImportedFromOsa = 2 }
public enum ObjectAssetLocation { Main = 0, Temporary = 1, Archive = 2 }
public enum Sha256ValidationStatus { NotChecked = 0, Valid = 1, Mismatch = 2, Failed = 3 }
