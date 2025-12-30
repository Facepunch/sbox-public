using Sandbox.Internal;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// Describes a type. We use this class to wrap and return <see cref="System.Type">System.Type</see>'s that are safe to interact with.
///
/// Returned by <see cref="Internal.TypeLibrary"/>.
/// </summary>
[SkipHotload]
public sealed class TypeDescription : ISourceLineProvider
{
	internal Internal.TypeLibrary library { get; set; }

	/// <summary>
	/// The type this class describes.
	/// </summary>
	public Type TargetType { get; private set; }

	/// <summary>
	/// The base type. This can return null if the type isn't in the type library!
	/// </summary>
	public TypeDescription BaseType => TargetType.BaseType is not null ? library.GetType( TargetType.BaseType ) : null;

	/// <summary>
	/// Whether the class is valid or not, i.e. whether the type still exists.
	/// </summary>
	public bool IsValid { get; private set; }

	/// <summary>
	/// Is from an assembly that was whitelist tested, so it can't have any bad stuff in it.
	/// We can feel happy to expose all members in these assemblies.
	/// </summary>
	internal bool IsDynamicAssembly { get; set; }

	/// <summary>
	/// Attributes that we, and our bases, implement
	/// </summary>
	internal List<System.Attribute> Attributes = new();

	/// <summary>
	/// Attributes that we implement directly
	/// </summary>
	internal List<System.Attribute> OwnAttributes = new();

	/// <summary>
	/// All members (methods, properties, etc) of this type.
	/// </summary>
	public MemberDescription[] Members { get; private set; }

	/// <summary>
	/// Members (methods, properties, etc) declared by exactly this type, and not inherited.
	/// </summary>
	public MemberDescription[] DeclaredMembers { get; private set; }

	/// <summary>
	/// All methods of this type.
	/// </summary>
	public MethodDescription[] Methods { get; private set; }

	/// <summary>
	/// All properties of this type.
	/// </summary>
	public PropertyDescription[] Properties { get; private set; }

	/// <summary>
	/// All fields on this type.
	/// </summary>
	public FieldDescription[] Fields { get; private set; }

	/// <summary>
	/// True if the target type is an interface
	/// </summary>
	public bool IsInterface => TargetType.IsInterface;

	/// <summary>
	/// True if the target type is an enum
	/// </summary>
	public bool IsEnum => TargetType.IsEnum;

	/// <summary>
	/// True if the target type is static
	/// </summary>
	public bool IsStatic => TargetType.IsAbstract && TargetType.IsSealed;

	/// <summary>
	/// True if the target type is a class
	/// </summary>
	public bool IsClass => TargetType.IsClass;

	/// <summary>
	/// True if the target type is a value
	/// </summary>
	public bool IsValueType => TargetType.IsValueType;

	/// <summary>
	/// Gets a value indicating whether the System.Type is abstract and must be overridden.
	/// </summary>
	public bool IsAbstract => TargetType.IsAbstract;

	/// <summary>
	/// Name of this type.
	/// </summary>
	public string Name => TargetType.Name;

	/// <summary>
	/// Namespace of this type.
	/// </summary>
	public string Namespace => TargetType.Namespace;

	/// <summary>
	/// Full name of this type.
	/// </summary>
	public string FullName => TargetType.FullName;

	/// <summary>
	/// Preferred name to use when serializing this type.
	/// </summary>
	internal string SerializedName => _useClassNameWhenSerializing ? ClassName : FullName;

	/// <inheritdoc cref="DisplayInfo.Name"/>
	public string Title => displayInfo.Name;

	/// <inheritdoc cref="DisplayInfo.Description"/>
	public string Description => displayInfo.Description;

	/// <inheritdoc cref="DisplayInfo.Icon"/>
	public string Icon => displayInfo.Icon;

	/// <inheritdoc cref="DisplayInfo.Group"/>
	public string Group => displayInfo.Group;

	/// <inheritdoc cref="DisplayInfo.Order"/>
	public int Order => displayInfo.Order;

	/// <summary>
	/// Tags are set via the [Tag] attribute
	/// </summary>
	public string[] Tags { get; internal set; }

	/// <inheritdoc cref="DisplayInfo.Alias"/>
	public string[] Aliases => displayInfo.Alias;

	/// <summary>
	/// An integer that represents this type. Based off the class name.
	/// </summary>
	public int Identity { get; internal set; }

