using CK.Setup;

// We must skip this assembly because it depends on the CK.Abstration
// PFeature definer but is absolutely not a PFeature and none of the assemblies
// that reference it must be PFeatures.
[assembly: SkippedAssembly]
