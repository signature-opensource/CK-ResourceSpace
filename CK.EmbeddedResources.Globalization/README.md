# Translations in a resource container

Reads a `locales/` folder out of any `IResourceContainer` and turns it into a tree of translations
that can be merged with the translations of other containers. Merging is the point: the same key may
be defined once and overridden elsewhere, and the result has to say when two definitions disagree.

## The folder is one required file plus one file per culture.

```csharp
bool LoadTranslations( this IResourceContainer container,
                       IActivityMonitor monitor,
                       ActiveCultureSet activeCultures,
                       out TranslationDefinitionSet? translations,
                       string folder,
                       bool isOverrideFolder = false )
```

```
ts-locales/
  default.jsonc          <- required, and it is the "en" root
  fr-FR.jsonc
  de.jsonc
  en/
    en-GB.jsonc
  en-US.jsonc
```

Sub-folders exist only to group files: the culture comes from the file name, so `en/en-GB.jsonc`
defines `en-GB` and the `en/` folder itself means nothing. Parent cultures are not synthesized -
there is no `fr` set above, and none is created.

Reading one is a single call on the container, and the result is a tree walked by culture:

```csharp
var p = new CodeGenResourceContainer( "P" );
p.AddText( "locales/default.jsonc", """{ "Msg": "Hello" }""" );
p.AddText( "locales/fr.jsonc", """{ "Msg": "Bonjour" }""" );
p.AddText( "locales/fr-FR.jsonc", """{ "Msg": "Salut" }""" );

p.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var set, "locales" );

set.Translations["Msg"].Text;                                    // "Hello" - the "en" root
var fr = set.Children.Single( s => s.Culture.Culture == C.Fr );
fr.Translations["Msg"].Text;                                     // "Bonjour"
```

Adapted from [`ReadDefinitionsTests.basic_reads`](../Tests/CK.EmbeddedResources.Globalization.Tests/ReadDefinitionsTests.cs) -
the assertions there are replaced by the values they assert. `CodeGenResourceContainer` is what makes
the fixture readable; any `IResourceContainer` works, and in a real package it is the assembly's
embedded resources.

Note `set.Children` rather than a lookup: `fr` is a child of the root and `fr-FR` is a child of `fr`.
The tree follows the culture hierarchy, not the folder layout.

Four rules for a regular folder. All four log an error rather than falling back - but see below, an
error logged is not always `false` returned:

- `default.jsonc` must exist - *"This file must contain all the resources in english"* - and it is
  the root of the tree, matching `NormalizedCultureInfo.CodeDefault`.
- every other file must end in `.jsonc`. `.json` is refused: *"Only '.jsonc' files must appear in
  locales folder."*
- the file name without extension must be a BCP47 culture name, and it must not be `en` - in a regular
  folder that one belongs in `default.jsonc`. An override folder is the exception: there, `en.jsonc`
  *is* how the root is supplied.
- a key that does not declare an override must already exist in `default.jsonc`.

A culture that is valid but outside the `ActiveCultureSet` is the one case that only warns: the file
is skipped, and the active culture names are logged once so the omission is diagnosable.

A container with no such folder at all is not an error: `LoadTranslations` returns **true** with a null
`translations`.

The trap is that a *broken* root returns true as well. When `CreateRoot` fails - a missing
`default.jsonc`, or one that does not parse - the method logs the error and then falls through to the
same `translations = null; return true;`. Only a bad **non-root** file returns false. So the return
value does not separate "no locales" from "unreadable locales", and a consumer that merges on a null
definition set treats the second as the first. Read the monitor, not the boolean.

## An override is a prefix on the key.

```jsonc
{
  "Some.Key":     "defines it",
  "O:Some.Key":   "overrides it",
  "?O:Some.Key":  "overrides it if it exists, otherwise silently dropped",
  "!O:Some.Key":  "defines or overrides, indifferently"
}
```

Those are four separate illustrations, not one copyable file: the prefix is stripped before the key is
inserted, so a file carrying both `"Some.Key"` and `"O:Some.Key"` fails on a duplicate key.

They map to `ResourceOverrideKind.None`, `Regular`, `Optional` and `Always`, and what happens when
the expectation is not met depends on which of the two merge operations is running - see the next
section, because the answers are not the same. Passing
`isOverrideFolder: true` changes the default: every unprefixed key in the folder becomes `Regular`,
which is how an application-level folder is declared to be pure override. Such a folder also does not
need a `default.jsonc` - it may use `en.jsonc`, and with neither it simply starts from an empty root.

Property names are constrained beyond that: never empty, and no leading dot, trailing dot or `..`
inside. The dots are structural, so an empty segment has no meaning.

## Two operations produce a final set, and they are not interchangeable.

[`TranslationDefinitionSet`](TranslationDefinitionSet.cs) is what was read. `FinalTranslationSet` is
what a merge produces, and there are exactly two ways to get one:

