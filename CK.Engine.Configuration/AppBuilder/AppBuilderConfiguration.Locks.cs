using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Setup;

public sealed partial class AppBuilderConfiguration
{
    /// <summary>
    /// Gets the entries of the <c>"$LOCK"</c> of the Builder aspects (in the "Aspects" at the root): they lock the values
    /// that the aspects project in the hosts (see <see cref="AppConfiguration.ProjectValue(string, string, string)"/>).
    /// </summary>
    public IReadOnlyList<ConfigurationLockEntry> BuilderLocks => _builderLocks;

    /// <summary>
    /// Applies the restrictions: the <c>"$LOCK"</c> of the "ckonf.json" and the ones declared by the packages
    /// (<see cref="ConfigurationLockAttribute"/>). Called once the contributors have contributed (they declared the
    /// anchors, the sections read at start and the compile keys), before <see cref="WriteProjections(IActivityMonitor)"/>.
    /// The values restricted to <see cref="ConfigurationLevel.Compile"/> are burnt: they go to the
    /// <see cref="BinPathConfiguration.CompiledValues"/> of their App, not to its host's "appsettings.json".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="packageLocks">The restrictions declared by the configurable types of each App (its type set).</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public bool ApplyLocks( IActivityMonitor monitor, Func<AppConfiguration, IEnumerable<PackageConfigurationLock>>? packageLocks = null )
    {
        bool success = true;
        foreach( var l in _builderLocks )
        {
            success &= CheckEntry( monitor, l, out _, out _ );
        }
        if( !success ) return false;
        var used = new HashSet<ConfigurationLockEntry>();
        foreach( var a in _apps )
        {
            success &= ApplyLocks( monitor, a, _builderLocks, packageLocks?.Invoke( a ) ?? [], used );
        }
        foreach( var l in _builderLocks.Where( l => !used.Contains( l ) ) )
        {
            monitor.Warn( $"{l} locks no value that the Builder aspects project in the hosts." );
        }
        return success;
    }

