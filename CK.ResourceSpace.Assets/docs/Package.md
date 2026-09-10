A resource space folder handler for assets: files that are copied as they are, not transformed.

Registered with a root folder name - "assets", "ts-assets" or anything else - it collects that folder
from every package of the space, merges the sets along the dependency graph, and installs the result
under the same folder name. Where each file lands comes from the "mappings" of an `assets.jsonc`,
not from the resource path.

Two packages claiming one target path without an override declaration is an ambiguity, and an
ambiguity fails the handler's initialization, which aborts the whole space build. Live update is
supported when the installation target is the file system.
