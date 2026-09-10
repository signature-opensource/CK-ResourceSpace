A resource space folder handler for translations: it collects a named locales folder from every
package of the space, merges the definitions along the dependency graph, and writes one JSON file per
active culture.

The set of active cultures is fixed at registration, so a package shipping a translation for a
culture nobody activated is reported rather than silently dropped. Install options choose between a
file carrying everything a culture inherits and a file carrying only its own keys.

The application's own resources folder is treated as a pure override folder: it needs no default
file, and its keys are expected to redefine ones the packages already declared.