    /// <summary>
    /// Applies the restrictions of one App: see <see cref="ApplyLocks(IActivityMonitor, Func{AppConfiguration, IEnumerable{PackageConfigurationLock}}?)"/>.
    /// This is public for the tests of the contributors.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="app">The App.</param>
    /// <param name="builderLocks">The entries of the "$LOCK" of the Builder aspects.</param>
    /// <param name="packageLocks">The restrictions declared by the configurable types of the App.</param>
    /// <param name="usedBuilderLocks">Collects the entries of <paramref name="builderLocks"/> that locked a value.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public static bool ApplyLocks( IActivityMonitor monitor,
                                   AppConfiguration app,
                                   IReadOnlyList<ConfigurationLockEntry> builderLocks,
                                   IEnumerable<PackageConfigurationLock> packageLocks,
                                   ISet<ConfigurationLockEntry>? usedBuilderLocks = null )
    {
        var r = new LockResolver( monitor, app );
        foreach( var l in app.Locks ) r.Apply( l );
        foreach( var p in packageLocks ) r.Apply( p );
        if( r.Success ) r.Burn( builderLocks, usedBuilderLocks );
        return r.Success;
    }

    static bool CheckEntry( IActivityMonitor monitor, ConfigurationLockEntry l, out ConfigurationLevel level, out ConfigurationPathPattern? pattern )
    {
        pattern = null;
        if( !l.TryGetLevel( out level ) )
        {
            monitor.Error( $"Invalid level in {l}: Compile, Init or Run is expected." );
            return false;
        }
        if( !ConfigurationPathPattern.TryParse( l.Pattern, out pattern, out var error ) )
        {
            monitor.Error( $"Invalid \"{l.SourcePath}\": {error}." );
            return false;
        }
        return true;
    }

    sealed class LockResolver
    {
        readonly IActivityMonitor _monitor;
        readonly AppConfiguration _app;
        readonly ImmutableConfigurationSection _root;
        readonly Dictionary<string, ConfigurationAnchor> _anchors;
        readonly Dictionary<string, List<ConfigurationAnchor>> _byFallback;
        // The locked values, by their path relative to the App.
        readonly Dictionary<string, Locked> _locked;
        // The patterns of the host's configuration restricted to Compile.
        readonly HashSet<string> _hostLocks;
        bool _success;

        sealed class Locked
        {
            public ConfigurationLevel Level;
            public string? Value;
            public bool IsRequired;
            public bool IsInit;
        }

        public LockResolver( IActivityMonitor monitor, AppConfiguration app )
        {
            _monitor = monitor;
            _app = app;
            _root = app.Configuration;
            _anchors = app.Anchors.ToDictionary( a => a.Path, StringComparer.OrdinalIgnoreCase );
            _byFallback = new Dictionary<string, List<ConfigurationAnchor>>( StringComparer.OrdinalIgnoreCase );
            foreach( var a in app.Anchors )
            {
                foreach( var f in a.Fallbacks )
                {
                    if( !_byFallback.TryGetValue( f, out var list ) ) _byFallback.Add( f, list = new List<ConfigurationAnchor>() );
                    list.Add( a );
                }
            }
            _locked = new Dictionary<string, Locked>( StringComparer.OrdinalIgnoreCase );
            _hostLocks = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
            _success = true;
        }

        public bool Success => _success;

        public void Apply( ConfigurationLockEntry l )
        {
            if( !CheckEntry( _monitor, l, out var level, out var pattern ) )
            {
                _success = false;
                return;
            }
            var objectPath = Relative( l.ObjectPath );
            if( objectPath == null )
            {
                _monitor.Error( $"{l} is not in {_app}." );
                _success = false;
                return;
            }
            // In a party (an anchor) or in the "Defaults" of parties (their fallback): the values of the parties.
            if( _anchors.TryGetValue( objectPath, out var anchor ) )
            {
                LockAnchor( anchor, pattern!, level, l.ToString(), null );
            }
            else if( _byFallback.TryGetValue( objectPath, out var anchors ) )
            {
                foreach( var a in anchors ) LockAnchor( a, pattern!, level, l.ToString(), null );
            }
            else
            {
                LockPlain( objectPath, pattern!, level, l.ToString() );
            }
        }

        public void Apply( PackageConfigurationLock p )
        {
            var source = $"[ConfigurationLock( \"{p.Attribute.Path}\" )] of '{p.TypeName}'";
            var parts = p.Attribute.Path?.Split( ':', 2 );
            if( parts == null || parts.Length != 2 || parts[0].Length == 0 )
            {
                _monitor.Error( $"Invalid {source}: its path is the kind of the parties, then the path in the section of the type (\"OpenedRemoteTemplate:AutoTrustKey\")." );
                _success = false;
                return;
            }
            var level = p.Attribute.Level;
            if( !Enum.IsDefined( level ) )
            {
                _monitor.Error( $"Invalid level in {source}." );
                _success = false;
                return;
            }
            // One restriction per section of the type (an option of a base owner is in each concrete owner).
            foreach( var section in p.Sections )
            {
                if( !ConfigurationPathPattern.TryParse( $"{section}:{parts[1]}", out var pattern, out var error ) )
                {
                    _monitor.Error( $"Invalid {source}: {error}." );
                    _success = false;
                    return;
                }
                if( p.Attribute.Value != null && (level != ConfigurationLevel.Compile || !pattern.IsLiteral) )
                {
                    _monitor.Error( $"Invalid {source}: a required value is a Compile restriction of one value (no wildcard)." );
                    _success = false;
                    return;
                }
                foreach( var a in _anchors.Values.Where( a => a.Kinds.Contains( parts[0] ) ) )
                {
                    LockAnchor( a, pattern, level, source, p.Attribute.Value );
                }
            }
        }

        // The values of a party: its own ones, otherwise the ones of its first fallback that has them.
        void LockAnchor( ConfigurationAnchor anchor, ConfigurationPathPattern pattern, ConfigurationLevel level, string source, string? required )
        {
            var candidates = new List<string>();
            var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
            foreach( var p in anchor.Fallbacks.Prepend( anchor.Path ) )
            {
                var s = Section( p );
                if( s == null ) continue;
                foreach( var (path, _) in Leaves( s, "" ) )
                {
                    if( pattern.Covers( path ) && seen.Add( path ) ) candidates.Add( path );
                }
            }
            if( candidates.Count == 0 )
            {
                if( !pattern.IsLiteral )
                {
                    _monitor.Warn( $"{source} matches no value of '{_root.Path}:{anchor.Path}'." );
                    return;
                }
                candidates.Add( pattern.Text );
            }
            bool isInit = false;
            foreach( var c in candidates )
            {
                var full = Combine( anchor.Path, c );
                var own = Value( full );
                var resolved = own;
                if( resolved == null )
                {
                    foreach( var f in anchor.Fallbacks )
                    {
                        if( Value( Combine( f, c ) ) is string v )
                        {
                            resolved = v;
                            break;
                        }
                    }
                }
                if( required != null )
                {
                    if( own != null && !own.Equals( required, StringComparison.OrdinalIgnoreCase ) )
                    {
                        _monitor.Error( $"\"{_root.Path}:{full}\" is '{own}': {source} requires '{required}' here." );
                        _success = false;
                        continue;
                    }
                    if( own == null && resolved != null && !resolved.Equals( required, StringComparison.OrdinalIgnoreCase ) )
                    {
                        _monitor.Info( $"\"{_root.Path}:{full}\" would be '{resolved}' (a \"Defaults\" above it): {source} requires '{required}' here." );
                    }
                    resolved = required;
                }
                isInit |= Record( full, level, resolved, required != null, source );
            }
            if( isInit && level == ConfigurationLevel.Compile ) _hostLocks.Add( _app.ToHostPath( Combine( anchor.Path, pattern.Text ) ) );
        }

        void LockPlain( string objectPath, ConfigurationPathPattern pattern, ConfigurationLevel level, string source )
        {
            var candidates = new List<string>();
            var s = Section( objectPath );
            if( s != null )
            {
                foreach( var (path, _) in Leaves( s, "" ) )
                {
                    if( pattern.Covers( path ) ) candidates.Add( path );
                }
            }
            if( candidates.Count == 0 )
            {
                if( !pattern.IsLiteral )
                {
                    _monitor.Warn( $"{source} matches no value." );
                    return;
                }
                candidates.Add( pattern.Text );
            }
            bool isInit = false;
            foreach( var c in candidates )
            {
                var full = Combine( objectPath, c );
                isInit |= Record( full, level, Value( full ), false, source );
            }
            if( isInit && level == ConfigurationLevel.Compile ) _hostLocks.Add( _app.ToHostPath( Combine( objectPath, pattern.Text ) ) );
        }

        // Returns true when the value is an Init value of the host (not a compile key nor an aspect's value).
        bool Record( string path, ConfigurationLevel level, string? value, bool isRequired, string source )
        {
            ConfigurationLevel capability;
            bool isInit = false;
            if( IsCompileKey( path ) ) capability = ConfigurationLevel.Compile;
            else if( IsUnder( path, "Aspects" ) ) capability = ConfigurationLevel.Init;
            else if( _app.InitSections.Any( s => IsUnder( path, s ) ) )
            {
                capability = ConfigurationLevel.Init;
                isInit = true;
            }
            else
            {
                _monitor.Error( $"{source} locks \"{_root.Path}:{path}\" that no code declares (the sections read at start are declared by the packages): it can't be locked." );
                _success = false;
                return false;
            }
            if( level > capability )
            {
                _monitor.Error( $"{source} restricts \"{_root.Path}:{path}\" to {level}: its code supports {capability} only (a restriction lowers a level, never raises it)." );
                _success = false;
                return false;
            }
            if( _locked.TryGetValue( path, out var l ) )
            {
                if( level < l.Level ) l.Level = level;
                if( isRequired )
                {
                    l.Value = value;
                    l.IsRequired = true;
                }
            }
            else
            {
                _locked.Add( path, new Locked { Level = level, Value = value, IsRequired = isRequired, IsInit = isInit } );
            }
            return isInit;
        }

        public void Burn( IReadOnlyList<ConfigurationLockEntry> builderLocks, ISet<ConfigurationLockEntry>? usedBuilderLocks )
        {
            var b = _app.BinPathConfiguration;
            foreach( var (path, l) in _locked.OrderBy( kv => kv.Key, StringComparer.OrdinalIgnoreCase ) )
            {
                if( l.Level != ConfigurationLevel.Compile || !l.IsInit ) continue;
                b.CompiledValues[_app.ToHostPath( path )] = l.Value;
                _app.AddBurntKey( path );
            }
            b.CompiledLocks.UnionWith( _hostLocks );
            // The values projected by the aspects from a locked value.
            var appPrefix = _root.Path + ":";
            foreach( var (hostPath, origin) in _app.ProjectedValueOrigins )
            {
                bool burnt = false;
                if( origin.StartsWith( appPrefix, StringComparison.OrdinalIgnoreCase )
                    && _locked.TryGetValue( origin[appPrefix.Length..], out var l )
                    && l.Level == ConfigurationLevel.Compile )
                {
                    burnt = true;
                }
                foreach( var e in builderLocks )
                {
                    if( !origin.StartsWith( e.ObjectPath + ":", StringComparison.OrdinalIgnoreCase )
                        || !e.TryGetLevel( out var level )
                        || level != ConfigurationLevel.Compile
                        || !ConfigurationPathPattern.TryParse( e.Pattern, out var p, out _ )
                        || !p.Covers( origin[(e.ObjectPath.Length + 1)..] ) )
                    {
                        continue;
                    }
                    burnt = true;
                    usedBuilderLocks?.Add( e );
                }
                if( burnt )
                {
                    b.CompiledValues[hostPath] = _app.ProjectedValues[hostPath];
                    b.CompiledLocks.Add( hostPath );
                }
            }
        }

        bool IsCompileKey( string path ) => _app.CompileKeys.Any( k => IsUnder( path, k ) );

        static bool IsUnder( string path, string prefix ) => path.Equals( prefix, StringComparison.OrdinalIgnoreCase )
                                                              || (path.Length > prefix.Length
                                                                  && path[prefix.Length] == ':'
                                                                  && path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ));

        string? Relative( string configurationPath )
        {
            if( configurationPath.Equals( _root.Path, StringComparison.OrdinalIgnoreCase ) ) return "";
            return IsUnder( configurationPath, _root.Path ) ? configurationPath[(_root.Path.Length + 1)..] : null;
        }

        ImmutableConfigurationSection? Section( string path ) => path.Length == 0 ? _root : _root.TryGetSection( path );

        string? Value( string path ) => _root.TryGetSection( path )?.Value;

        static string Combine( string a, string b ) => a.Length == 0 ? b : $"{a}:{b}";

        static IEnumerable<(string Path, string Value)> Leaves( ImmutableConfigurationSection s, string prefix )
        {
            foreach( var c in s.GetChildren() )
            {
                var path = Combine( prefix, c.Key );
                if( c.Value != null ) yield return (path, c.Value);
                foreach( var l in Leaves( c, path ) ) yield return l;
            }
        }
    }
}
