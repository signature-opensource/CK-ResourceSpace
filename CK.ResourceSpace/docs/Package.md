A set of resource packages, topologically ordered, and projected onto an installation target.

Packages are registered from a type or from any resource container, in any order, and declare
`Requires`, `RequiredBy` and `Children` constraints. The sort inserts a `<Code>` head and an `<App>`
tail, and orders each package's children between its own two resource sets.

What a resource means is left to handlers: a folder handler claims a root folder name, a file handler
claims file extensions, and both write through a deliberately minimal installer interface. A
per-package data cache and an optional Live state, updated as local files change, come with it.