	/// <summary>
	/// A string representing this class name. Historically this was provided by [Library( classname )].
	/// If no special name is provided, this will be type.Name.
	/// </summary>
	public string ClassName => displayInfo.ClassName;

	/// <summary>
	/// The line number of this member
	/// </summary>
	public int SourceLine { get; internal set; }

	/// <summary>
	/// The file containing this member
	/// </summary>
	public string SourceFile { get; internal set; }

	string ISourcePathProvider.Path => SourceFile;
	int ISourceLineProvider.Line => SourceLine;

	DisplayInfo displayInfo;

	private bool _useClassNameWhenSerializing;

	static bool ShouldExposeMember( MemberInfo member, Internal.TypeLibrary lib, TypeDescription source )
	{
		// |               |  public  | protected | private |
		// |---------------|---------|-----------|---------|
		// | package.*/*   |    ✅   |     ✅    |    ✅   |
		// | Sandbox.*/*   |    ✅   |     ✅    |    ❌   |
		// | System.*/*    |    ✅   |     ❌    |    ❌   |
		//
		// Note: only intrinsic types from System.* are included

		// Ignore compiler generated fields, except for event backing fields
		if ( member is FieldInfo fielda && fielda.HasAttribute( typeof( CompilerGeneratedAttribute ) ) )
		{
			if ( fielda.GetEventInfo() is null )
			{
				return false;
			}
		}

		if ( member is MethodInfo methodInfo )
		{
			// Very special case, destructors implement this as a virtual that automatically calls base destructors
			// Some destructors such as ~WeakReference() have unsafe behaviour
			// And there is no reason to explicitly call Object.Finalize for something
			if ( methodInfo.Name == Microsoft.CodeAnalysis.WellKnownMemberNames.DestructorName ) return false;

			// Ignore getter/setter methods
			if ( methodInfo.IsSpecialName ) return false;
			if ( methodInfo.Name == "GetType" ) return false;
			if ( methodInfo.Name == "ToString" ) return false;
			if ( methodInfo.Name == "Equals" ) return false;
			if ( methodInfo.Name == "GetHashCode" ) return false;
		}

		// Assume we're a System or something else if we're not package or Sandbox
		var assembly = member.DeclaringType.Assembly;

		//
		// This could be a bit shitty. We're looking at the derived types, so we try to
		// get the derived type here. What if that type isn't in the library yet? I'll tell you
		// what, we just fucked up. That's what.
		//
		if ( source.TargetType.Assembly == assembly )
		{
			// This is a dynamic assembly, allow anything
			if ( source.IsDynamicAssembly )
				return true;
		}
		else
		{
			// this assembly is dynamic, allow anything
			var dt = lib.GetType( member.DeclaringType );
			if ( dt is not null && dt.IsDynamicAssembly ) return true;
		}

		var asmName = assembly.GetName().Name!;

		//
		// Keeping this around because we can't totally rely on the above. But the above is needed.
		// One instance is when loading the game assemblies in a unittest.. the assemblies don't have
		// package. at the start. And we're not gonna check whether we're running a unit test in here..
		// because that's putting us in a house of card spagetti code situation again.
		//
		if ( asmName.StartsWith( "package." ) )
			return true;

		var isSystemType = !asmName.StartsWith( "Sandbox.", StringComparison.OrdinalIgnoreCase );

		// If we're not a Sandbox.* assembly, we're either something like System.* or SkiaSharp.* etc.
		// Only intrinsic types are allowed from these.
		if ( isSystemType && !TypeLibrary.ShouldExposePublicSystemMember( member ) )
		{
			return false;
		}

		//
		// Members with [Expose] or [ActionGraphInclude] are always exposed
		//
		if ( member.HasAttribute( typeof( ExposeAttribute ) ) )
			return true;

		if ( member.HasAttribute( typeof( ActionGraphIncludeAttribute ) ) )
			return true;

		//
		// Sandbox.* and intrinsic types from System.* - allow public and protected (family)
		//

		if ( member is MethodBase method )
		{
			if ( method.IsPublic ) return true;
			if ( !isSystemType && method.IsFamily ) return true;
		}

		if ( member is PropertyInfo property )
		{
			// should be able to access get if public
			if ( property.GetMethod?.IsPublic ?? false ) return true;

			if ( (property.GetMethod?.IsPublic ?? true) && (property.SetMethod?.IsPublic ?? true) ) return true;
			if ( !isSystemType && (property.GetMethod?.IsFamily ?? true) && (property.SetMethod?.IsFamily ?? true) ) return true;
		}

		if ( member is FieldInfo field )
		{
			if ( field.IsPublic ) return true;
			if ( !isSystemType && field.IsFamily ) return true;
		}

		return lib.ShouldExposePrivateMember?.Invoke( member ) ?? false;
	}

