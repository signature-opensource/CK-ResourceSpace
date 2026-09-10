# Resource space

A set of packages, each carrying resources, topologically ordered, and projected onto a target - a
folder on disk, or anything that accepts a stream. This package owns the ordering and the projection
machinery; what a resource *means* is left to the handlers.

## A space is built in four steps, and each one hands over a different object.

```csharp
var config = new ResSpaceConfiguration();
var dA = config.RegisterPackage( monitor, "A", default, resStore, resAfterStore, false );
var dD = config.RegisterPackage( monitor, "D", default, resStore2, resAfterStore2, false );
dD.Requires.Add( dA );

var collector = config.Build( monitor );                                // ResSpaceCollector
var spaceData = new ResSpaceDataBuilder( collector ).Build( monitor );   // ResSpaceData
var builder = new ResSpaceBuilder( spaceData );
builder.RegisterHandler( monitor, someHandler );
var space = builder.Build( monitor );                                   // ResSpace
space.Install( monitor );
```

The two `Build` methods that can fail return `null` and log the reason rather than throwing, so the
return value is the thing to test. Bad *arguments* are a different matter and do throw, but only in
the setters and the register methods - `Throw.CheckArgument` on `AppResourcesLocalPath` and
`LiveStatePath`, `Throw.CheckNotNullArgument` on `GeneratedCodeContainer` and on a handler being
registered. No `Build` guards its monitor. The steps are not ceremony: they mark the points where
something becomes immutable.

| Step | What it is for |
|------|----------------|
| [`ResSpaceConfiguration`](Builder/Collector/ResSpaceConfiguration.cs) | registering packages in any order, and the space-wide settings |
| [`ResSpaceCollector`](Builder/Collector/ResSpaceCollector.cs) | the same registrar, carried forward - descriptors are still mutable |
| [`ResSpaceDataBuilder`](Builder/Data/ResSpaceDataBuilder.cs) | the topological sort; produces the final [`ResPackage`](ResPackage.cs) set |
| [`ResSpaceBuilder`](Builder/ResSpaceBuilder.cs) | registering handlers, and initializing them against the sorted packages |

The first step is the odd one: `ResSpaceConfiguration.Build` is typed `ResSpaceCollector?` but its
body has no failure path - it always returns a collector wrapping the *same* `CoreCollector`
instance. Registrations are closed later, by `ResSpaceDataBuilder.Build`. So the null check on that
first call is defensive, not a real branch.

The generated code container is the reason for the last two being separate. It can be assigned at any
of the five stages, `ResSpaceBuilder.GeneratedCodeContainer` being the last chance, so that a code
generator can produce its code *from* the topologically ordered packages it has just been given.

## Every space has a head and a tail you never register.

The sort always inserts two packages: `<Code>` first, `<App>` last. `<Code>` carries the generated
code container, `<App>` carries the application's own local resources
(`ResSpaceConfiguration.AppResourcesLocalPath`). They are not in `ResSpaceCollector.Packages` - the
data builder creates them - and they are reachable as `ResCoreData.CodePackage` and
`ResCoreData.AppPackage`.

So a space of four unconstrained packages orders as:

```
<Code>, A, B, C, D, <App>
```

Determinism when the graph leaves a choice comes from the name: `RevertOrderingNames` sorts
descending instead of ascending, and the fixtures assert both. That switch is a diagnostic - if a
build succeeds one way and fails the other, the graph is missing a constraint.

## A package has two resource sets because its children sit between them.

[`ResPackage.Resources`](ResPackage.cs) and `ResPackage.AfterResources` are the "head" and the "tail"
of the same package, and `Children` are ordered *inside* them. With `A` containing `B` and `C`:

```
Before <Code>, After <Code>,
Before A, Before B, After B, Before C, After C, After A,
Before D, After D,
Before <App>, After <App>
```

That is `ResCoreData.AllPackageResources` verbatim, from
[`TopologicalSorterTests.with_children`](../Tests/CK.ResourceSpace.Tests/TopologicalSorterTests.cs).
A package that contains others can therefore state something before them and override them after -
which is what `Reachables` (head point of view, children excluded) and `AfterReachables` (tail point
of view) express. Note that `AfterReachables` unions each child's *`AfterReachables`*, not its
`Reachables`, so a grandchild's own children are in there too - the doc comment saying "the `Children`
and their `Reachables`" understates it.

`Requires` and `Children` are exclusive: a child is not a requirement and a requirement is not a
child. `RequiredBy` declares the reverse edge, and a package requiring or being required by itself is
silently ignored rather than rejected.

Two failures are worth recognizing by their message. An item contained in two packages:

```
Multiple package error: 'E' is contained in 'B' and 'A' that are both Packages (and not Groups).
```

and a group cycle, which reports the path rather than the cycle length:

```
Cyclic dependency error:
'A' contains 'E', 'B' contains 'E', 'A' requires 'B'
```

A group (`ResPackageDescriptor.IsGroup`) is the escape hatch for the first: several *independent*
groups may contain the same item, several *packages* may not. Independent is the operative word - the
cyclic message above comes from a fixture where two groups sharing an item are also related by a
`Requires`, and that is refused.

## Handlers project, installers write, and that is the whole contract.

