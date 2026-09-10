# CK-ResourceSpace

[![Licence](https://img.shields.io/github/license/signature-opensource/CK-ResourceSpace.svg)](LICENSE)

Resources spread across packages, ordered by their dependencies, merged, and installed. A package
ships a `Res/` folder; the space decides what the merge of all those folders is, and writes the
result.

| Package | Description | Latest stable |
|---------|-------------|---------------|
| [CK.ResourceSpace](CK.ResourceSpace/README.md) | The topological sort, the package model, and the handler/installer contract that projects resources onto a target. | [![nuget](https://img.shields.io/nuget/v/CK.ResourceSpace.svg?label=CK.ResourceSpace)](https://www.nuget.org/packages/CK.ResourceSpace/) |
| [CK.EmbeddedResources.Globalization](CK.EmbeddedResources.Globalization/README.md) | Reading a locales folder out of a resource container, and merging translations with override and ambiguity semantics. | [![nuget](https://img.shields.io/nuget/v/CK.EmbeddedResources.Globalization.svg?label=CK.EmbeddedResources.Globalization)](https://www.nuget.org/packages/CK.EmbeddedResources.Globalization/) |
| [CK.ResourceSpace.Assets](CK.ResourceSpace.Assets/README.md) | The folder handler for assets - files copied as they are. | [![nuget](https://img.shields.io/nuget/v/CK.ResourceSpace.Assets.svg?label=CK.ResourceSpace.Assets)](https://www.nuget.org/packages/CK.ResourceSpace.Assets/) |
| [CK.ResourceSpace.Globalization](CK.ResourceSpace.Globalization/README.md) | The folder handler for locales, writing one JSON file per active culture. | [![nuget](https://img.shields.io/nuget/v/CK.ResourceSpace.Globalization.svg?label=CK.ResourceSpace.Globalization)](https://www.nuget.org/packages/CK.ResourceSpace.Globalization/) |

`CK.ResourceSpace` is the one to read first, and the only one that carries concepts. The three others
are leaves: two handlers that fill one slot each, and one library that has nothing to do with
resource spaces - `CK.EmbeddedResources.Globalization` depends on `CK.Globalization` and
`CK.EmbeddedResources` only, and is usable on its own for anything that reads translations out of a
container.

The two handlers are near-identical in shape, and comparing them is the fastest way to understand
what a handler is: same constructor pattern, same `ResPackageDataCache<T>` with three methods, same
"an ambiguity fails the initialization and aborts the whole build" rule, same Live support gated on
installing to the file system. What differs is `T` - a set of files versus a tree of translations -
and that is the entire difference.
