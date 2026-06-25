using CK.Core;

namespace CK.EmbeddedResources.Globalization.Tests;

static class C
{
    public static NormalizedCultureInfo En = NormalizedCultureInfo.CodeDefault;
    public static NormalizedCultureInfo Fr = NormalizedCultureInfo.EnsureNormalizedCultureInfo( "fr" );
    public static NormalizedCultureInfo FrFR = NormalizedCultureInfo.EnsureNormalizedCultureInfo( "fr-FR" );
    public static NormalizedCultureInfo FrFRA1 = NormalizedCultureInfo.EnsureNormalizedCultureInfo( "fr-FR-A1" );
    public static NormalizedCultureInfo FrFRA2 = NormalizedCultureInfo.EnsureNormalizedCultureInfo( "fr-FR-A2" );
    public static NormalizedCultureInfo FrCA = NormalizedCultureInfo.EnsureNormalizedCultureInfo( "fr-CA" );
    public static NormalizedCultureInfo Es = NormalizedCultureInfo.EnsureNormalizedCultureInfo( "es" );
    public static NormalizedCultureInfo EsES = NormalizedCultureInfo.EnsureNormalizedCultureInfo( "es-ES" );
    public static NormalizedCultureInfo EsMX = NormalizedCultureInfo.EnsureNormalizedCultureInfo( "es-MX" );
    public static ActiveCultureSet EnFrSet = new ActiveCultureSet( [Fr] );
    public static ActiveCultureSet ActiveCultures = new ActiveCultureSet( [Fr, FrFR, FrCA, FrFRA1, FrFRA2, Es, EsES, EsMX] );
}
