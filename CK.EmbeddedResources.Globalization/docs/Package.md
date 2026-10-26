Reads a locales folder out of any resource container and merges translations across containers.

The `LoadTranslations` extension method reads a folder into a `TranslationDefinitionSet`; merging
produces a `FinalTranslationSet` with override (`O:`, `?O:`, `!O:` key prefixes) and ambiguity
semantics. `ActiveCultureSet` and `ActiveCulture` define the cultures to consider.
