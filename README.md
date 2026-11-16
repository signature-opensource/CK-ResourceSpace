# CK-ResourceSpace

[![Licence](https://img.shields.io/github/license/signature-opensource/CK-ResourceSpace.svg)](LICENSE)

Resources spread across packages, ordered by their dependencies, merged, and installed.

| Package | Description | Latest stable |
|---------|-------------|---------------|
| [CK.Engine.Configuration](CK.Engine.Configuration/README.md) | The configuration of the CKomposable engine, and the App builder model that produces it from a `ckonf.json`. | [![nuget](https://img.shields.io/nuget/v/CK.Engine.Configuration.svg?label=CK.Engine.Configuration)](https://www.nuget.org/packages/CK.Engine.Configuration/) |
| [CK.TypeCache](CK.TypeCache/README.md) | The assemblies of the BinPaths and the cache of their types (`AssemblyCache`, `GlobalTypeCache`). | [![nuget](https://img.shields.io/nuget/v/CK.TypeCache.svg?label=CK.TypeCache)](https://www.nuget.org/packages/CK.TypeCache/) |
| [CK.ResourceSpace](CK.ResourceSpace/README.md) | The topological sort, the package model, and the handler/installer contract that projects resources onto a target. | [![nuget](https://img.shields.io/nuget/v/CK.ResourceSpace.svg?label=CK.ResourceSpace)](https://www.nuget.org/packages/CK.ResourceSpace/) |
| [CK.EmbeddedResources.Globalization](CK.EmbeddedResources.Globalization/README.md) | Reading a locales folder out of a resource container, and merging translations with override and ambiguity semantics. | [![nuget](https://img.shields.io/nuget/v/CK.EmbeddedResources.Globalization.svg?label=CK.EmbeddedResources.Globalization)](https://www.nuget.org/packages/CK.EmbeddedResources.Globalization/) |
| [CK.ResourceSpace.Assets](CK.ResourceSpace.Assets/README.md) | The folder handler for assets - files copied as they are. | [![nuget](https://img.shields.io/nuget/v/CK.ResourceSpace.Assets.svg?label=CK.ResourceSpace.Assets)](https://www.nuget.org/packages/CK.ResourceSpace.Assets/) |
| [CK.ResourceSpace.Globalization](CK.ResourceSpace.Globalization/README.md) | The folder handler for locales, writing one JSON file per active culture. | [![nuget](https://img.shields.io/nuget/v/CK.ResourceSpace.Globalization.svg?label=CK.ResourceSpace.Globalization)](https://www.nuget.org/packages/CK.ResourceSpace.Globalization/) |

Read [CK.ResourceSpace](CK.ResourceSpace/README.md) first: it carries the concepts the handlers build on.
