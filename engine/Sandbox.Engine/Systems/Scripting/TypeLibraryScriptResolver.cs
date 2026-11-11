using Sandbox.Internal;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// Supplies the current context's exposed metadata. Breen owns binding and overload resolution.
/// </summary>
internal sealed partial class TypeLibraryScriptResolver( TypeLibrary library ) : Breen.IScriptTypeResolver, Breen.IEditorMetadata
{
	private IReadOnlyList<Assembly> incomingAssemblies = [];
	private Type[] incomingTypes = [];
	private HashSet<string> incomingAssemblyNames = [];

	/// <summary>
	/// Supplies real incoming user types while preparing a shared native and script hotload,
	/// before their normal catalog registration callbacks can run.
	/// </summary>
	internal IReadOnlyList<Assembly> IncomingAssemblies
	{
		get => incomingAssemblies;
		set
		{
			var types = value.SelectMany( assembly => assembly.GetTypes() )
				.Where( type => !type.IsAssignableTo( typeof( Delegate ) ) ).ToArray();
			incomingAssemblyNames = value.Select( assembly => assembly.GetName().Name ).ToHashSet();
			incomingTypes = types;
			incomingAssemblies = value;
			typeNames = null;
			indexedCatalog = null;
		}
	}

	private bool IsIncoming( Type type ) => type is not null && incomingAssemblies.Contains( type.Assembly );

	/// <summary>
	/// Allows registered component bases to expose their normal protected extension points.
	/// </summary>
	public void GetBaseMembers( Type type, List<MemberInfo> members )
	{
		var description = library.GetType( type );
		if ( (description is null && !IsIncoming( type )) || !type.IsAssignableTo( typeof( Component ) ) )
			return;

		members.AddRange( type.GetConstructors( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance )
			.Where( constructor => (constructor.IsPublic || constructor.IsFamily) && constructor.GetParameters().Length == 0 ) );

		// Public properties already arrive through GetMembers. Only add protected-only properties here,
		// otherwise live module metadata contains duplicate entries for the same inherited property.
		members.AddRange( GetExposedMembers( type ).Where( IsScriptSafe ).Where( member => member is
			MethodInfo { IsFamily: true } or FieldInfo { IsFamily: true }
			|| member is PropertyInfo property
				&& (property.GetMethod?.IsFamily == true || property.SetMethod?.IsFamily == true)
				&& property.GetMethod?.IsPublic != true && property.SetMethod?.IsPublic != true ) );
	}

	/// <summary>
	/// Supplies the same update subscriptions as the C# component generator.
	/// </summary>
	public IEnumerable<Type> GetMarkerInterfaces( Type baseType, IReadOnlySet<string> overrides )
	{
		if ( !baseType.IsAssignableTo( typeof( Component ) ) )
			yield break;

		if ( overrides.Contains( "OnUpdate" ) )
			yield return typeof( IUpdateSubscriber );

		if ( overrides.Contains( "OnFixedUpdate" ) )
			yield return typeof( IFixedUpdateSubscriber );

		if ( overrides.Contains( "OnPreRender" ) )
			yield return typeof( IPreRenderSubscriber );
	}

