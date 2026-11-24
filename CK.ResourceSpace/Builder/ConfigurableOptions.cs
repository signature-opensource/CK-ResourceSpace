using CK.TypeCache;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CK.Core;

/// <summary>
/// An option (see <see cref="IConfigurableOption{TOwner}"/>) found in a type set by <see cref="ConfigurableOptions.TryCollect"/>.
/// </summary>
/// <param name="Type">The option type.</param>
/// <param name="Owner">Its owner type (the type argument of its <see cref="IConfigurableOption{TOwner}"/>).</param>
/// <param name="Attribute">Its attribute.</param>
/// <param name="Constructor">Its single public constructor.</param>
public sealed record ConfigurableOptionModel( ICachedType Type, Type Owner, IConfigurableOptionAttribute Attribute, ConstructorInfo Constructor )
{
    /// <summary>
    /// Gets the option name (<see cref="IConfigurableTypeAttribute.Name"/>).
    /// </summary>
    public string Name => Attribute.Name;

    /// <summary>
    /// Gets the activation of the option (<see cref="IConfigurableTypeAttribute.Activation"/>).
    /// </summary>
    public TypeActivation Activation => Attribute.Activation;
}

/// <summary>
/// An option of an owner in its order, with the names of the options without which it cannot be enabled (the targets of
/// its non optional relations) and the types outside the options of its owner that it requires (its non optional
/// <c>[Requires]</c> to a configurable type of its level or of a level above: a feature of the party of its owner, an
/// aspect...). These ones don't order it (they are outside its island) but they keep their "only with it" meaning: the
/// level of the option knows whether they are enabled (see <see cref="ConfigurationOptionSelector"/>).
/// </summary>
/// <param name="Option">The option.</param>
/// <param name="Requires">The names of the options that it requires.</param>
/// <param name="RequiredTypes">The types outside the options of its owner that it requires.</param>
public sealed record OrderedConfigurableOption( ConfigurableOptionModel Option, IReadOnlyList<string> Requires, IReadOnlyList<Type> RequiredTypes );