	internal static TypeDescription Create( TypeLibrary typeLibrary, Type type, bool dynamicAssembly, TypeDescription target )
	{
		target ??= new TypeDescription( typeLibrary );

		target.Init( type, dynamicAssembly );


		return target;
	}

	internal TypeDescription( Internal.TypeLibrary lib )
	{
		IsValid = true;
		library = lib;
	}

	internal static int GetTypeIdentity( Type type )
	{
		return $"{type.FullName},{type.Assembly.FullName.Split( "," ).FirstOrDefault()}".FastHash();
	}

	/// <summary>
	/// This needs to generate a unique string per member on a type.
	/// </summary>
	static string GetMemberIdentity( MemberInfo info )
	{
		return $"{info.DeclaringType}/{info}";
	}

	private void Init( Type type, bool dynamicAssembly )
	{
		TargetType = type;
		IsDynamicAssembly = dynamicAssembly;
		Identity = GetTypeIdentity( type );
		GenericArguments = type.GetGenericArguments();
		Interfaces = type.GetInterfaces();

		var members = type.GetMembers( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static )
								.Where( x => ShouldExposeMember( x, library, this ) )
								.ToList();


		// If GetMemberIdentity isn't creating unique per type then something is wrong. Debug here.

		/*
		var groups = members.GroupBy( x => GetMemberIdentity( x ) ).Where( x => x.Count() > 1 );
		if ( groups.Any() )
		{
			throw new System.Exception( $"DUPLOICATES: {groups.First().Key}" );
		}*/

		Dictionary<string, MemberDescription> previousPool = Members?.ToDictionary( x => x.Ident, x => x );
		ConcurrentBag<MemberDescription> md = new();
		System.Threading.Tasks.Parallel.ForEach( members, x =>
		{
			var ident = GetMemberIdentity( x );

			MemberDescription previous = null;
			previousPool?.TryGetValue( ident, out previous );
			var memberDesc = MemberDescription.Create( this, x, previous );

			if ( memberDesc is not null )
			{
				memberDesc.Ident = ident;
				md.Add( memberDesc );
			}
		} );

		//
		// If we don't have source lines, then order by the order we were in the GetMembers array.
		// which is usually ordered by source lines anyway.
		//
		foreach ( var member in md.Where( x => x.SourceLine == 0 ) )
		{
			member.SourceLine = members.IndexOf( member.MemberInfo );
		}

		Members = md.OrderBy( x => x.SourceLine ).ToArray();
		DeclaredMembers = Members.Where( x => x.MemberInfo.DeclaringType == type ).ToArray();
		Methods = Members.OfType<MethodDescription>().ToArray();
		Properties = Members.OfType<PropertyDescription>().ToArray();
		Fields = Members.OfType<FieldDescription>().ToArray();

		CaptureAttributes();
		displayInfo = DisplayInfo.ForMember( type, true, Attributes );

		if ( !string.IsNullOrWhiteSpace( displayInfo.ClassName ) )
		{
			Sandbox.Internal.TypeLibrary.OnClassName?.Invoke( displayInfo.ClassName );
		}
	}

	internal void Dispose()
	{
		foreach ( var attr in Attributes )
		{
			if ( attr is ITypeAttribute ita )
			{
				try
				{
					ita.TypeUnregister();
				}
				catch ( System.Exception e )
				{
					// Cheeky but we definitely want to print a warning for this
					Logging.GetLogger( "Type Library" ).Warning( e, $"Exception when calling {attr.GetType()}.TypeUnregister()" );
				}
			}
		}

		//
		// clear the member info so hotload isn't gonna find anything and shit itself
		//
		foreach ( var member in Members ?? Array.Empty<MemberDescription>() )
		{
			member.Dispose();
		}

		//TargetType = null; // no! HotloadUpgrader needs it!
		IsValid = false;
	}

