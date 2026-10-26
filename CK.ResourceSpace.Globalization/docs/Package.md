A resource space folder handler for translations.

`LocalesResourceHandler` collects a named locales folder from every package of the space, merges the
definitions along the dependency graph and writes one JSON file per culture of its `ActiveCultureSet`,
shaped by its `InstallOption`. The application's own resources folder is a pure override folder.
