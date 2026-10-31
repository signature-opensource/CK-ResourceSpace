A set of resource packages, topologically ordered, and projected onto an installation target by handlers.

Main types: `ResSpaceConfiguration` (package registration), `ResPackageClosure` (the packages a space will keep, computed
from the types), the automatic mapping of abstractions to their implementations (`ResCoreData.MultipleMappings`), `ResSpaceDataBuilder` (the topological sort
into `ResPackage`s), `ResSpaceBuilder` and `ResSpace` (handler registration and install), the
`ResourceSpaceFolderHandler` and `ResourceSpaceFileHandler` base classes, the `IResourceSpaceItemInstaller`
interface with its `FileSystemInstaller`, and `ResPackageDataCache<T>`.