/// <summary>
/// The engine side of the options of the configurable types (<see cref="IConfigurableOption{TOwner}"/>), shared by the
/// levels of the configuration (the aspects of the App builder, the party components): <see cref="TryCollect"/> finds the
/// options of a level in a type set, <see cref="TryOrder"/> orders the options of an owner.
/// </summary>
public static class ConfigurableOptions
{
    /// <summary>
    /// Finds the options of a level in a type set: the concrete classes that implement <see cref="IConfigurableOption{TOwner}"/>
    /// (once) whose owner type is assignable to one of the <paramref name="ownerKinds"/>. Each one must be decorated with
    /// exactly one <see cref="IConfigurableOptionAttribute"/> whose <see cref="IConfigurableOptionAttribute.OwnerKind"/>
    /// is assignable from its owner, and have one public constructor with at most one <see cref="IConfiguration"/>
    /// parameter (its configuration; what the other parameters are is the level's business).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="typeSet">The type set.</param>
    /// <param name="ownerKinds">The owner kinds of the level (the options of the other levels are ignored).</param>
    /// <param name="attributeHint">Names the attributes of the level, for the error of an option without attribute.</param>
    /// <param name="options">The options of the level.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public static bool TryCollect( IActivityMonitor monitor,
                                   IEnumerable<ICachedType> typeSet,
                                   IReadOnlyList<Type> ownerKinds,
                                   string attributeHint,
                                   out List<ConfigurableOptionModel> options )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( typeSet );
        Throw.CheckNotNullArgument( ownerKinds );
        options = new List<ConfigurableOptionModel>();
        bool success = true;
        foreach( var cached in typeSet )
        {
            var t = cached.Type;
            if( !t.IsClass || t.IsAbstract ) continue;
            var owners = GetOwners( t ).Distinct().ToList();
            if( owners.Count == 0 || !owners.Any( o => ownerKinds.Any( k => k.IsAssignableFrom( o ) ) ) ) continue;
            if( owners.Count > 1 )
            {
                monitor.Error( $"The option '{t.ToCSharpName()}' is an option of more than one owner type ('{owners.Select( o => o.ToCSharpName() ).Concatenate( "', '" )}'): an option has one owner type." );
                success = false;
                continue;
            }
            var owner = owners[0];
            var attributes = t.GetCustomAttributes( inherit: false ).OfType<IConfigurableOptionAttribute>().ToList();
            if( attributes.Count != 1 )
            {
                monitor.Error( $"The option '{t.ToCSharpName()}' must be decorated with the attribute of its kind of owner: {attributeHint}." );
                success = false;
                continue;
            }
            var a = attributes[0];
            if( !a.OwnerKind.IsAssignableFrom( owner ) )
            {
                monitor.Error( $"The option '{t.ToCSharpName()}' is an option of '{owner.ToCSharpName()}' that is not a '{a.OwnerKind.ToCSharpName()}': its attribute [{a.GetType().Name.Replace( "Attribute", "", StringComparison.Ordinal )}] is the one of another kind of owner." );
                success = false;
                continue;
            }
            var ctors = t.GetConstructors();
            if( ctors.Length != 1 )
            {
                monitor.Error( $"The option '{t.ToCSharpName()}' must have exactly one public constructor (its IConfiguration parameter is its configuration)." );
                success = false;
                continue;
            }
            var parameters = ctors[0].GetParameters();
            if( parameters.Count( p => p.ParameterType == typeof( IConfiguration ) ) > 1 )
            {
                monitor.Error( $"The constructor of the option '{t.ToCSharpName()}' has more than one IConfiguration parameter: an option has one configuration." );
                success = false;
                continue;
            }
            var section = parameters.FirstOrDefault( p => p.ParameterType != typeof( IConfiguration ) && typeof( IConfiguration ).IsAssignableFrom( p.ParameterType ) );
            if( section != null )
            {
                monitor.Error( $"The parameter '{section.Name}' of the option '{t.ToCSharpName()}' is a '{section.ParameterType.ToCSharpName()}': the configuration of an option is a plain IConfiguration." );
                success = false;
                continue;
            }
            // The name and the activation are checked by the type cache (an option is a configurable type).
            if( !cached.TryGetConfigurableType( monitor, out _ ) )
            {
                success = false;
                continue;
            }
            options.Add( new ConfigurableOptionModel( cached, owner, a, ctors[0] ) );
        }
        return success;
    }

    static IEnumerable<Type> GetOwners( Type t ) => t.GetInterfaces()
                                                     .Where( i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof( IConfigurableOption<> ) )
                                                     .Select( i => i.GetGenericArguments()[0] );

    /// <summary>
    /// Orders the options of an owner with a ResourceSpace island: its options are the allowed types, the compatible types
    /// are the options of an owner type assignable from it (a relation to another option is ignored). The topology
    /// attributes between options order them; a non optional relation also means "only with it" (the requiring option
    /// can't be enabled without the required one). A non optional <c>[Requires]</c> to a type that is not an option (a
    /// configurable type of the option's level or above) is kept in <see cref="OrderedConfigurableOption.RequiredTypes"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="typeCache">The type cache.</param>
    /// <param name="revertOrderingNames">See <see cref="ResSpaceConfiguration.RevertOrderingNames"/>.</param>
    /// <param name="owner">The owner type.</param>
    /// <param name="options">The options that apply to the owner (their owner type is assignable from it).</param>
    /// <returns>The ordered options or null on error (errors are logged).</returns>
    public static List<OrderedConfigurableOption>? TryOrder( IActivityMonitor monitor,
                                                             GlobalTypeCache typeCache,
                                                             bool revertOrderingNames,
                                                             Type owner,
                                                             IReadOnlyList<ConfigurableOptionModel> options )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( typeCache );
        Throw.CheckNotNullArgument( owner );
        Throw.CheckNotNullArgument( options );
        var duplicate = options.GroupBy( o => o.Name, StringComparer.OrdinalIgnoreCase ).FirstOrDefault( g => g.Count() > 1 );
        if( duplicate != null )
        {
            monitor.Error( $"The options '{duplicate.Select( o => o.Type.CSharpName ).Concatenate( "', '" )}' of '{owner.ToCSharpName()}' have the same name '{duplicate.Key}'." );
            return null;
        }
        var byType = options.ToDictionary( o => o.Type );
        var config = new ResSpaceConfiguration( typeCache, byType.ContainsKey )
        {
            PackageResolver = new IslandResolver( owner ),
            RevertOrderingNames = revertOrderingNames
        };
        var byRawType = options.ToDictionary( o => o.Type.Type );
        var requires = options.ToDictionary( o => o, o => new List<string>() );
        var requiredTypes = options.ToDictionary( o => o, o => new List<Type>() );
        bool success = true;
        foreach( var o in options )
        {
            var d = config.RegisterPackage( monitor, o.Type, isOptional: false );
            if( d == null )
            {
                success = false;
                continue;
            }
            // [Requires<T>] on o: o requires T. [RequiredBy<T>] on o: T requires o.
            // Before the build, the references of a type's relations are their raw types.
            foreach( var r in d.Requires )
            {
                if( r.IsOptional || r.AsType is not { } t ) continue;
                if( byRawType.TryGetValue( t, out var required ) ) AddRequirement( o, required.Name );
                else if( !GetOwners( t ).Any() && !requiredTypes[o].Contains( t ) ) requiredTypes[o].Add( t );
            }
            foreach( var r in d.RequiredBy )
            {
                if( !r.IsOptional && r.AsType is { } t && byRawType.TryGetValue( t, out var requiring ) ) AddRequirement( requiring, o.Name );
            }
        }
        if( !success ) return null;
        var collector = config.Build( monitor );
        if( collector == null ) return null;
        var data = new ResSpaceDataBuilder( collector ).Build( monitor );
        if( data == null ) return null;
        return data.CoreData.Packages.Where( p => p.Type != null && byType.ContainsKey( p.Type ) )
                                     .Select( p => new OrderedConfigurableOption( byType[p.Type!], requires[byType[p.Type!]], requiredTypes[byType[p.Type!]] ) )
                                     .ToList();

        void AddRequirement( ConfigurableOptionModel requiring, string required )
        {
            var list = requires[requiring];
            if( !list.Contains( required ) ) list.Add( required );
        }
    }

    // The compatibility rule of an island: the options of an owner type assignable from the owner. A relation to another
    // option (of another owner) is ignored.
    sealed class IslandResolver : IResPackageDescriptorResolver
    {
        readonly Type _owner;

        public IslandResolver( Type owner ) => _owner = owner;

        public bool IsCompatible( ICachedType type ) => GetOwners( type.Type ).Any( o => o.IsAssignableFrom( _owner ) );

        public bool ResolveRequired( IActivityMonitor monitor, IResPackageDescriptorRegistrar registrar, ResPackageDescriptor.Ref reference )
        {
            monitor.Error( $"The option '{reference}' is required but it is not an option of '{_owner.ToCSharpName()}' in this BinPath." );
            return false;
        }
    }
}
