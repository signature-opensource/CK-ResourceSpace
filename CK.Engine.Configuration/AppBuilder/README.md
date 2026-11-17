# The App builder model

`AppBuilderConfiguration` reads the `ckonf.json` of a "XXX.Builder" project, creates the `EngineConfiguration`
(a BinPath per App) and writes the projections (the `appsettings.json` of the hosts).

## `ckonf.json`: the configuration of the builder

When the "XXX.Builder" project has a `ckonf.json`, it is the configuration of what the builder builds in one engine run:
its Apps, keyed by their name, each one with the configuration of its host, and the aspects.

```jsonc
{
  // The Builder aspects: they apply to the whole engine run.
  "Aspects": { "SqlServer": { "DefaultDatabaseConnectionString": "Server=.;Database=MyDB;Integrated Security=True" } },
  "Apps": {
    "Back1": {
      "Types": [ "CK.Core.IActivityMonitor, CK.ActivityMonitor",
                 { "Name": "CK.Core.IParallelLogger, CK.ActivityMonitor", "Kind": "IsContainerConfiguredService|IsScoped" } ],
      // The aspects of this App.
      "Aspects": { "Globalization": { "ActiveCultures": "fr, en" } },
      "FullName": "Sample/MultiBinPath/$Back1/#Dev",
      "Local": { ... },
      "CK-Monitoring": { ... }
    },
    "Back2": { ... }
  }
}
```

- **An App is a process:** the App "Back1" is the "../Back1.App" project (its code; its bin folder is the one of the
  builder: `bin/{Configuration}/{TargetFramework}`) and the "../Back1.Host" project (its host, the `ProjectPath` of
  its engine BinPath). `"AppProject"` and `"HostProject"` (relative to the `ckonf.json` folder) depart from this
  convention. A path that leaves the solution's folder is an error.
- **The compile time keys of an App:** `Types` (an assembly qualified name, or `{ "Name", "Kind" }`),
  `ExcludedTypes`, `Assemblies`, `DiscoverAssembliesFromPath`, `CompileOption`, `GenerateSourceFiles`, `AppProject`,
  `HostProject` and `Aspects`. The other keys are the configuration of the host.
- **The aspects are compile time,** at the root (the Builder aspects) and in an App (its aspects), keyed by their
  name (see [The aspects](#the-aspects) below). An aspect key that nothing reads is an error.
- **The keys are case insensitive** (it is a configuration): a duplicate key (`Back1` and `back1`) is an error. The
  order of the Apps means nothing: the engine receives them sorted by name.
- **The host's `appsettings.json` is a projection of its App:** its content without the compile time keys (the ones
  above, and the ones the contributors declare, unless `"ProjectCompileKeys": true`), the keys that the contributors
  project in a section moved in it (the keys of the party in `"CK-AppIdentity"`). It is generated (its first line
  says so) and written only when it changes: edit the `ckonf.json`, not the projection.
- **A `CKSetup.xml` may complete it** with the aspects that are not deduced yet and the global options of the engine,
  but no BinPath.

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

## The contributors' contract

The engines deduce the engine configuration from the `ckonf.json`: they declare an `IAppBuilderContributor` with
`[assembly: AppBuilderContributor( typeof( MyContributor ) )]`. It receives an `AppBuilderContext`:

- `IAppBuilderContributor.Contribute( monitor, context )` reads the `context.Apps` (an `AppConfiguration`: its name,
  its projects, its `Configuration` section, its `Aspects`, its engine `BinPathConfiguration`) and the Builder
  `context.Aspects`, and completes the `context.EngineConfiguration` (what it deduces from the Apps).
  - `AppAspects.TryGetAspect( name )` reads an aspect section and marks it as handled (for a contributor that reads an
    aspect that is not yet an aspect type).
  - `AppConfiguration.AddCompileKey( path )` keeps a key out of the projection.
  - `AppConfiguration.AddDeclarationKey( path )` keeps a declaration: a section (a feature of a client) that the
    projection empties (all its keys are compile time keys) is written as `true`.
  - `BinPathConfiguration.AddTypeSet( set )` adds a named type set to the BinPath of the App (a party's, a client's:
    see `TypeSetConfiguration`): a subset of its types, computed at the start of the build.
  - `AppConfiguration.ProjectInSection( key, sectionName )` moves a key in a section of the projection.
- `IAppBuilderContributor.OnContributed( monitor, context )` is called once all of them have contributed: a
  contributor can check what the others did.

The builder that finds the contributors and runs them is a consumer of this model (for how it runs them, see [CK.AppBuilder](https://github.com/signature-opensource/CK-StObj/blob/stable/CK.AppBuilder/README.md)).