A handler is registered on the `ResSpaceBuilder` and initialized by `Build()` once the packages are
final; its job is to turn the available resources into a final projection ready to be installed. Two
base classes, and the choice is made by how the handler recognizes its resources:

```csharp
protected ResourceSpaceFileHandler( IResourceSpaceItemInstaller? installer, params ImmutableArray<string> fileExtensions )
protected ResourceSpaceFolderHandler( IResourceSpaceItemInstaller? installer, string rootFolderName )
```

Both then implement `Initialize` and `Install`. A handler that fails to initialize aborts
`ResSpaceBuilder.Build()`, which returns null - nothing is installed.

Registration is deliberately not symmetric: two folder handlers claiming the same `RootFolderName` is
an **error** and `RegisterHandler` returns false, while two file handlers sharing a file extension is
only a **warning**. The reason is in [`Handler/README.md`](Handler/README.md) - a resource may
legitimately be handled by more than one handler, and a `.t` transformation file is the case that
forces it.

Both of those are about two *different* handler objects. Registering the very same instance twice is
neither: it warns `Duplicate handler registration for '...'. Ignored.` and returns true.

`ResSpace` builds a `FolderExclusion` from the registered folder handlers and passes it to every file
handler's `Initialize`, so a file handler can skip what a folder handler already claims. Note that it
is *passed*, not applied: calling `IsExcluded` is the handler's own responsibility, and a handler that
ignores the parameter will happily see those files. The Live side has no equivalent at all -
`ILiveUpdater.OnChange` relies instead on folder updaters being called first and returning true for
anything under their root folder.

Installers are dumb on purpose: [`IResourceSpaceItemInstaller`](Installer/IResourceSpaceItemInstaller.cs)
is `Open`, `PushSubPath`, three `Write` overloads, `OpenWriteStream` and `Close`. They are not
composable and cannot be chained - the handler is where selection complexity belongs.
[`FileSystemInstaller`](Installer/FileSystemInstaller.cs) writes to a folder;
[`InitialFileSystemInstaller`](Installer/InitialFileSystemInstaller.cs) additionally collects what was
already there and deletes, on success only, whatever it did not write. That deferred cleanup exists so
the target folder is updated in place rather than destroyed and recreated, which would wake every file
watcher pointed at it.

## Cached per-package data is three methods, and only one of them may fail.

[`ResPackageDataCache<T>`](ResPackageDataCache.cs) is a template method that associates an immutable
`T` to each package, computed once and reused:

```csharp
protected abstract T? Create( IActivityMonitor monitor, ResPackage package );
protected abstract T? Combine( IActivityMonitor monitor, IResPackageResources resources, T data );
protected abstract T  Aggregate( T data1, T data2 );
```

`Create` initializes packages that have no `Reachables`, `Combine` applies one resource set to an
existing value, and `Aggregate` merges two independent branches. The asymmetry is the contract:
`Create` and `Combine` return null to signal failure, `Aggregate` **cannot fail** - so an aggregation
that is somehow invalid has to surface inside `T`, not as a null. A `T` that has no way to represent
"these two disagree" makes that impossible to honour.

`InvalidateCache` walks from a local resource set up to `<App>`, which is what makes the Live side
cheap: a changed file invalidates one path through the graph, not the graph.

## The Live state is written at install time, and disabled by a string.

`ResSpace.Install` writes `LiveState.dat` into `ResCoreData.LiveStatePath` - defaulting to
`AppResourcesLocalPath/.ck-watch/`, created with a `.gitignore` containing `*`.

`ResSpaceCollector.NoLiveState` (the literal `"none"`) is documented as suppressing it *"even if
`AppResourcesLocalPath` is specified"*. **The code does not honour that combination.** The per-package
watch root is gated on the marker, but the `<App>` fallback `watchRoot ??= appLocalPath` is not, so
with both set `WatchRoot` is non-null, `Install` takes the writing branch, and `LiveStatePath` - still
the bare string `"none"` - is used as a path prefix: a folder named `none` is created and the state
lands in `noneLiveState.dat`. Set `LiveStatePath` to `"none"` and leave `AppResourcesLocalPath` unset,
or expect that folder.

Two more conditions turn it off in practice, and both only log: no local package and no
`AppResourcesLocalPath` (`WatchRoot` is null, so there is nothing to watch), or no registered handler
that implements [`ILiveResourceSpaceHandler`](Live/ILiveResourceSpaceHandler.cs) with
`DisableLiveUpdate` false. `Install` still returns true in both cases - a missing Live state is not an
install failure.

`ILiveResourceSpaceHandler.ReadLiveState` is the restore side, and it *should* be `static abstract`.
It is `static virtual` returning null, because of [csharplang#5955](https://github.com/dotnet/csharplang/issues/5955),
so a specialized handler that forgets to implement it compiles and then has no Live support.

## Requires.

- `CK.EmbeddedResources` for `IResourceContainer` and the `[EmbeddedResourceType]` /
  `IResourcePackage` / `[Requires<>]` / `[Package<>]` / `[Children<>]` declarations that
  `RegisterPackage( monitor, type )` reads.
- `CK.Engine.TypeCollector` for `GlobalTypeCache` and `ICachedType`.
- `CK.BinarySerialization.Sliced` for the Live state and the serialization of `ResCoreData`.
