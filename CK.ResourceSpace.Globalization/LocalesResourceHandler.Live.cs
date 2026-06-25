using CK.BinarySerialization;
using CK.EmbeddedResources;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace CK.Core;

public partial class LocalesResourceHandler : ILiveResourceSpaceHandler
{
    /// <summary>
    /// Live update is currently supported only when installing on the file system:
    /// the <see cref="FileSystemInstaller.TargetPath"/> is serialized in the live state
    /// and a basic <see cref="FileSystemInstaller"/> is deserialized to be the installer
    /// on the live side.
    /// </summary>
    public bool DisableLiveUpdate => Installer is not FileSystemInstaller;

    /// <summary>
    /// Writes the live state in the primary live state file and into auxiliary files in
    /// the <see cref="ResCoreData.LiveStatePath"/>.
    /// <para>
    /// This can be overridden (and a static <see cref="ReadLiveState"/> must always be implemented).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="s">The serializer for the primary <see cref="ResSpace.LiveStateFileName"/>.</param>
    /// <param name="spaceData">The resource space that has been serialized.</param>
    /// <returns>True on success, false on error. Errors must be logged.</returns>
    public virtual bool WriteLiveState( IActivityMonitor monitor, IBinarySerializer s, ResCoreData spaceData )
    {
        Throw.DebugAssert( "Otherwise LiveState would have been disabled.", Installer is FileSystemInstaller );
        s.Writer.Write( ((FileSystemInstaller)Installer).TargetPath );
        s.Writer.Write( string.Join( ',', _cache.ActiveCultures.AllActiveCultures.Select( c => c.Culture.Name ) ) );
        s.Writer.Write( RootFolderName );
        s.Writer.WriteNonNegativeSmallInt32( (int)_installOption );
        var stableToSave = _cache.GetStableData();
        s.Writer.WriteNonNegativeSmallInt32( stableToSave.Length );
        if( stableToSave.Length > 0 )
        {
            var folderData = spaceData.LiveStatePath + "Folder";
            Directory.CreateDirectory( folderData );
            using( var stableStream = File.Create( folderData + Path.DirectorySeparatorChar + RootFolderName + ".dat" ) )
            using( var w = new CKBinaryWriter( stableStream ) )
            {
                foreach( var set in stableToSave )
                {
                    WriteSerializedData( w, spaceData, set.Serialize() );
                }
            }
        }
        return true;

        static void WriteSerializedData( ICKBinaryWriter w, ResCoreData spaceData, FinalTranslationSet.SerializedData d )
        {
            WriteTranslations( w, spaceData, d.Translations, d.IsAmbiguous );
            w.WriteNonNegativeSmallInt32( d.SubSets.Length );
            foreach( var (t, isAmbiguous) in d.SubSets )
            {
                WriteTranslations( w, spaceData, t, isAmbiguous );
            }

            static void WriteTranslations( ICKBinaryWriter w,
                                           ResCoreData spaceData,
                                           IReadOnlyDictionary<string, FinalTranslationValue>? translations,
                                           bool isAmbiguous )
            {
                if( translations == null )
                {
                    w.WriteSmallInt32( -1 );
                }
                else
                {
                    w.WriteSmallInt32( translations.Count );
                    foreach( var (k, v) in translations )
                    {
                        w.WriteSharedString( k );
                        WriteFinalTranslationValue( w, spaceData, v, true );
                    }
                    w.Write( isAmbiguous );
                }

                static void WriteFinalTranslationValue( ICKBinaryWriter w, ResCoreData spaceData, FinalTranslationValue v, bool withAmbiguities )
                {
                    w.WriteNonNegativeSmallInt32( spaceData.GetPackageResources( v.Origin ).Index );
                    w.Write( v.Origin.FullResourceName );
                    w.Write( v.Text );
                    if( withAmbiguities )
                    {
                        var ambiguities = v.Ambiguities;
                        if( ambiguities != null )
                        {
                            foreach( var a in ambiguities )
                            {
                                w.Write( true );
                                WriteFinalTranslationValue( w, spaceData, v, false );
                            }
                        }
                        w.Write( false );
                    }
                }
            }
        }

    }

    /// <summary>
    /// Restores a <see cref="ILiveUpdater"/>.
    /// <para>
    /// The live updater implementation is sealed and private. If the updater behavior must be overridden,
    /// please use a <see cref="ILiveUpdater"/> wrapper that delegates to this updater.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="spaceData">The deserialized resource space data.</param>
    /// <param name="d">The deserializer for the primary <see cref="ResSpace.LiveStateFileName"/>.</param>
    /// <returns>The live updater on success, null on error. Errors are logged.</returns>
    public static ILiveUpdater? ReadLiveState( IActivityMonitor monitor, ResCoreData spaceData, IBinaryDeserializer d )
    {
        var targetInstallPath = d.Reader.ReadString();
        var cultures = d.Reader.ReadString().Split( ',' ).Select( NormalizedCultureInfo.EnsureNormalizedCultureInfo );
        var activeCultures = new ActiveCultureSet( cultures );
        var rootFolderName = d.Reader.ReadString();
        var options = (InstallOption)d.Reader.ReadNonNegativeSmallInt32();
        var stableCount = d.Reader.ReadNonNegativeSmallInt32();
        // No installer on the handler.
        // We reuse its logic only, not its Install capability.
        var handler = new LocalesResourceHandler( null, spaceData.SpaceDataCache, rootFolderName, activeCultures, options );

        // Our live installer knowns the "/locales/" sub folder.
        var installer = new FileSystemInstaller( targetInstallPath + rootFolderName+ Path.DirectorySeparatorChar );
        return new LiveUpdater( handler, installer, spaceData, stableCount );
    }

