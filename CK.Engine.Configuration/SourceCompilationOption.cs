
namespace CK.Setup;

/// <summary>
/// Drives the code generation parsing and compilation behavior.
/// </summary>
public enum CompileOption
{
    /// <summary>
    /// No parsing nor compilation is required: Roslyn can be totally skipped.
    /// </summary>
    None,

    /// <summary>
    /// The source code is parsed by Roslyn.
    /// </summary>
    Parse,

    /// <summary>
    /// The source code is parsed and compiled: a final assembly is produced.
    /// </summary>
    Compile
}