	/// <summary>
	/// Resolves registered CLR and C# names, including nested types relative to their registered outer type.
	/// </summary>
	public Type ResolveType( string name )
	{
		var catalog = library.GetTypes( (Type)null );
		var names = GetTypeNames( catalog );
		if ( names.Find( name ) is { } exact )
		{
			return exact;
		}

		// Aliases belong to mutable descriptions, not immutable CLR names. Read them live so metadata
		// refreshes and changes to an alias array cannot leave stale names in the index.
		var registered = catalog.Where( type => incomingAssemblyNames.Count == 0
			|| !incomingAssemblyNames.Contains( type.TargetType.Assembly.GetName().Name ) );
		if ( registered.FirstOrDefault( type => type.IsNamed( name ) ) is { } alias )
		{
			return alias.TargetType;
		}

		// Match the addon compiler's redirect only while the safe wrapper is registered.
		// User type names and live catalog aliases take precedence over these compatibility names.
		if ( name is "StringBuilder" or "System.Text.StringBuilder"
			&& names.Find( typeof( SafeStringBuilder ).FullName ) == typeof( SafeStringBuilder ) )
		{
			return typeof( SafeStringBuilder );
		}

		// Resolve the outer name first, then walk only its registered public nested types.
		// Matching a suffix alone could select a type from a different namespace or owner.
		for ( var separator = name.LastIndexOf( '.' ); separator > 0; separator = name.LastIndexOf( '.', separator - 1 ) )
		{
			var outerName = name[..separator];
			var owner = names.Find( outerName )
				?? registered.FirstOrDefault( type => type.IsNamed( outerName ) )?.TargetType;
			if ( owner is null )
			{
				continue;
			}

			foreach ( var part in name[(separator + 1)..].Split( '.' ) )
			{
				owner = names.FindNested( owner, part );
				if ( owner is null )
				{
					return null;
				}
			}

			return owner;
		}

		// Async and catch declarations need these types, but their members and constructors
		// still require catalog approval. In particular, naming Exception must not expose TargetSite.
		return name switch
		{
			"Exception" or "System.Exception" => typeof( Exception ),
			"Task" or "System.Threading.Tasks.Task" => typeof( Task ),
			"Task`1" or "System.Threading.Tasks.Task`1" => typeof( Task<> ),
			_ => null
		};
	}

	/// <summary>
	/// Exact CLR or C# names win, followed by top-level simple names, nested names and catalog aliases.
	/// </summary>
	static Type ResolveNamedType( string name, IEnumerable<Type> types )
	{
		// Scripts use C# type names. An editor alias such as "color" must not shadow Color.
		Type topLevel = null;
		Type nested = null;
		foreach ( var type in types )
		{
			if ( type.FullName == name || MatchesCSharpName( type.FullName, name ) )
			{
				return type;
			}

			if ( type.Name != name )
			{
				continue;
			}

			// Keep scanning: a later exact name wins over either simple-name fallback.
			// A nested Script.Input must not displace the ordinary top-level Input API.
			if ( type.IsNested )
			{
				nested ??= type;
			}
			else
			{
				topLevel ??= type;
			}
		}

		return topLevel ?? nested;
	}

	/// <summary>
	/// Compares the nested C# spelling without allocating a replaced full-name string for every candidate.
	/// Keep the comparison ordinal and normalize only CLR nesting separators, as the original lookup did.
	/// </summary>
	private static bool MatchesCSharpName( string fullName, string name )
	{
		if ( fullName.Length != name.Length )
		{
			return false;
		}

		for ( int i = 0; i < fullName.Length; i++ )
		{
			if ( (fullName[i] == '+' ? '.' : fullName[i]) != name[i] )
			{
				return false;
			}
		}

		return true;
	}

	/// <inheritdoc />
	public string GetDescription( MemberInfo member ) => library.GetType( member.DeclaringType )?.Members
		.FirstOrDefault( x => x.MemberInfo == member )?.Description;

	/// <inheritdoc />
	public IEnumerable<Breen.EditorType> GetTypes() => library.GetTypes()
		.OrderBy( type => type.Name, StringComparer.Ordinal )
		.Select( type => type.ToBreenEditorType() );

	// Reflected member suggestions are derived by Breen from the same catalog used for execution.
	IEnumerable<Breen.EditorMember> Breen.IEditorMetadata.GetMembers( Type type, bool isStatic ) => [];

