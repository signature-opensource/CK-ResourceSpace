using CK.Core;


// No namespace here.
[EmbeddedResourceType]
public sealed class AppRoutedComponent : IResourcePackage
{
}

[EmbeddedResourceType]
public sealed class DemoNgModule : IResourcePackage
{
}

[EmbeddedResourceType]
public sealed class PublicPageComponent : IResourcePackage
{
}


[EmbeddedResourceType]
public class Zorro : IResourcePackage
{
}

[EmbeddedResourceType]
public sealed class SomeAuthPackage : IResourcePackage
{
}

[EmbeddedResourceType]
[Package<SomeAuthPackage>]
public sealed class LogoutConfirmComponent : IResourcePackage
{
}

[EmbeddedResourceType]
[Requires<PublicPageComponent>]
[Package<SomeAuthPackage>]
public sealed class LoginComponent : IResourcePackage
{
}

[EmbeddedResourceType]
[Requires<LogoutConfirmComponent>]
[Package<SomeAuthPackage>]
public sealed class LogoutResultComponent : IResourcePackage
{
}

[EmbeddedResourceType]
[Requires<Zorro>]
[Children<PublicTopbarComponent, PublicFooterComponent>]
public sealed class PublicSectionComponent : IResourcePackage
{
}

[EmbeddedResourceType]
[Package<PublicSectionComponent>]
public sealed class PublicFooterComponent : IResourcePackage
{
}

[EmbeddedResourceType]
[Package<PublicSectionComponent>]
public sealed class PublicTopbarComponent : IResourcePackage
{
}

[EmbeddedResourceType]
[Requires<LoginComponent>]
[Package<SomeAuthPackage>]
public sealed class PasswordLostComponent : IResourcePackage
{
}

