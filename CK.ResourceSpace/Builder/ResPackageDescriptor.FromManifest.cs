using System.Xml;
using System;

namespace CK.Core;

public sealed partial class ResPackageDescriptor
{
    internal bool InitializeFromManifest( IActivityMonitor monitor, bool? isOptional )
    {
        var descriptor = _resources.GetResource( "CKPackage.xml" );
        if( descriptor.IsValid )
        {
            try
            {
                using( var s = descriptor.GetStream() )
                using( var xmlReader = XmlReader.Create( s ) )
                {
                    return ReadCKPackage( monitor, xmlReader, isOptional );
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"While reading {descriptor}.", ex );
                return false;
            }
        }
        monitor.Info( $"No package manifest found in {_resources}." );
        _isOptional = isOptional ?? false;
        return true;
    }

    bool ReadCKPackage( IActivityMonitor monitor, XmlReader xmlReader, bool? isOptional )
    {
        throw new NotImplementedException( "CKPackage.xml resource is reserved for future use." );
    }
}
