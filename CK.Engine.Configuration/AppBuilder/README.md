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
  "GlobalDomainPrefix": "Sample",
  // The defaults of every App: merged in each App, the App wins (the objects are merged).
  "Defaults": { "App": { "Logging": { "LogLevel": { "Default": "Warning" } } } },
  // The builders, keyed by the path of their folder.
  "Builders": {
    "Auth3/Auth3.Auth3.Builder": { "Aspects": { "SqlServer": { ... } }, "App": { "Local": { ... } } },
    "Front/Auth3.Front.Builder": { "App": { "Local": { ... } } }
  },
  // The links between the parties; "Domain", "GlobalDomainPrefix" and "Links" are read by the AppIdentity contributor
  // (the name of an App is the identity of its party: "Auth3.Front" is "$Front" of "Sample/Auth3").
  "Links": [ ... ]
}
```

- **A builder is keyed by the path of its folder,** relative to the root, downward only (`..` is an error). Its entry is
  exactly its `ckonf.json` (`"Aspects"`, `"App"` or `"Apps"`). The names of the Apps are unique in the whole solution.
- **The other root keys belong to the contributors:** the engine of a contributor declares the keys it handles
  (`[assembly: CentralConfigurationKey( "Links", PerDeployment = ["[*]:Link:Address"] )]`, claimed before its
  contributors project; `CentralProjectionContext.ClaimKey` for a key that only its code knows) and its contributors
  complete the Apps of the builder being projected (`IAppBuilderContributor.ProjectFromCentral`). A root key that no contributor claims is an error, except `"$schema"` and
  `"$configurator"` (`CentralConfiguration.ConfiguratorKey`: the source of the configurator's catalog, ignored by the
  builders). The meaning of what
  they project is checked by the builder that reads its projection, as for a hand written `ckonf.json`.
- **A builder of the repository that is not in `"Builders"` is an error** when the repository has a central
  configuration.

### The deployments

The central configuration is the development one (`#Dev`). The other deployments of the solution are in its
`Deployment` folder, next to it (`CentralDeployment`): a folder whose name starts with `#` is a deployment, its
environment, and the folders above it are its domain.

```
Deployment/
  #Production/ckonf.json            the central domain, in "#Production"
  #Staging/ckonf.json               { "$BASE": "#Production", ... }
  Paris/Shop/#Production/ckonf.json the domain "Paris/Shop"
  GenerateAssets.cs, Assets/...     regular folders and files: ignored
```

- **Its name is its path** (`#Production`, `Paris/Shop/#Staging`). Its `Domain` and `Environment` come from it: writing them
  is an error (and writing `GlobalDomainPrefix`, which names the projects of the solution), and so are a `#Dev` folder and a path that spells the central domain (it is `Deployment/#X`). A folder
  whose name starts with `.`, `Deployment/Assets`, and a folder without a `#` folder below it are ignored.
- **Its `ckonf.json` is an overlay** (JSON Merge Patch, RFC 7396, case insensitive): an object merges, `null` removes, any
  other value (an array included) replaces. It overlays the central configuration, or the deployment that its
  `"$BASE"` names. The structure can change (a removed party, another feature): the merged configuration is checked like
  the central one.
- **Every builder run writes its merged configuration**, `ckonf.merged.json`, next to it, when it changes (its first line
  lists its sources).
- **A value "per deployment"** (an address, a public URL) must be written by the deployment itself, in its own overlay
  (`null` counts): inherited from the central configuration or from its base, it is a build error. They are declared:
  - by the configuration: `"$PER_DEPLOYMENT": [ "Demo", "Links:[*]:Link:KeyManagement:AutoTrustKey" ]` on any object of the
    central configuration or of an overlay (the patterns are relative to the object; the markers are removed when the
    file is read, they are never projected);
  - by the code: `[ConfigurationPerDeployment( "Party:Urls" )]` on a configurable type (built like a `[ConfigurationLock]`
    path); `[assembly: ConfigurationPerDeployment( "Remote:Address" )]` on the assembly of a contributor for a property
    of the parties of a kind (in each anchor of the kind and its fallbacks; `AppConfiguration.AddPerDeploymentPartyKey`
    when only the code knows it); `AppConfiguration.AddPerDeploymentKey( path )` for a reader of an App; the
    `PerDeployment` of an `[assembly: CentralConfigurationKey]` (or `CentralProjectionContext.AddPerDeploymentKey`) for a
    contributor's central keys;
  - by the builder: the size of a fleet (`"InstanceCount"`, `"Instances"`, `"MaxInstanceCount"` of an App that is a
    template).

  A value of an App is the one of its entry in `"Builders"`, otherwise the one of `"Defaults:App"`: the deployment writes
  the one it uses. `CentralDeployment.CheckPerDeployment` checks them (the builder runs it for its own Apps); the rule is
  `CentralDeployment.FindInheritedValues` (data, no file: the configurator checks the documents it edits with it, like
  `DiscoverFolders` and `ValidateName` for the folders and the names of the deployments).
