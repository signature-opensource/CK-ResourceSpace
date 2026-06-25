using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace CK.Core;

/// <summary>
/// Captures a array of package indexes in a equatable value.
/// </summary>
readonly struct AggregateKey : IEquatable<AggregateKey>
{
    readonly int[] _indexes;

    public AggregateKey( ReadOnlySpan<int> packageIndexes )
    {
        Throw.DebugAssert( packageIndexes.Length >= 2 );
        int[] indexes = new int[1 + packageIndexes.Length];
        var indexesPart = indexes.AsSpan( 1 );
        packageIndexes.CopyTo( indexesPart );
        indexesPart.Sort();
        HashCode c = new HashCode();
        c.AddBytes( MemoryMarshal.AsBytes( indexesPart ) );
        indexes[0] = c.ToHashCode();
        _indexes = indexes;
    }

    public ReadOnlySpan<int> PackageIndexes => _indexes.AsSpan( 1 );

    public bool Equals( AggregateKey other ) => _indexes.AsSpan( 1 ).SequenceEqual( other._indexes.AsSpan( 1 ) );

    public override int GetHashCode() => _indexes[0];

    public override bool Equals( object? obj ) => obj is AggregateKey key && Equals( key );

    public static bool operator ==( AggregateKey a1, AggregateKey a2 ) => a1.Equals( a2 );

    public static bool operator !=( AggregateKey a1, AggregateKey a2 ) => !a1.Equals( a2 );
}
