using System;

namespace CK.Core;

readonly struct AggregateId : IEquatable<AggregateId>, ICKSimpleBinarySerializable
{
    /// <summary>
    /// <list type="bullet">
    ///     <item>= 0: No local at all (the aggregate is only composed of stable packages).</item>
    ///     <item>&gt; 0 and &lt;= number of packages: Single (necesarily local) <see cref="ResPackage.Index"/> incremented by 1.</item>
    ///     <item>&gt; number of packages: index in the SpaceDataCache local list offset by the total number of packages incremented by 1.</item>
    /// </list>
    /// </summary>
    internal readonly int _localKeyId;

    /// <summary>
    /// <list type="bullet">
    ///     <item>= 0: No stable at all.</item>
    ///     <item>&gt; 0 and &lt;= number of packages: Single (necesarily stable) <see cref="ResPackage.Index"/> incremented by 1.</item>
    ///     <item>&gt; number of packages: index in the SpaceDataCache stable list offset by the total number of packages incremented by 1.</item>
    /// </list>
    /// </summary>
    internal readonly int _stableKeyId;

    public AggregateId( int localKeyId, int stableKeyId )
    {
        _localKeyId = localKeyId;
        _stableKeyId = stableKeyId;
    }

    public bool HasLocal => _localKeyId != 0;

    public bool HasStable => _stableKeyId != 0;

    public bool Equals( AggregateId other ) => _localKeyId == other._localKeyId && _stableKeyId == other._stableKeyId;

    public override bool Equals( object? obj ) => obj is AggregateId id && Equals( id );

    public static bool operator ==( AggregateId a1, AggregateId a2 ) => a1.Equals( a2 );

    public static bool operator !=( AggregateId a1, AggregateId a2 ) => !a1.Equals( a2 );

    public override int GetHashCode() => HashCode.Combine( _localKeyId, _stableKeyId );

    public AggregateId( ICKBinaryReader r )
    {
        _localKeyId = r.ReadInt32();
        _stableKeyId = r.ReadInt32();
    }

    public void Write( ICKBinaryWriter w )
    {
        w.Write( _localKeyId );
        w.Write( _stableKeyId );
    }
}