- **The settings of a deployment** that are not configuration are its `"Deployment"` key (`CentralDeploymentSettings`;
  in the central configuration, the defaults of every deployment, merged like the rest): `"Builders"` (the builders to
  deploy, all by default), `"SingleZipAsset"` (one asset for the deployment instead of one per App), `"Publish"` (the MSBuild properties of the publication of every host, applied as-is:
  `{ "RuntimeIdentifier": "linux-x64", "SelfContained": true, "PublishSingleFile": true }`) and `"Apps"` (the ones of an
  App, over them: `"Apps": { "Auth3.Worker": { "Publish": { "PublishSingleFile": "" } } }`, an empty value resets a
  property). They are not modeled: only their shape is checked (`CentralDeploymentSettings.GetPublishProperties`).
- **The build of a deployment** (CK.AppDeployment) runs, for each deployed builder, a builder generated for it: its folder
  has a `ckonf.deployment.json` (`{ "Deployment": "#Production", "Builder": "Front/Auth3.Front.Builder" }`,
  `CentralDeployment.WriteBuilderFile`), and its `ckonf.json` is the projection of the builder in the merged configuration
  (`CentralDeployment.WriteBuilderProjection`): the `"AppProject"` of each App is the original `.App` project, its host is
  the copy next to the generated builder. Its `AppBuilderContext.Deployment` is the deployment: `TryResolveSecret` fails
  (the build of a deployment never reads a secret, never touches a database). It writes the manifest of each host,
  `$StObjGen/Deployment.manifest` (`AppBuilderConfiguration.WriteDeploymentManifests`; CK.AppDeployment adds it to the
  file that the publication writes next to the executable): the full name, the deployment and its bases, the
  publication properties of the host, the names of the secrets the host needs ("Secrets") and of the optional ones ("OptionalSecrets"), its values per deployment; for a fleet, its instances (each configured one with
  its full name, its start arguments `--Instance=Name` and its values per deployment resolved, and the indexes of the
  dynamic ones of an Opened fleet). The asset name of a deployment
  (`CentralDeployment.AssetName`) drops the `#` and maps `/` to `.`: `Production`, `Paris.Shop.Staging`; the asset
  folder of an App (`CentralDeployment.GetAssetFolder`) is `Production-Auth3.Front` (one asset per App, the default) or
  `Production/Auth3.Front` (`"SingleZipAsset": true`).

## The aspects

An aspect is a configurable type of a model, declared with CK.AppBuilder.Abstractions: a package (a concrete
`IResourceGroup`) decorated with
`[AppBuilderAspect( "Name", TypeActivation.X )]` (a Builder aspect, `IAppBuilderAspect`: one per engine run, at the root
`"Aspects"`) or `[AppAspect( "Name", TypeActivation.X )]` (an App aspect, `IAppAspect`: in the `"Aspects"` of an App).
The builder finds them in the type sets of the Apps, before the contributors run:

- **The types drive, the sections configure:** an aspect is present when it is a member of the set of an App (its
  activation `ByDefault` or `Required`) or when its key is declared (`Optional`), unless its key is `false` (an error
  for a `Required` one). Its key never brings its type in.
- **The aspect declares its data:** it has one public constructor whose optional parameters are its section (a plain
  `IConfiguration`, null for a bare `true` or when its activation makes it present) and its options (below); it reads
  its settings and throws on an invalid value.
- **An aspect has options:** an `IAppAspectOption<TAspect>` decorated with `[AppAspectOption( "Name", TypeActivation.X )]`
  (or an `IAppBuilderAspectOption<TAspect>` with `[AppBuilderAspectOption]`) is configured in the section of its aspect
  by its name, like the options of the party features: a section or `true` enables it, `false` disables it, nothing
  is its activation (`ConfigurationOptionSelector` of CK.Configuration). The builder orders the options of the aspect
  (the topology attributes between options, `ConfigurableOptions` of CK.ResourceSpace), selects and creates them (one
  public constructor, with its optional `IConfiguration` section), then gives them to the aspect's constructor: its
  `IReadOnlyList<IAppAspectOption<TAspect>>` parameter (an option of a base aspect applies to the derived ones).
