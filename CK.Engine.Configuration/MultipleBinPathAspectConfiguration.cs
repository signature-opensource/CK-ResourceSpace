using CK.Core;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Setup;

/// <summary>
/// Non generic base class for the composite <see cref="MultipleBinPathAspectConfiguration{TSelf}"/>.
/// </summary>
public abstract class MultipleBinPathAspectConfiguration : BinPathAspectConfiguration
{
    readonly List<MultipleBinPathAspectConfiguration> _configurations;
    readonly ExposedOthers _exposedOthers;
    MultipleBinPathAspectConfiguration? _head;

    sealed class ExposedOthers : IReadOnlyCollection<MultipleBinPathAspectConfiguration>
    {
        readonly MultipleBinPathAspectConfiguration _o;

        public ExposedOthers( MultipleBinPathAspectConfiguration o ) => _o = o;

        public int Count => (_o._head ?? _o)._configurations.Count;

        public IEnumerator<MultipleBinPathAspectConfiguration> GetEnumerator()
        {
            if( _o._head == null )
            {
                return _o._configurations.GetEnumerator();
            }
            return _o._head._configurations.Where( c => c != _o ).Prepend( _o._head ).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Initializes an empty BinPath aspect configuration.
    /// </summary>
    private protected MultipleBinPathAspectConfiguration()
    {
        _configurations = new List<MultipleBinPathAspectConfiguration>();
        _exposedOthers = new ExposedOthers( this );
    }

    /// <summary>
    /// Gets the extra configurations if any.
    /// </summary>
    public IReadOnlyCollection<MultipleBinPathAspectConfiguration> OtherConfigurations => _exposedOthers;

    /// <summary>
    /// Gets all the configurations (this one and the <see cref="OtherConfigurations"/>).
    /// </summary>
    public IEnumerable<MultipleBinPathAspectConfiguration> AllConfigurations => _head != null
                                                                                  ? _head.AllConfigurations
                                                                                  : _configurations.Prepend( this );

    /// <summary>
    /// Creates an Xml element with this configuration only regardless of any <see cref="OtherConfigurations"/>.
    /// <para>
    /// <see cref="BinPathAspectConfiguration.ToXml()"/> always writes &lt;Multiple&gt; elements if <see cref="OtherConfigurations"/>
    /// is not empty.
    /// </para>
    /// </summary>
    /// <returns>The xml element.</returns>
    public abstract XElement ToOnlyThisXml();

    void BaseBind( BinPathConfiguration? o, EngineAspectConfiguration? a ) => base.Bind( o, a );

    internal override void Bind( BinPathConfiguration? o, EngineAspectConfiguration? a )
    {
        if( _head != null )
        {
            Throw.DebugAssert( _configurations.Count == 0 );
            _head.Bind( o, a );
        }
        else
        {
            base.Bind( o, a );
            foreach( var other in _configurations )
            {
                other.BaseBind( o, a );
            }
        }
    }

    internal void DoAddOtherConfiguration( MultipleBinPathAspectConfiguration other )
    {
        if( other == this ) return;
        Throw.CheckArgument( other != null && other.Owner == null );
        // The other configuration is not in a EngineConfiguration, it is detached.
        // We silently handle the move across detached configuration in this case.
        if( other._head != null )
        {
            other._head.DoRemoveOtherConfiguration( other );
            Throw.DebugAssert( other._head == null );
        }
        if( _head != null )
        {
            Throw.DebugAssert( _head._head == null );
            _head.DoAddOtherConfiguration( other );
        }
        else
        {
            DoAdd( other );
            foreach( var o in other._configurations )
            {
                DoAdd( o );
            }
            other._configurations.Clear();
        }
    }

    void DoAdd( MultipleBinPathAspectConfiguration other )
    {
        Throw.DebugAssert( other._head == null );
        _configurations.Add( other );
        // First bind...
        other.Bind( Owner, AspectConfiguration );
        // Then sets the head.
        other._head = this;
    }

    internal override void HandleOwnRemove( Dictionary<string, BinPathAspectConfiguration> aspects )
    {
        if( _head != null )
        {
            _head.DoRemoveOtherConfiguration( this );
        }
        else
        {
            if( _configurations.Count == 0 )
            {
                // If we are the last one...
                base.HandleOwnRemove( aspects );
            }
            else
            {
                var newHead = _configurations[0];
                Throw.DebugAssert( newHead._head == this && newHead._configurations.Count == 0 );
                newHead._head = null;
                newHead._configurations.AddRange( _configurations.Skip( 1 ) );
                foreach( var o in newHead._configurations )
                {
                    o._head = newHead;
                }
                _configurations.Clear();
                BaseBind( null, null );
                aspects[AspectName] = newHead;
            }
        }
    }

    private protected void DoRemoveOtherConfiguration( MultipleBinPathAspectConfiguration other )
    {
        if( other == this ) return;
        if( other._head == this )
        {
            Throw.DebugAssert( other._configurations.Count == 0 );
            _configurations.Remove( other );
            other._head = null;
            other.Bind( null, null );
        }
        else if( _head != null && other._head == _head )
        {
            _head.DoRemoveOtherConfiguration( other );
        }
    }

    private protected void DoRemoveAllOtherConfiguration()
    {
        MultipleBinPathAspectConfiguration? next;
        while( (next = OtherConfigurations.FirstOrDefault()) != null )
        {
            DoRemoveOtherConfiguration( next );
        }
    }
}
