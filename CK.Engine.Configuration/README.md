# CK.Engine.Configuration

The configuration of the CKomposable engine, and the App builder model that produces it from a `ckonf.json`. It
depends on no engine: the engine, the builder and the contributors of the packages read it.

## The engine configuration

| Type | Role |
|------|------|
| `EngineConfiguration` | The root: global options (`BasePath`, `GlobalTypes`, `GlobalExcludedTypes`, `ExcludedAssemblies`...), the `Aspects` and the `BinPaths`. Read from and written to XML (a `CKSetup.xml`). `EngineConfiguration.NormalizeConfiguration` checks it. |
| `BinPathConfiguration` | A BinPath: its `Path`, its `Assemblies`, its `Types` and `ExcludedTypes`, its named `TypeSets`, its aspects. |
| `TypeSetConfiguration` | A named set of types of a BinPath, computed inside its `Parent` set: its roots (`Types`) and its exclusions (`ExcludedTypes`). The types drive: the configuration names of the configurable types are the keys of their sections, they never bring a type in. |
| `TypeConfiguration`, `TypeConfigurationSet` | A configured type and its kind. |
| `EngineAspectConfiguration`, `BinPathAspectConfiguration`, `MultipleBinPathAspectConfiguration<T>` | The base classes of the aspects' configurations, at the engine level and per BinPath. |

## The App builder model

`AppContextConfiguration` reads the `ckonf.json` of a builder (its Apps, each one an `AppConfiguration`) and the
contributors of the packages (`IAppBuilderContributor`) complete the engine configuration: see
[the App builder model](AppBuilder/README.md).