- **It applies itself:** it implements `IAppBuilderAspectApply` or `IAppAspectApply` (here: `IAppBuilderAspect` and
  `IAppAspect` are markers, their `Apply` works on the builder's model). `IAppBuilderAspectApply.Apply( monitor, context,
  apps )` (the Apps whose set contains it) or `IAppAspectApply.Apply( monitor, context, app )` ensures and configures its engine aspect configurations and projects its
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
  on a configurable type: the first segment is a kind of anchor, the rest is relative to the type's section. The section
  of a type is its configurable name (a feature in a party) unless a contributor resolves it
  (`IAppBuilderContributor.ResolveSections`): an option is in the section of its owner, in each concrete owner of the App
  (the AppIdentity contributor resolves the options of the features and of the client features, the clients are the
  "Client" anchors). With a `Value`, another configured value is an error and an absent one is burnt with it.
- **A Builder aspect's `"$LOCK"`** locks the values it projects in the hosts (`AppConfiguration.ProjectValue( path, value, origin )`).
- **The errors:** an invalid level or pattern, a value that no code declares, a level above the capability of the code.

The builder applies them (`AppBuilderConfiguration.ApplyLocks`) once the contributors have contributed.

## The secrets

A secret is never written in a configuration file: its place holds a **handle**, the name of the secret
(`ConfigurationSecret`, CK.ResourceSpace.Abstractions), that the host resolves when it starts:

```jsonc
"Oidc": { "Schemes": { "Google": { "ClientId": "...", "ClientSecret": { "$SECRET": "Google:ClientSecret" } } } }
```

- **A handle is a value like another one:** in the flattened configuration it is the key `"<path>:$SECRET"` whose value
  is the name. It is projected as is in the host's `appsettings.json`; a `"$LOCK"` to `Compile` on the value burns the
  handle, never a value (the name is fixed by the build, the value is still read at start). An aspect projects a
  handle with `AppConfiguration.ProjectValue( "<path>:$SECRET", name, "<origin>:$SECRET" )`: the `"$LOCK"` of the value it
  replaces applies.
- **A name** is made of segments separated by `:` made of letters, digits, `_` and `-` (`Duende:ClientSecret`); the
  handle is the only key of its object. Anything else is an error. The handles of a fleet App may use its placeholders
  (`{ "$SECRET": "Worker:{Name}:Key" }`: a secret per instance, resolved with the instance at start): the name of each
  instance is checked; a secret that the build itself needs (`AppBuilderContext.TryResolveSecret`) has no placeholder.
- **The value** is the configuration key `Secrets:<Name>` of the host: see CK.AppHost. In development, the solution's
  `#Dev/Secrets.json` holds them (its root is the content of the `Secrets` section: `{ "Google": { "ClientSecret": "..." } }`).
- **The code declares what is a secret:** `[ConfigurationSecret( "Party:Oidc:Schemes:*:ClientSecret" )]` on a configurable type
  (built like a `[ConfigurationLock]` path: a kind of anchor, then a path relative to the type's section, with
  patterns), `AppConfiguration.AddSecretKey( path )` for a reader that reads its keys by hand. A plain value where a
  secret is declared is reported: the development configuration accepts it (a known test secret, an Integrated Security
  connection string).
- **An engine that needs the value at build time** (the setup of a development database) resolves it with
  `AppBuilderContext.TryResolveSecret`, from the `#Dev/Secrets.json` file (`AppBuilderConfiguration.TryLoadDevSecrets`):
  the value is never projected nor burnt.
- **An optional handle**, `{ "$SECRET?": "<Name>" }`, names a secret that may be missing: the host then leaves its path
  absent, as if nothing were configured there (a one-time enlistment ticket, a feature activated by its secret). The
  deployment, not the code, chooses it: the rules of a handle apply (`"<path>:$SECRET?"` in the flattened configuration,
  projected and burnt as is).

The builder checks them (`AppBuilderConfiguration.CheckSecrets`) with the restrictions.

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

The builder that finds the contributors and runs them is a consumer of this model (for how it runs them, see [CK.AppBuilder](https://github.com/signature-opensource/CK-AppBuilder/blob/stable/CK.AppBuilder/README.md)).