`ToInitialFinalSet` turns a definition set into a standalone final set, for a component that depends
on nothing. A `Regular` override here is a hard error - there is nothing below to override - and the
method returns null. `Optional` is silently dropped; `None` and `Always` survive.

`Combine( monitor, baseFinalSet )` applies definitions on top of an existing final set, and it is far
more forgiving with the same input:

| Key in the definitions | Already in the base set | Not in it |
|------------------------|-------------------------|-----------|
| unprefixed (`None`) | **error**, `Combine` returns null - the message tells you to write `O:` | defined |
| `O:` (`Regular`) | overridden | **warning**, dropped |
| `?O:` (`Optional`) | overridden | silently dropped |
| `!O:` (`Always`) | overridden | defined |

The one error there is the interesting cell: redefining a key without saying so is refused, and the
message spells out both texts and the fix. Overriding with a value identical to the existing one is a
warning too - *"Useless override"* - unless the override is what resolves an ambiguity, which it also
does: an override clears the ambiguities on the key it replaces.

`FinalTranslationSet.Aggregate` is the third operation, and the merge of two independent branches:
same key, different text, and the result is ambiguous even when neither input was. It is the **only**
place that creates an ambiguity - `AddAmbiguity` has exactly one call site, inside it. So the doc
comment on `Combine` claiming it *"can create ambiguities as well as removing ones"* is half right in
this code: it removes them, and nothing in it adds one.

The same comment says `Combine` is **not idempotent** - *"When applied twice on a set, false
ambiguities will be created"*. Nothing enforces that, and nothing in the current body would produce
those ambiguities either. Treat it as a warning about a call pattern rather than a described
behaviour.

## Ambiguity is carried in the value, not raised.

```csharp
public FinalTranslationValue AddAmbiguity( FinalTranslationValue ambiguous )
```

[`FinalTranslationValue`](FinalTranslationValue.cs) is a `Text`, an `Origin` resource, and an optional
`Ambiguities` list of the other values claiming the same key. Equality deliberately ignores
`Ambiguities` - two values are equal when text and origin match - and `AddAmbiguity` returns the same
value when the incoming one carries the same text or is already listed. Only genuine disagreement
accumulates.

`IFinalTranslationSet.IsAmbiguous` reports the set's own values; on the root `FinalTranslationSet` it
also reports any ambiguous sub-set, so a single root test is enough to know whether the whole tree is
clean - with a caveat. `IsAmbiguous` is a stored flag, propagated by an `|=` that both merge operations
skip when the sub-set came through unchanged. A sub-set that was already ambiguous on some key and
whose definitions change nothing does not raise the root flag. A `Throw.DebugAssert` guards the
invariant, so the hole shows up in a Debug build and passes in Release.
`FinalTranslationSet.Ambiguities` enumerates the offending entries for a message.

Deciding what to do about an ambiguity is the caller's: nothing here refuses to build an ambiguous
set. "Nothing throws" would be too strong for the package as a whole, though - the argument guards
(`Throw.CheckArgument` on `Aggregate`, `Combine` and the set lookups) throw on misuse, and a malformed
`.jsonc` raises inside the reader, where `LoadTranslations` catches it and turns it into a logged
failure.

## Active cultures are a compact tree, and the index is the API.

[`ActiveCultureSet`](ActiveCultureSet.cs) and [`ActiveCulture`](ActiveCulture.cs) are the two types of
this assembly that live in `CK.Core` rather than `CK.EmbeddedResources` - a consumer needs both
`using` directives. The set is built from a plain culture list and adds every missing parent, so the
tree is closed. `Root` is always present even for an empty list.
`ActiveCulture.Index` is a stable position in `AllActiveCultures`, and it exists to be used: the
intended way to attach anything per-culture is an array indexed by it, which is exactly how the
translation sub-sets are stored.

The order of `AllActiveCultures` is *not* a traversal order - not depth-first, not breadth-first -
and the doc says so explicitly. Read the tree through `Parent`, `Children` and `Path` when order
matters.

Serialization is deliberately absent. An `ActiveCultureSet` cannot round-trip on its own, because
restoring a culture is ambiguous between `EnsureNormalizedCultureInfo` and the safer lookup in
`ExtendedCultureInfo.All`, and only the first preserves `Index`. So the set is stored as the culture
names of `AllActiveCultures` in array order - `ToString()` produces exactly that - and restored by
passing the ensured cultures back to the constructor. `FinalTranslationSet` follows the same
principle with explicit `Serialize` / `Deserialize( SerializedData )` methods rather than an
attribute.

## Requires.

- `CK.Globalization` for `NormalizedCultureInfo` and `ExtendedCultureInfo`.
- `CK.EmbeddedResources` for `IResourceContainer`, `ResourceLocator` and `ResourceOverrideKind`.