	/// <summary>
	/// Supplies public members only; protected extension points are supplied separately for derived script classes.
	/// </summary>
	public void GetMembers( Type type, List<MemberInfo> members )
	{
		// TypeLibrary omits common object methods to keep discovery tidy. Scripts can use them
		// on every receiver, including unregistered return types and incoming hotload types.
		var objectMethods = type.GetMethods( BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy )
			.Where( method => method.Name is "ToString" or "Equals" or "GetHashCode" )
			.Where( IsScriptSafe ).ToArray();

		// Native APIs can return private implementations of public base classes and interfaces.
		// Use the nearest registered base contract without exposing implementation-only members.
		var contract = type;
		while ( !IsIncoming( contract ) && library.GetType( contract ) is null && contract.BaseType is { } baseType )
		{
			contract = baseType;
		}

		var directMembers = GetExposedMembers( contract ).Where( IsPublicMember )
			.Concat( objectMethods ).Select( MemberKey ).ToHashSet();
		foreach ( var owner in type.GetInterfaces().Prepend( contract ) )
		{
			foreach ( var member in GetExposedMembers( owner ).Where( IsPublicMember ) )
			{
				// A concrete public API wins over an interface member with the same name, as in C#.
				// Explicit interface implementations remain available when there is no direct API.
				if ( owner != contract && directMembers.Contains( MemberKey( member ) ) )
				{
					continue;
				}

				// TypeLibrary stores generic definitions; bind their approved members to the
				// actual receiver type (for example List<int>, rather than List<T>).
				var info = member;
				if ( owner.IsConstructedGenericType && info.DeclaringType.ContainsGenericParameters )
				{
					info = owner.GetMemberWithSameMetadataDefinitionAs( info );
				}

				if ( IsScriptSafe( info ) && !members.Contains( info ) )
				{
					members.Add( info );
				}
			}
		}

		foreach ( var method in objectMethods )
		{
			if ( !members.Contains( method ) )
			{
				members.Add( method );
			}
		}

		// TypeDescription.Create exposes public constructors on registered types too.
		if ( !type.IsByRefLike && (IsIncoming( type ) || library.GetType( type ) is not null) )
		{
			members.AddRange( type.GetConstructors().Where( IsScriptSafe ) );
		}
	}

	/// <summary>
	/// Reads ordinary catalog members or the incoming CLR members awaiting registration.
	/// </summary>
	private IEnumerable<MemberInfo> GetExposedMembers( Type type ) => IsIncoming( type )
		? GetIncomingMembers( type )
		: library.GetType( type )?.Members.Select( member => member.MemberInfo ) ?? [];

	/// <summary>
	/// Applies the registered catalog's visibility rules without constructing attributes or registering types.
	/// Inherited engine APIs still require approval from their existing catalog.
	/// </summary>
	private IEnumerable<MemberInfo> GetIncomingMembers( Type type )
	{
		foreach ( var member in type.GetMembers( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static ) )
		{
			if ( member is not (MethodInfo or PropertyInfo or FieldInfo) ) continue;

			if ( !IsIncoming( member.DeclaringType ) )
			{
				if ( library.GetType( member.DeclaringType )?.Members.Any( registered => registered.MemberInfo.HasSameMetadataDefinitionAs( member ) ) == true )
				{
					yield return member;
				}
				continue;
			}

			if ( member is FieldInfo field && field.IsDefined( typeof( CompilerGeneratedAttribute ), true ) && field.GetEventInfo() is null ) continue;
			if ( member is MethodInfo method )
			{
				if ( method.Name is "Finalize" or "ToString" or "Equals" or "GetHashCode" ) continue;
				if ( method.Name == "GetType" && method.DeclaringType == typeof( object ) ) continue;
				if ( method.IsSpecialName && MethodDescription.ClassifyOperator( method ) == OperatorKind.None ) continue;
			}

			yield return member;
		}
	}

	private static bool IsPublicMember( MemberInfo member ) => member switch
	{
		MethodInfo method => method.IsPublic,
		FieldInfo field => field.IsPublic,
		PropertyInfo property => property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true,
		_ => false
	};

	private static (string Name, bool IsStatic, MemberTypes Kind) MemberKey( MemberInfo member ) =>
		(member.Name, member switch
		{
			MethodInfo method => method.IsStatic,
			FieldInfo field => field.IsStatic,
			PropertyInfo property => (property.GetMethod ?? property.SetMethod)?.IsStatic == true,
			_ => false
		}, member.MemberType);

	// Breen can reuse argument temporaries, so nothing may hand a script a ref that outlives the call.
	static bool IsScriptSafe( MemberInfo member ) => member switch
	{
		MethodBase method => (method is not MethodInfo info || !IsRefLike( info.ReturnType ))
			&& method.GetParameters().All( p => !IsRefStruct( p.ParameterType ) ),
		PropertyInfo property => !IsRefLike( property.PropertyType )
			&& property.GetIndexParameters().All( p => !IsRefStruct( p.ParameterType ) ),
		FieldInfo field => !IsRefLike( field.FieldType ),
		_ => true
	};

	static bool IsRefLike( Type type ) => type.IsByRef || IsRefStruct( type );

	static bool IsRefStruct( Type type ) => (type.IsByRef ? type.GetElementType() : type).IsByRefLike;
}
