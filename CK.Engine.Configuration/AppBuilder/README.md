# The App builder model

`AppBuilderConfiguration` reads the `ckonf.json` of a "XXX.Builder" project, creates the `EngineConfiguration`
(a BinPath per App) and writes the projections (the `appsettings.json` of the hosts). When the repository has a
central configuration (`CentralConfiguration`, see [below](#the-central-configuration)), the `ckonf.json` of each
builder is itself a projection of it.

## `ckonf.json`: the configuration of the builder

When the "XXX.Builder" project has a `ckonf.json`, it is the configuration of what the builder builds in one engine run:
its App (or its Apps) with the configuration of its host, and the aspects. A builder usually builds one App:

```jsonc
{
  // The Builder aspects: they apply to the whole engine run.
  "Aspects": { "SqlServer": { "DefaultDatabaseConnectionString": "Server=.;Database=MyDB;Integrated Security=True" } },
  "App": {
    // Optional: the name of the builder's folder without ".Builder" ("Front.Builder" builds the App "Front").
    "Name": "Front",
    // The aspects of this App.
    "Aspects": { "Globalization": { "ActiveCultures": "fr, en" } },
    "FullName": "Sample/Auth3/$Front/#Dev",
    "Local": { ... },
    "CK-Monitoring": { ... }
  }
}
```

A builder that builds several Apps (they share the Builder aspects: one database) lists them, each one with its name:

```jsonc
{
  "Aspects": { "SqlServer": { ... } },
  "Apps": [
    {
      "Name": "Back1",
      "Types": [ "CK.Core.IActivityMonitor, CK.ActivityMonitor",
                 { "Name": "CK.Core.IParallelLogger, CK.ActivityMonitor", "Kind": "IsContainerConfiguredService|IsScoped" } ],
      "FullName": "Sample/MultiBinPath/$Back1/#Dev"
    },
    { "Name": "Back2", ... }
  ]
}
```

- **`"App"` XOR `"Apps"`:** one of them, not both. The names are unique (case insensitive).
- **An App is a process:** the App "Back1" is the "../Back1.App" project (its code; its bin folder is the one of the
  builder: `bin/{Configuration}/{TargetFramework}`) and the "../Back1.Host" project (its host, the `ProjectPath` of
  its engine BinPath). `"AppProject"` and `"HostProject"` (relative to the `ckonf.json` folder) depart from this
  convention. A path that leaves the solution's folder is an error.
- **The compile time keys of an App:** `Name`, `Types` (an assembly qualified name, or `{ "Name", "Kind" }`),
  `ExcludedTypes`, `Assemblies`, `DiscoverAssembliesFromPath`, `CompileOption`, `GenerateSourceFiles`, `AppProject`,
  `HostProject` and `Aspects`. The other keys are the configuration of the host.
- **The aspects are compile time,** at the root (the Builder aspects) and in an App (its aspects), keyed by their
  name (see [The aspects](#the-aspects) below). An aspect key that nothing reads is an error.
- **The keys are case insensitive** (it is a configuration): a duplicate key (`Local` and `local`) is an error. The
  order of the Apps means nothing: the engine receives them sorted by name.
- **The host's `appsettings.json` is a projection of its App:** its content without the compile time keys (the ones
  above, and the ones the contributors declare, unless `"ProjectCompileKeys": true`), the keys that the contributors
  project in a section moved in it (the keys of the party in `"CK-AppIdentity"`). It is generated (its first line
  says so) and written only when it changes: edit the `ckonf.json`, not the projection.
- **A `CKSetup.xml` may complete it** with the aspects that are not deduced yet and the global options of the engine,
  but no BinPath.

## The central configuration

A solution of several builders (several parties) may describe them once: an optional `ckonf.json` at the root of the
repository (its git root, next to `#Dev`), read by `CentralConfiguration`. The `ckonf.json` of each of its builders is
then generated from it (its first line says so, it is written only when it changes): edit the central file, not the
projection.

```jsonc
{
  "Domain": "Sample/Auth3",
  // The defaults of every App: merged in each App, the App wins (the objects are merged).
  "Defaults": { "App": { "Logging": { "LogLevel": { "Default": "Warning" } } } },
  // The builders, keyed by the path of their folder.
  "Builders": {
    "Auth3/Auth3.Builder": { "Aspects": { "SqlServer": { ... } }, "App": { "Local": { ... } } },
    "Front/Front.Builder": { "App": { "Local": { ... } } }
  },
  // The links between the parties (read by the AppIdentity contributor).
  "Links": [ ... ]
}
```

- **A builder is keyed by the path of its folder,** relative to the root, downward only (`..` is an error). Its entry is
  exactly its `ckonf.json` (`"Aspects"`, `"App"` or `"Apps"`). The names of the Apps are unique in the whole solution.
- **The other root keys belong to the contributors:** a contributor claims the keys it handles
  (`CentralProjectionContext.ClaimKey`) and completes the Apps of the builder being projected
  (`IAppBuilderContributor.ProjectFromCentral`). A root key that no contributor claims is an error. The meaning of what
  they project is checked by the builder that reads its projection, as for a hand written `ckonf.json`.
- **A builder of the repository that is not in `"Builders"` is an error** when the repository has a central
  configuration.

## The aspects

An aspect is a configurable type of a model: a package (a concrete `IResourceGroup`) decorated with
`[AppBuilderAspect( "Name", TypeActivation.X )]` (a Builder aspect, `IAppBuilderAspect`: one per engine run, at the root
`"Aspects"`) or `[AppAspect( "Name", TypeActivation.X )]` (an App aspect, `IAppAspect`: in the `"Aspects"` of an App).
The builder finds them in the type sets of the Apps, before the contributors run:

- **The types drive, the sections configure:** an aspect is present when it is a member of the set of an App (its
  activation `ByDefault` or `Required`) or when its key is declared (`Optional`), unless its key is `false` (an error
  for a `Required` one). Its key never brings its type in.
- **The aspect declares its data:** it has one public constructor, without parameter or with its section (a plain
  `IConfiguration`, null for a bare `true` or when its activation makes it present); it reads its settings and throws
  on an invalid value.
- **It applies itself:** `IAppBuilderAspect.Apply( monitor, context, apps )` (the Apps whose set contains it) or
  `IAppAspect.Apply( monitor, context, app )` ensures and configures its engine aspect configurations and projects its
  run time settings in the hosts: `AppConfiguration.ProjectValue( path, value )` writes a value in a root section of
  the host's `appsettings.json` (that can't be a key of the App).

The "SqlServer" aspect (`SqlServerAspect`, CK.SqlServer.Setup.Model) is a Builder aspect: its
`DefaultDatabaseConnectionString` is projected as `"SqlServer": { "DefaultDatabaseConnectionString" }` in each host.

## The configuration levels and `"$LOCK"`

A value is set at one of three levels (`ConfigurationLevel`): `Compile` (fixed by the build), `Init` (read when the
application starts) or `Run` (changed while it runs). A level is a capability of the code that reads the value: the
engine inputs (the compile keys) are `Compile`, the sections that the code reads at start (declared by the
contributors) are `Init`. The configuration can only lower it:

```jsonc
"App": {
  "Parties": [ ... ],
  // The patterns are relative to the object that holds the "$LOCK": ':' paths, where a whole segment "*" is any member
  // of an object and "[*]" any item of an array. An index ("Parties:0") is refused: it is not an identity.
  "$LOCK": { "Parties:[*]:Address": "Compile" },
  "Defaults": { "$LOCK": { "KeyManagement:AutoTrustKey": "Compile" } }
}
```

- **A `"$LOCK"` is a build input:** the builder removes them before anything reads the configuration, they are never
  projected. In the central `ckonf.json`, `Defaults:App` can hold one for every App (the lowest level wins).
- **A value restricted to `Compile` is burnt:** it is not written in the host's `appsettings.json`, it goes to the
  `CompiledValues` of the App's BinPath and the generated code gives it to the host after its other configuration
  sources (nothing outside the build can change it). A literal pattern that matches no value burns its absence. The
  `CompiledLocks` are the patterns of the host's configuration that the deployed configuration can't extend.
- **The anchors** (`AppConfiguration.DeclareAnchor`): a section of a kind ("Party", "Remote", "OpenedRemoteTemplate"...)
  whose values may come from fallbacks (the "Defaults" above a party). A `"$LOCK"` in an anchor, or in one of its
  fallbacks, locks the value of each anchor at its own path: its own value, otherwise the one of its first fallback.
- **The restrictions of the packages:** `[ConfigurationLock( "OpenedRemoteTemplate:AutoTrustKey", ConfigurationLevel.Compile, Value = "Never" )]`
  on a configurable type whose section is a key of a party: the first segment is a kind of anchor, the rest is relative
  to the type's section. With a `Value`, another configured value is an error and an absent one is burnt with it.
- **A Builder aspect's `"$LOCK"`** locks the values it projects in the hosts (`AppConfiguration.ProjectValue( path, value, origin )`).
- **The errors:** an invalid level or pattern, a value that no code declares, a level above the capability of the code.

The builder applies them (`AppBuilderConfiguration.ApplyLocks`) once the contributors have contributed.

## The contributors' contract

The engines deduce the engine configuration from the `ckonf.json`: they declare an `IAppBuilderContributor` with
`[assembly: AppBuilderContributor( typeof( MyContributor ) )]`.

- `IAppBuilderContributor.ProjectFromCentral( monitor, context )` is called first, when the repository has a central
  configuration: the `CentralProjectionContext` gives the `CentralConfiguration` (its `Configuration` section, its
  `Builders` and their `Apps`) and the Apps of the builder being projected, whose JSON (`CentralProjectedApp.Node`) the
  contributor completes. Does nothing by default.

Then it receives an `AppBuilderContext`:

- `IAppBuilderContributor.Contribute( monitor, context )` reads the `context.Apps` (an `AppConfiguration`: its name,
  its projects, its `Configuration` section, its `Aspects`, its engine `BinPathConfiguration`) and the Builder
  `context.Aspects`, and completes the `context.EngineConfiguration` (what it deduces from the Apps; the aspects have
  been applied before).
  - `AppConfiguration.AddCompileKey( path )` keeps a key out of the projection.
  - `AppConfiguration.AddDeclarationKey( path )` keeps a declaration: a section (a feature of a client) that the
    projection empties (all its keys are compile time keys) is written as `true`.
  - `BinPathConfiguration.AddTypeSet( set )` adds a named type set to the BinPath of the App (a party's, a client's:
    see `TypeSetConfiguration`): a subset of its types, computed at the start of the build.
  - `AppConfiguration.ProjectInSection( key, sectionName )` moves a key in a section of the projection.
  - `AppConfiguration.DeclareInitSection( path )` declares a section read at start: a `"$LOCK"` can restrict it.
  - `AppConfiguration.DeclareAnchor( path, kinds, fallbacks )` declares an anchor (see above).
- `IAppBuilderContributor.OnContributed( monitor, context )` is called once all of them have contributed: a
  contributor can check what the others did.

The builder that finds the contributors and runs them is a consumer of this model (for how it runs them, see [CK.AppBuilder](https://github.com/signature-opensource/CK-StObj/blob/stable/CK.AppBuilder/README.md)).
