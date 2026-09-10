# Assets folder handler

One [`ResourceSpaceFolderHandler`](../CK.ResourceSpace/Handler/ResourceSpaceFolderHandler.cs) that
collects a named folder of assets - files copied as they are, not transformed - from every package of
a resource space, merges them, and installs the result.

> ℹ️ Read [CK.ResourceSpace](../CK.ResourceSpace/README.md) first: the four build steps and the
> handler/installer contract are its concepts, this package only fills one slot.

## Registering it is one line, and the folder name is the parameter.

```csharp
var handler = new AssetsResourceHandler( new FileSystemInstaller( targetPath ),
                                         spaceData.CoreData.SpaceDataCache,
                                         "ts-assets" );
builder.RegisterHandler( monitor, handler );
```

The root folder name - `"assets"`, `"ts-assets"`, or anything else - is what the handler looks for in
each package's resources, and it is also pushed as a sub path at install time, so every asset is
written at `<target>/<root folder name>/<its key in the final set>`. The same handler type can be
registered twice with two different folder names; two *different* handlers claiming the same root
folder is an error, since one root folder has one handler. Registering the identical instance twice is
not that error - it warns `Duplicate handler registration` and is ignored.

That key is not the resource's own path. It comes from the mapping declared in an `assets.jsonc`
beside the file:

```jsonc
{
    "mappings": {
        "logo.png": "logos"
    }
}
```

And it is not prefixed per package either, even though `Combine` and `Create` both pass
`package.DefaultTargetPath` to `LoadAssets`. Two packages in different namespaces shipping that exact
`assets.jsonc` collide - which is what the fixtures below rely on.

## The merge is a cache, and it is where collisions are decided.

`AssetsResourceHandler` owns an internal `ResPackageDataCache<FinalResourceAssetSet>` and implements
the three methods with the asset types of `CK.EmbeddedResources.Assets`:

| Method | What it does |
|--------|--------------|
| `Create` | `LoadAssets` on the package's own resources, then `ToInitialFinalSet` |
| `Combine` | `LoadAssets` on one resource set, then `definitions.Combine( monitor, data )` |
| `Aggregate` | `data1.Aggregate( data2 )` |

A package with no `assets.jsonc` and no asset folder contributes nothing and is not an error:
`LoadAssets` succeeds with a null definition set, and the incoming data passes through unchanged.

Which of `Combine` and `Aggregate` sees a given pair of packages is decided by the graph, not by the
handler: a dependency relation between two packages routes their assets through `Combine`, two
unrelated siblings through `Aggregate`. Both must therefore recognize the same conflicts, and
[`AssetCollisionTests`](../Tests/CK.ResourceSpace.Tests/AssetCollisionTests.cs) is the end-to-end
check that they do - the same two packages, both mapping `logo.png` to `logos`, refused as siblings
and refused as a dependency pair, with nothing installed either way.

## An ambiguity is an initialization failure, not a warning.

```csharp
protected override bool Initialize( IActivityMonitor monitor, ResCoreData spaceData )
{
    _finalAssets = GetUnambiguousFinalAssets( monitor, spaceData );
    return _finalAssets != null;
}
```

`FinalResourceAssetSet.IsAmbiguous` - two packages claiming one target path with no override
declaration - is turned into an error listing every offending key:

```
Ambiguities detected in assets:
'logos/logo.png' is mapped by ... but also to ....
```

and `Initialize` returns false, which makes `ResSpaceBuilder.Build()` return null. Nothing is
installed, and `FinalAssets` stays null. So `FinalAssets`, once non-null, is guaranteed unambiguous -
which is why `Install` asserts it with `Throw.CheckState` rather than re-testing it.

An `Installer` of null is the one case that is merely reported: `Install` logs
`No installer associated to '...'. Skipped.` and returns true. That is not an accident - it is how
the Live side reuses the handler's merge logic without letting it write anything.

## Live update requires a file system installer, silently.

```csharp
public bool DisableLiveUpdate => Installer is not FileSystemInstaller;
```

The Live state stores the installer's `TargetPath` and the root folder name, and rebuilds a plain
[`FileSystemInstaller`](../CK.ResourceSpace/Installer/FileSystemInstaller.cs) on the other side. A
handler installed through anything else has no way to be serialized, so it opts out, and nothing
warns about it: the space just reports that no enabled Live handler exists.

Read the test literally, though: it is `is not FileSystemInstaller`, so a *subclass* keeps Live
enabled - and then loses itself, because only the target path crosses over and the live side
reconstructs the base class. `InitialFileSystemInstaller`, whose whole purpose is deleting files it
did not write, becomes a plain writer on the Live side.

On the Live side the updater invalidates the cache for the changed resources, then recomputes the
whole final set and rewrites it. There is no per-file update path and none is claimed - the handler's
own summary says *"Live support currently uses no cache"*.

## Requires.

- `CK.ResourceSpace`, for the handler base class, the installers and the data cache.
- `CK.EmbeddedResources.Assets`, for `LoadAssets`, `ResourceAssetDefinitionSet` and
  `FinalResourceAssetSet`.
