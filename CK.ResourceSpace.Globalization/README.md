# Locales folder handler

One [`ResourceSpaceFolderHandler`](../CK.ResourceSpace/Handler/ResourceSpaceFolderHandler.cs) that
collects a named locales folder from every package of a resource space, merges the translations along
the dependency graph, and writes one JSON file per culture.

> ℹ️ Read [CK.ResourceSpace](../CK.ResourceSpace/README.md) for the build steps and the
> handler/installer contract, and [CK.EmbeddedResources.Globalization](../CK.EmbeddedResources.Globalization/README.md)
> for what a locales folder contains and what merging means. This package is the junction of the two.

## Registering it takes the culture set as well as the folder name.

```csharp
var handler = new LocalesResourceHandler( new FileSystemInstaller( targetPath ),
                                          spaceData.CoreData.SpaceDataCache,
                                          "ts-locales",
                                          new ActiveCultureSet( cultures ),
                                          LocalesResourceHandler.InstallOption.Minimal );
builder.RegisterHandler( monitor, handler );
```

The `ActiveCultureSet` is a handler-level decision, not a per-package one: a package shipping
`de.jsonc` while `de` is not active gets a warning and its file is skipped. Deciding this once, at
registration, is what makes the skip diagnosable instead of silent.

## The output is `.json`, the input is `.jsonc`.

Files are written as `<culture name>.json` under the root folder name - so `ts-locales/fr-FR.json` -
with `Utf8JsonWriter` and `Indented = true`. The source files must be `.jsonc`; `.json` is refused on
the way in. The asymmetry is deliberate: comments are for the author of the translations, and what is
installed is consumed by a program.

`InstallOption` is a `[Flags]` enum with `Full = 0` as its default, and its three bits are three
independent questions:

| Value | Effect |
|-------|--------|
| `Full` (0) | each culture's file carries `RootPropagatedTranslations` - its own plus everything inherited |
| `Minimal` (1) | each culture's file carries only its own `Translations` |
| `WithoutEmptySet` (2) | cultures that ended up with no translation of their own get no file |
| `WithSortedKeys` (4) | keys are ordered instead of following insertion |

`Full` means a caller can load one file and be done; `Minimal` means it must walk the fallback chain
itself. Note that `WithoutEmptySet` changes which cultures get a file, and it tests
`set.Translations.Count > 0` - the set's own translations - regardless of whether `Full` would have
propagated something into it.

Without that flag, a file is written for **every** active culture, and `Install` does not skip an
empty handler either. The comment in the code says why: *"we want the translations files to be
available (empty) so the structure is here"* - a consumer resolving `ts-locales/de.json` always gets
a file rather than a 404.

What is in that file is worth being precise about, because `Minimal` does not mean "only what this
culture declared". `WriteAllCultures` first resolves the set with `FindTranslationSetOrParent( c )`,
so a culture with no set of its own is already standing on its closest ancestor; the flag then
chooses between that ancestor's propagated translations and that ancestor's *own* ones. An empty file
therefore appears only when the resolved ancestor is itself empty.

## The application's own locales folder is an override folder, automatically.

The internal cache is a `ResPackageDataCache<FinalTranslationSet>`, and its `Combine` reads:

```csharp
resources.Resources.LoadTranslations( monitor,
                                      _activeCultures,
                                      out var definitions,
                                      _rootFolderName,
                                      isOverrideFolder: resources.IsAppResources )
```

`IResPackageResources.IsAppResources` is true for exactly one resource set in the whole space: the
`<App>` package's own resources. So the folder pointed at by
`ResSpaceConfiguration.AppResourcesLocalPath` needs no `default.jsonc`, and every key in it is a
`Regular` override - it is meant to redefine what the packages already declared.

"Meant to" is the accurate verb: a key there that overrides nothing is a **warning**, not an error -
*"Invalid override 'O:{key}' in {origin}: this key is not defined by any reachable translation
sets."* - and the key is dropped. So a typo in an application override loses the translation silently
unless someone reads the build log.

That is the intended asymmetry between a package and the application: packages *define* translations,
the application *corrects* them. Nothing in the folder itself says so - the behaviour comes from its
position in the space.

## An ambiguity is an initialization failure, and it names both texts.

`Initialize` computes the final set and returns false when it is ambiguous, which makes
`ResSpaceBuilder.Build()` return null. The error names the key, the origin and the text on each side:

```
Ambiguities detected in final translations:
```

followed, per key, by the origin and text of the winner and of every rival - so the message alone is
enough to find both files.

So `FinalTranslations`, once non-null, is unambiguous, and `Install` asserts that rather than
re-checking. As with any folder handler, a null `Installer` is only reported - `No installer
associated to '...'. Skipped.` - which is how the Live side reuses the merge without writing.

## Live update carries the stable sets in a side file.

The primary live state holds the target path, the active culture names, the root folder name and the
`InstallOption`. The already-computed final sets of the stable packages are too big for it, so they go
to `<LiveStatePath>/Folder/<root folder name>.dat` - `.ck-watch/` being only the default LiveStatePath -
and are loaded lazily, on the first change that actually
matters. That is what lets the Live process avoid re-reading resources from assemblies it would
otherwise have to load.

`OnChange` claims every path under the root folder - returning true even for a file it ignores, since
that is the contract that keeps file handlers from also seeing it - but only invalidates the cache for
a `.jsonc` whose name is an active culture, or for a change to the folder itself. Anything else is
traced and dropped.

Two things to know before specializing this handler. `WriteLiveState` is `virtual` but `ReadLiveState`
is `static`, so an override of the first without a matching static on the derived type leaves the
handler with no Live restore - the class summary states the requirement, the compiler cannot. And the
live updater is a sealed private class: changing its behaviour means wrapping it in your own
`ILiveUpdater`, not deriving from it.

## Requires.

- `CK.ResourceSpace`, for the handler base class, the installers and the data cache.
- `CK.EmbeddedResources.Globalization`, for `ActiveCultureSet`, `LoadTranslations` and
  `FinalTranslationSet`.