	void CaptureAttributes()
	{
		OwnAttributes.Clear();
		Attributes.Clear();

		_useClassNameWhenSerializing = false;

		HashSet<string> tags = null;

		// Get our attributes
		var attributes = TargetType.GetCustomAttributes<System.Attribute>( false );

		OwnAttributes.AddRange( attributes );
		Attributes.AddRange( OwnAttributes );

		// and any base attributes that we're allowed to inherit
		if ( TargetType.BaseType != null )
		{
			var baseAttributes = TargetType.BaseType.GetCustomAttributes<System.Attribute>( true ).Where( x => x is not IUninheritable );
			Attributes.AddRange( baseAttributes );
		}

		foreach ( var attr in Attributes )
		{
			if ( attr is ITypeAttribute ita )
			{
				ita.TargetType = TargetType;
				library.PostAddCallbacks.Enqueue( () => ita.TypeRegister() );
			}

			if ( attr is TagAttribute tag )
			{
				foreach ( var n in tag.EnumerateValues() )
				{
					tags ??= new();
					tags.Add( n.ToLower() );
				}
			}
		}

		SourceLine = 0;
		SourceFile = null;

		if ( OwnAttributes.OfType<SourceLocationAttribute>().MinBy( x => x.Path.Length ) is { } sourceLocation )
		{
			SourceLine = sourceLocation.Line;
			SourceFile = sourceLocation.Path;
		}

		foreach ( var attr in OwnAttributes )
		{
			// TODO: decide if we want to always serialize as the [ClassName], then we can get rid of the ".comp" check
			if ( attr is ClassNameAttribute className && className.Value?.EndsWith( ".comp", StringComparison.OrdinalIgnoreCase ) is true )
			{
				_useClassNameWhenSerializing = true;
			}
		}

		Tags = tags?.ToArray() ?? Array.Empty<string>();
	}

