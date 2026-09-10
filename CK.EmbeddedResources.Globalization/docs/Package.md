Reads a locales folder out of any resource container and turns it into a tree of translations that can
be merged with the translations of other containers.

A folder is a required `default.jsonc` holding the english root, plus one `.jsonc` file per culture,
named after it. A key prefixed `O:`, `?O:` or `!O:` overrides a definition from another component -
strictly, optionally, or either way - and redefining a key without a prefix is refused.

Two merge operations with different tempers: combining definitions onto a base set reports conflicts
and resolves ambiguities, while aggregating two independent branches records a key defined twice with
differing texts as an ambiguity in the value.
