using CK.Core;

namespace CK.ResourceSpace.Tests.P1;

/// <summary>
/// Ships "ts-assets/logo.png" and maps it to the "logos" folder of the final asset set.
/// <see cref="P2.Package"/> does exactly the same: the two collide on 'logos/logo.png'.
/// </summary>
[EmbeddedResourceType]
class Package { }
