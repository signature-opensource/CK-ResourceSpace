# Handlers
Resource space handlers are either [file](ResourceSpaceFileHandler.cs) or [folder](ResourceSpaceFolderHandler.cs)
handlers.

They are stateful objects that are registered on the `ResSpaceBuilder` and initialized
by an internal call from `ResSpaceBuilder.Build()` once the final `ResSpace` has been
created. Their goal is to analyze the available resources and create a final projection of the
resource space that is ready to be installed.

A handler's final projection is conceptually a namespace that contains final "items"
that can eventually be written, exported, installed in the external world.

They optionally can be [`ILiveResourceSpaceHandler`](../Live/ILiveResourceSpaceHandler.cs) and
participate in the serialization of a Live state and the activation of a [`ILiveUpdater`](../Live/ILiveUpdater.cs).

## One handler, one installer

In order to keep the system as simple as possible, we consider that "handler ⇒ installer"
instead of introducing a potentially complex "mapping" between resource, handlers and the
final result. We simply associate an installer instance when a handler is created.
Below are the constructors of the 2 kinds of handlers:
```csharp
protected ResourceSpaceFileHandler( IResourceSpaceItemInstaller? installer, params ImmutableArray<string> fileExtensions )
protected ResourceSpaceFolderHandler( IResourceSpaceItemInstaller? installer, string rootFolderName )
```

This simplicity implies that the installer interface is really basic and that
different handlers can target the same installer. Moreover, the optional Live aspect
implementation is fully managed by the handler: installers are unaware of any Live aspect,
all the Live stuff is on the handler side.

This final projection is kept internally by each handler and its `bool Install( IActivityMonitor monitor )`
method installs this final projection into its installer. The internal representation can
be complex but this is the responsibility of the handler.

Installers are dumb: their interface is not composable, they cannot be implemented
as a Chain of Responsibility or as a Composite (`Stream OpenWriteStream( NormalizedPath path )`
hands out the stream itself). Handlers concentrate any required item selection complexity and
decide whether an item must be written to the installer or not.

## Selecting resources

A handler can easily select the resource subspace that it considers by filtering the
`ResPackage` (by name and/or by its associated C# Type if there's one), by resource folder
name (this is basically how `ResourceSpaceFolderHandler` works) or by file extension (this is
basically how `ResourceSpaceFileHandler` works) or by any other "key" resource (like file name
pattern, a resource sub folder, a specific file in a resource folder, etc.).

A folder-based `ResourceSpaceFolderHandler` (for instance the "assets"
or "locales") is a simple case where the handler naturally drives the installer:
a "ts-assets" folder contains TypeScript assets that target a physical folder, through a
[`FileSystemInstaller`](../Installer/FileSystemInstaller.cs).

It is almost as easy for simple files where the file extension is enough to conclude on
their destination. But it is less obvious for files that support transformations: how can a handler
know if a (potentially multi-target) ".t" file should be handled or ignored? When files of two
different targets coexist in a resource space and can all be transformed, two file handlers are
set up, each with its own installer, and ".t" files can apply to both of them.

To keep the system simple (handler ⇒ installer), it's up to the handler to choose by analyzing
the package that contains the resources, with any heuristic: for instance, the kinds of package
each handler targets can be considered mutually exclusive.

This has an important consequence: a resource CAN be handled by more than one handler and
this can be used as a feature (or as a source of bugs!). This is why two file handlers sharing a
file extension is only a warning when they are registered.

Namespaces of different handlers should be independent, but technically nothing prevents
2 handlers from writing the same final item.

Whether a specific item of a final projection must be installed by submitting it to the provided
installer is under control of the handler: a handler may skip some items or write
different contents than the original resources.
