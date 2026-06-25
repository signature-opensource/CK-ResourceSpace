# Handlers
Resource space handlers are either [file](ResourceSpaceFileHandler.cs) or [folder](ResourceSpaceFolderHandler.cs)
handlers.

They stateful objects that are registered on the `ResourceSpaceBuilder` and initialized
by an internal call from `ResourceSpaceBuilder.Build()` once the final `ResourceSpace` has been
created. Their goal is to analyze the available resources and create a final projection of the
resource space that is ready to be installed. 

A handler's final projection is conceptually a namespace that contains final "items" 
that can eventually be written, exported, installed in the external world.

They optionally can be [`ILiveResourceSpaceHandler`](../Live/ILiveResourceSpaceHandler.cs) and
participates in the serialization of a Live state and the activation of a [`ILiveUpdater`](../Live/ILiveUpdater.cs).

In order to keep the system as simple as possible, we consider that "handler ⇒ installer"
instead of introducing a potentially complex "mapping" between resource, handlers and the
final result. We simply associate a installer instance when a handler is initialized.
Below are the constructors of the 2 kind of handlers:
```csharp
protected ResourceSpaceFileHandler( IResourceSpaceItemInstaller? installer, params ImmutableArray<string> fileExtensions )
protected ResourceSpaceFolderHandler( IResourceSpaceItemInstaller? installer, string rootFolderName )
```

This simplicity implies that the installer interface is really basic and that 
different handlers can target the same installer. Moreover, the optional Live aspect
implementation is fully managed by the handler: installers are unaware of any Live aspect,
all the Live stuff is on the handler side.

This final projection is kept internally by each handlers and their `bool Install( IActivityMonitor monitor )`
methods installs this final projection into an installer. The internal representation can
ba complex but this is the responsibility of the handler.

Installers are dumb, their interface is currently not composable, they cannot be implemented
as a Chain of Responsibility or as a Composite (because of the `Stream OpenWriteStream( NormalizedPath path )`
that should take a `Func<Stream>`) and this is intended (at least for the moment).

Handlers should concentrate any required item selection complexity and whether
it must be written to the installer or not.

A handler can easily select the resource subspace that it considers by filtering the
`ResPackage` (by name and/or by its associated C# Type if there's one), by resource folder
name (this is basically how `ResourceSpaceFolderHandler` work) or by file extension (this is
basically how `ResourceSpaceFileHandler` work) or by any other "key" resource (like file name
pattern, a resource sub folder, a specific file in a resource folder, etc.).

A folder-based `ResourceSpaceFolderHandler` (for instance the "assets"
or "locales") is a simple case where the handler naturally drives the installer.
A "ts-assets" folder contains TypeScript assets that target the physical folder "ck-gen/" (the
[file system](../IFileSystemInstaller.cs) is the implicit choice) and a "sql-assets" (if
it were to exist) would target a "SqlInstaller".

It is almost as easy for simple files where the file extension is enough to conclude on
its destination. But it is less obvious for files that support transformations. How a handler
can know if a (potentially multi-target) ".t" file should be handled or ignored?

In a resource space where "sql" and "ts" files coexist (that can all be transformed), we must
be able to setup 2 handlers:
- One for the TypeScript files (".ts", ".css", ".less", ".htm" or .html) that will target
  a `IFileSystemInstaller`.
- One for ".sql" files that will target a "SqlInstaller".
- And ".t" files can apply to both of them.

To keep the system simple (handler ⇒ installer), we consider that it's up to the handler
to be able to chose by analyzing the package that contains the resources and use any
heuristic to decide. And this is easy: we must consider that being a "SqlPackage" and a
"TypeScriptPackage" is mutually exclusive.

This has an important consequence: a resource CAN be handled by more than one handler and
this can be used as a feature (or as a source of bugs!).














Namespaces of different
handlers should be independent, but technically nothing prevents 2 handlers to write the same
final item (the last registered handler will win). 

Whether a specific item of a final projection must be installed by submitting it to the provided
installer is under control of the handler: handlers may ignore some installers or chose to write
different contents to different installers.