	/// <summary>
	/// Returns true if this is named the passed name, either through classname, target class name or an alias
	/// </summary>
	public bool IsNamed( string name )
	{
		if ( string.Equals( name, ClassName, StringComparison.OrdinalIgnoreCase ) )
			return true;

		if ( string.Equals( name, displayInfo.Fullname, StringComparison.OrdinalIgnoreCase ) )
			return true;

		if ( Aliases.Any( x => string.Equals( name, x, StringComparison.OrdinalIgnoreCase ) ) )
			return true;

		return string.Equals( Name, name, StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>
	/// Returns the first attribute of given type, if any are present.
	/// </summary>
	public T GetAttribute<T>( bool inherited = false ) where T : Attribute
	{
		return (inherited ? Attributes : OwnAttributes).OfType<T>().FirstOrDefault();
	}

	/// <summary>
	/// Returns all attributes of given type, if any are present.
	/// </summary>
	public IEnumerable<T> GetAttributes<T>( bool inherited = false ) where T : Attribute
	{
		return (inherited ? Attributes : OwnAttributes).OfType<T>();
	}

	/// <summary>
	/// Returns true if the class has this attribute
	/// </summary>
	public bool HasAttribute<T>( bool inherited = true ) where T : Attribute
	{
		return (inherited ? Attributes : OwnAttributes).OfType<T>().Any();
	}

	/// <summary>
	/// True if we have this tag.
	/// </summary>
	public bool HasTag( string tag ) => Tags.Contains( tag );

	/// <summary>
	/// Get property by name
	/// </summary>
	public PropertyDescription GetProperty( string name ) => Properties.FirstOrDefault( x => x.IsStatic == false && x.IsNamed( name ) );

	/// <summary>
	/// Get field by name
	/// </summary>
	internal FieldDescription GetField( string name ) => Fields.FirstOrDefault( x => x.IsStatic == false && x.IsNamed( name ) );

	/// <summary>
	/// Get value by field or property name
	/// </summary>
	public object GetValue( object instance, string name )
	{
		return GetValue( instance, name, out _ );
	}

	/// <summary>
	/// Get value by field or property name, and which type the member is declared to store.
	/// </summary>
	internal object GetValue( object instance, string name, out Type memberType )
	{
		var member = Members.FirstOrDefault( x => x.IsStatic == false && x.IsNamed( name ) );

		switch ( member )
		{
			case PropertyDescription pd:
				memberType = pd.PropertyType;
				return pd.GetValue( instance );
			case FieldDescription fd:
				memberType = fd.FieldType;
				return fd.GetValue( instance );
			default:
				memberType = null;
				return default;
		}
	}

	/// <summary>
	/// Get value by field or property name
	/// </summary>
	public bool SetValue( object instance, string name, object value )
	{
		var member = Members.Where( x => x.IsStatic == false && x.IsNamed( name ) ).FirstOrDefault();
		if ( member is null ) return default;

		if ( member is PropertyDescription pd )
		{
			pd.SetValue( instance, value );
			return true;
		}

		if ( member is FieldDescription fd )
		{
			fd.SetValue( instance, value );
			return true;
		}

		return default;
	}

	/// <summary>
	/// Get a method by name (will not find static methods)
	/// </summary>
	public MethodDescription GetMethod( string name ) => Methods.FirstOrDefault( x => !x.IsStatic && x.IsNamed( name ) );

	/// <summary>
	/// True if we're a generic type
	/// </summary>
	public bool IsGenericType => TargetType.IsGenericType;

	/// <summary>
	/// If we're a generic type this will return our generic parameters.
	/// </summary>
	// PAIN DAY TODO: Rename to GenericParameters, since this will never be a constructed generic type
	public Type[] GenericArguments { get; private set; }

	/// <summary>
	/// If we implement any interfaces they will be here
	/// </summary>
	public Type[] Interfaces { get; private set; }

	/// <summary>
	/// Create an instance of this class, return it as a T.
	/// If it can't be cast to a T we won't create it and will return null.
	/// </summary>
	public T Create<T>( object[] args = null )
	{
		var type = typeof( T );

		if ( !TargetType.IsAssignableTo( type ) )
			return default;

		return (T)System.Activator.CreateInstance( TargetType, args );
	}


	/// <summary>
	/// Create an instance of this class using generic arguments
	/// We're going to assume you know what you're doing here and let it throw any exceptions it wants.
	/// </summary>
	public T CreateGeneric<T>( Type[] typeArgs = null, object[] args = null )
	{
		// PAIN DAY TODO: maybe typeArguments shouldn't have a default value? Why do we allow null?

		var genericType = MakeGenericType( typeArgs );
		if ( genericType is null ) return default;

		// create a new type
		return (T)System.Activator.CreateInstance( genericType, args );
	}

	/// <summary>
	/// For generic type definitions, create a type by substituting the given types for each type parameter.
	/// Returns null if any of the type arguments violate the generic constraints.
	/// </summary>
	public Type MakeGenericType( Type[] inargs )
	{
		// make a copy of the type args so they can't change them on a thread while we're validating
		var typeArgs = inargs?.ToArray();

		//
		// Check that all the types are safe for us to instantiate.
		//
		foreach ( var arg in typeArgs )
		{
			library.AssertType( arg );
		}

		// Validate generic constraints
		var genericParams = TargetType.GetGenericArguments();
		if ( genericParams.Length != typeArgs.Length )
			return null;

		for ( int i = 0; i < genericParams.Length; i++ )
		{
			var param = genericParams[i];
			var arg = typeArgs[i];
			var attributes = param.GenericParameterAttributes;

			// Check reference type constraint (class)
			if ( (attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0 )
			{
				if ( arg.IsValueType )
					return null;
			}

			// Check value type constraint (struct)
			if ( (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 )
			{
				if ( !arg.IsValueType || Nullable.GetUnderlyingType( arg ) != null )
					return null;
			}

			// Check new() constraint
			if ( (attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 )
			{
				if ( !arg.IsValueType && arg.GetConstructor( Type.EmptyTypes ) == null )
					return null;
			}

			// Check managed/unmanaged constraints via attributes
			foreach ( var constraintAttr in param.GetCustomAttributes( false ) )
			{
				var attrType = constraintAttr.GetType();

				// Check for 'unmanaged' constraint
				if ( attrType.Name == "IsUnmanagedAttribute" )
				{
					if ( !SandboxedUnsafe.IsAcceptablePod( arg ) )
						return null;
				}
			}

			// Check base type and interface constraints
			var constraints = param.GetGenericParameterConstraints();
			foreach ( var constraint in constraints )
			{
				if ( !constraint.IsAssignableFrom( arg ) )
					return null;
			}
		}

		return TargetType.MakeGenericType( typeArgs );
	}


	public override string ToString() => TargetType.FullName;


}
