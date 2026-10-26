A resource space folder handler for assets: files that are copied as they are, not transformed.

`AssetsResourceHandler` collects a named folder ("assets", "ts-assets"...) from every package of the
space, merges the sets along the dependency graph and installs the result. An ambiguity fails the space
build; Live update is supported when installing through a `FileSystemInstaller`.
