using CK.Core;

namespace CK.ResourceSpace.Tests.P2;

/// <summary>
/// Ships "ts-assets/logo.png" and maps it to the "logos" folder of the final asset set.
/// <see cref="P1.Package"/> does exactly the same: the two collide on 'logos/logo.png'.
/// <para>
/// Neither package declares an override: they don't know each other.
/// </para>
/// </summary>
[EmbeddedResourceType]
class Package { }