    sealed class LiveUpdater : ILiveUpdater
    {
        readonly LocalesResourceHandler _handler;
        readonly FileSystemInstaller _installer;
        readonly ResCoreData _data;
        readonly string[] _activeNames;
        int _stableCount;
        bool _hasChanged;

        public LiveUpdater( LocalesResourceHandler handler, FileSystemInstaller installer, ResCoreData data, int stableCount )
        {
            _handler = handler;
            _installer = installer;
            _data = data;
            _stableCount = stableCount;
            _activeNames = handler._cache.ActiveCultures.AllActiveCultures
                                                        .Select( c => Path.DirectorySeparatorChar + c.Culture.Name )
                                                        .ToArray();
        }

        bool IsActiveCultureFile( ReadOnlySpan<char> n )
        {
            foreach( var end in _activeNames )
            {
                if( n.EndsWith( end ) ) return true;
            }
            return false;
        }

        bool ILiveUpdater.OnChange( IActivityMonitor monitor, PathChangedEvent changed )
        {
            // This first filter allows us to exit quickly.
            if( !IsFileInRootFolder( _handler.RootFolderName, changed.SubPath, out ReadOnlySpan<char> localFile ) )
            {
                return false;
            }
            // If localFile is empty, something happened to the whole IResPackageResources.LocalPath
            // or to our RootFolderName.
            // Otherwise, it must be a ".jsonc" file and its name must be one of the active culture names.
            // Invalidate all the cache.
            if( localFile.Length == 0 || (localFile.EndsWith( ".jsonc" ) && IsActiveCultureFile( localFile[..^6] ) ) )
            {
                _hasChanged = true;
                _handler._cache.InvalidateCache( monitor, changed.Resources );
            }
            else
            {
                monitor.Trace( $"'/{_handler.RootFolderName}': ignored changed file." );
            }
            return true;
        }


        void ILiveUpdater.ApplyChanges( IActivityMonitor monitor )
        {
            if( !_hasChanged ) return;
            if( _stableCount > 0 )
            {
                var loadPath = "Folder" + Path.DirectorySeparatorChar + _handler.RootFolderName + ".dat";
                using( monitor.OpenInfo( $"Loading {_stableCount} cached final translation sets from '.ck-watch/{loadPath}'.") )
                {
                    LoadStableData( monitor, loadPath );
                    _stableCount = 0;
                }
            }
            _hasChanged = false;
            var f = _handler.GetUnambiguousFinalTranslations( monitor, _data );
            if( f != null ) _handler.WriteFinal( monitor, f, _installer );
        }

        void LoadStableData( IActivityMonitor monitor, string loadPath )
        {
            try
            {
                string fileName = _data.LiveStatePath + loadPath;
                using( var data = File.OpenRead( fileName ) )
                using( var r = new CKBinaryReader( data ) )
                {
                    var stableData = ImmutableArray.CreateBuilder<FinalTranslationSet>( _stableCount );
                    for(int i = 0; i < _stableCount; ++i )
                    {
                        stableData.Add( FinalTranslationSet.Deserialize( ReadSerializedData( r ) ) );
                    }
                    _handler._cache.SetStableData( stableData.MoveToImmutable() );
                }
            }
            catch( Exception ex )
            {
                monitor.Error( ex );
            }

            FinalTranslationSet.SerializedData ReadSerializedData( CKBinaryReader r )
            {
                var (translations,isAmbiguous) = ReadTranslations( r, _data );
                Throw.CheckData( "The default translations necessarily exists.", translations != null );
                int nbSubset = r.ReadNonNegativeSmallInt32();
                var subsets = nbSubset > 0
                                ? new (IReadOnlyDictionary<string, FinalTranslationValue>?, bool)[nbSubset]
                                : Array.Empty<(IReadOnlyDictionary<string, FinalTranslationValue>?, bool)>();
                for( int i = 0; i < nbSubset; ++i )
                {
                    subsets[i] = ReadTranslations( r, _data );
                }
                return new FinalTranslationSet.SerializedData( _handler._cache.ActiveCultures, translations, subsets, isAmbiguous );

                static (IReadOnlyDictionary<string, FinalTranslationValue>?,bool) ReadTranslations( ICKBinaryReader r, ResCoreData spaceData )
                {
                    int count = r.ReadSmallInt32();
                    if( count == -1 )
                    {
                        return (null,false);
                    }
                    if( count == 0 )
                    {
                        return (ImmutableDictionary<string, FinalTranslationValue>.Empty, r.ReadBoolean());
                    }
                    var result = new Dictionary<string, FinalTranslationValue>( count );
                    for( int i = 0; i < count; ++i )
                    {
                        result.Add( r.ReadSharedString()!, ReadFinalTranslationValue( r, spaceData, true ) );
                    }
                    return (result, r.ReadBoolean());

                    static FinalTranslationValue ReadFinalTranslationValue( ICKBinaryReader r, ResCoreData spaceData, bool withAmbiguities )
                    {
                        int idxPackageResources = r.ReadNonNegativeSmallInt32();
                        var resourcesContainer = spaceData.AllPackageResources[idxPackageResources].Resources;
                        var origin = new ResourceLocator( resourcesContainer, r.ReadString() );
                        var text = r.ReadString();
                        List<FinalTranslationValue>? ambiguities = null;
                        if( withAmbiguities )
                        {
                            while( r.ReadBoolean() )
                            {
                                ambiguities ??= new List<FinalTranslationValue>();
                                ambiguities.Add( ReadFinalTranslationValue( r, spaceData, false ) );
                            }
                        }
                        return new FinalTranslationValue( text, origin, ambiguities );
                    }
                }

            }
        }
    }

}
