using Facepunch.ActionGraphs;
using Sandbox.UI;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;

namespace Sandbox.Internal;

[SkipHotload]
public partial class TypeLibrary
{
	/// <summary>
	/// The editor's TypeLibrary contains the game's typelibrary as well as all the editor types. 
	/// This is null when not running in editor mode!
	/// </summary>
	internal static TypeLibrary Editor;

	/// <summary>
	/// Called to populate StringTokens with our custom tokens in c#
	/// </summary>
	internal static Action<string> OnClassName;

	/// <summary>
	/// The current TypeLibrary. God we could make TypeLibrary a big sexy static class how awesome would that be.
	/// </summary>
	//internal static TypeLibrary Current;

	Logger log;

	/// <summary>
	/// A list of loaded types.
	/// </summary>
	internal HashSet<Type> Types;
	internal Dictionary<int, WeakReference<TypeDescription>> removedTypes = new();

	readonly ConcurrentDictionary<Type, TypeDescription> typedata;
	readonly ConcurrentDictionary<int, MemberDescription> members;

	internal ConcurrentQueue<Action> PostAddCallbacks;

	internal TypeLibrary()
	{
		log = new Logger( $"TypeLibrary" );
		log.Trace( "Created" );

		Types = new HashSet<Type>( 512 );
		typedata = new ConcurrentDictionary<Type, TypeDescription>();
		members = new ConcurrentDictionary<int, MemberDescription>();

		InitBytePack();
	}

	/// <summary>
	/// Clean up after ourselves
	/// </summary>
	internal void Dispose()
	{
		log.Trace( "Dispose" );

		Types.Clear();
	}

	/// <summary>
	/// Add essential types from <c>System</c> assemblies, for example
	/// <see cref="object"/>, <see cref="string"/>, and <see cref="int"/>.
	/// Only public members of these types will be added.
	/// </summary>
	internal void AddIntrinsicTypes()
	{
		var coreLibAsm = typeof( object ).Assembly;

		foreach ( var type in IntrinsicTypes )
		{
			if ( type.Assembly == coreLibAsm && Types.Add( type ) )
			{
				AddType( type, false );
			}
		}
	}

	/// <summary>
	/// Add an assembly yo the library.
	/// If marked as dynamic then all types are added.
	/// </summary>
	internal void AddAssembly( Assembly incoming, bool isDynamic )
	{
		if ( incoming == null ) throw new System.ArgumentNullException( nameof( incoming ) );

		var types = incoming
						.GetTypes()
						.Where( x => !x.IsAssignableTo( typeof( Delegate ) ) )
						.Where( x => isDynamic || ShouldExposeType( x ) )
						.ToArray();

		PostAddCallbacks = new ConcurrentQueue<Action>();

		log.Trace( $"Registering {incoming} (dynamic:{isDynamic}) ({types.Count()} Types)" );

		var added = new ConcurrentBag<Action>();
		System.Threading.Tasks.Parallel.ForEach( types, t => AddType( t, isDynamic ) );

		foreach ( var t in types )
			Types.Add( t );

		//
		// Call the callbacks on the main thread
		//
		while ( PostAddCallbacks.TryDequeue( out var callback ) )
		{
			callback();
		}

		PostAddCallbacks = null;
		InitBytePack();
	}

	// These types get sucked in the library
	// we do this manaully so we don't accidentally
	// allow access to something that shouldn't be accessed.
	//
	// All public members of these types must be safe to use.
	private static HashSet<Type> IntrinsicTypes { get; } = new()
	{
		// System
		typeof(object),
		typeof(char), typeof(string),
		typeof(bool),
		typeof(byte), typeof(sbyte),
		typeof(ushort), typeof(short),
		typeof(uint), typeof(int),
		typeof(ulong), typeof(long),
		typeof(float), typeof(double),
		typeof(Nullable<>),
		typeof(List<>),
		typeof(Dictionary<,>),
		typeof(HashSet<>),
		typeof(Array),
		typeof(IList<>),
		typeof(ValueTuple<>),
		typeof(ValueTuple<,>),
		typeof(ValueTuple<,,>),
		typeof(ValueTuple<,,,>),
		typeof(ValueTuple<,,,,>),
		typeof(ValueTuple<,,,,,>),
		typeof(ValueTuple<,,,,,,>),
		typeof(ValueTuple<,,,,,,,>),

		// Sandbox
		typeof(Rect),
		typeof(RectInt),
		typeof(Rect3D),
		typeof(Vector2),
		typeof(Vector3),
		typeof(Vector4),
		typeof(Vector2Int),
		typeof(Vector3Int),
		typeof(Color),
		typeof(Color.Rgba16),
		typeof(Color32),
		typeof(ColorHsv),
		typeof(Capsule),
		typeof(Line),
		typeof(Transform),
		typeof(BBox),
		typeof(Sphere),
		typeof(Frustum),
		typeof(Length),
		typeof(Angles),
		typeof(Rotation),
		typeof(Margin),
		typeof(RealTimeSince),
		typeof(RealTimeUntil),
		typeof(Curve),
		typeof(CurveRange),
		typeof(RangedFloat),
		typeof(TextFlag),

		// UI
		typeof(Sandbox.UI.OverflowMode),
		typeof(Sandbox.UI.Align),
		typeof(Sandbox.UI.PositionMode),
		typeof(Sandbox.UI.FlexDirection),
		typeof(Sandbox.UI.Justify),
		typeof(Sandbox.UI.DisplayMode),
		typeof(Sandbox.UI.PointerEvents),
		typeof(Sandbox.UI.Wrap),
		typeof(Sandbox.UI.TextAlign),
		typeof(Sandbox.UI.TextOverflow),
		typeof(Sandbox.UI.WordBreak),
		typeof(Sandbox.UI.TextTransform),
		typeof(Sandbox.UI.TextSkipInk),
		typeof(Sandbox.UI.TextDecorationStyle),
		typeof(Sandbox.UI.TextDecoration),
		typeof(Sandbox.UI.WhiteSpace),
		typeof(Sandbox.UI.FontStyle),
		typeof(Sandbox.UI.ImageRendering),
		typeof(Sandbox.UI.BorderImageFill),
		typeof(Sandbox.UI.BorderImageRepeat),
		typeof(Sandbox.UI.BackgroundRepeat),
		typeof(Sandbox.UI.MaskMode),
		typeof(Sandbox.UI.MaskScope),
		typeof(Sandbox.UI.FontSmooth),
		typeof(Sandbox.UI.ObjectFit),
		typeof(Sandbox.UI.ObjectFit),

		// ActionGraph
		typeof(AssignmentKind)
	};

	/// <summary>
	/// For some system types we only want to expose a subset of public members. In the future
	/// we should try to use the same whitelist as when checking user code, but that's a bit scary.
	/// </summary>
	private static Dictionary<Type, HashSet<string>> WhitelistedSystemMembers { get; } = new()
	{
		{ typeof(Array), new() { nameof(Array.Length) } },
		{ typeof(IList<>), new() { nameof(IList<object>.Count) } }
	};

	internal static bool IsIntrinsicType( Type t )
	{
		return IntrinsicTypes.Contains( t );
	}

	static bool ShouldExposeType( Type t )
	{
		if ( System.Attribute.IsDefined( t, typeof( LibraryAttribute ) ) ) return true;
		if ( System.Attribute.IsDefined( t, typeof( ExposeAttribute ) ) ) return true;
		if ( System.Attribute.IsDefined( t, typeof( ActionGraphIncludeAttribute ) ) ) return true;

		if ( IsIntrinsicType( t ) ) return true;

		// If we're a nested type, defer to the class we're declared in
		if ( t.DeclaringType != null )
			return ShouldExposeType( t.DeclaringType );

		return false;
	}

	internal static bool ShouldExposePublicSystemMember( MemberInfo member )
	{
		if ( member.DeclaringType is null )
		{
			return false;
		}

		if ( !IntrinsicTypes.Contains( member.DeclaringType ) )
		{
			return false;
		}

		if ( !WhitelistedSystemMembers.TryGetValue( member.DeclaringType, out var whitelisted ) )
		{
			// If not specifically whitelisted, allow all public members

			return true;
		}

		return whitelisted.Contains( member.Name );
	}

	private void AddType( Type type, bool dynamicAssembly )
	{
		TypeDescription previous = TakeRemovedType( type );

		var typeData = TypeDescription.Create( this, type, dynamicAssembly, previous );

		typedata[type] = typeData;

		foreach ( var m in typeData.Members )
		{
			members[m.Identity] = m;
		}

		InvalidateCache();
	}

	/// <summary>
	/// Find a type that was previously removed
	/// </summary>
	private TypeDescription TakeRemovedType( Type type )
	{
		var ident = TypeDescription.GetTypeIdentity( type );

		WeakReference<TypeDescription> reference = default;

		lock ( removedTypes )
		{
			if ( !removedTypes.TryGetValue( ident, out reference ) )
				return null;

			removedTypes.Remove( ident );
		}

		if ( !reference.TryGetTarget( out var targetType ) )
			return null;

		return targetType;

	}

	/// <summary>
	/// Store this type in the removed list, incase the assembly comes back. We
	/// can re-initialize it - so all the references to it will be unchanged.
	/// </summary>
	private void StoreRemovedType( TypeDescription td )
	{
		removedTypes[td.Identity] = new WeakReference<TypeDescription>( td );
	}

	/// <summary>
	/// Removed all of the stored removed types
	/// </summary>
	internal void ClearRemovedTypes()
	{
		removedTypes.Clear();
	}

	/// <summary>
	/// Remove a specific assembly and all types associated with it
	/// </summary>
	internal void RemoveAssembly( Assembly asm )
	{
		InitBytePack();

		log.Trace( $"Removing {asm}" );

		foreach ( var type in Types.Where( x => x.Assembly == asm ).ToArray() )
		{
			Types.Remove( type );
			log.Trace( $" - {type}" );

			if ( typedata.TryRemove( type, out var typeDescription ) )
			{
				foreach ( var m in typeDescription.Members )
				{
					members.TryRemove( m.Identity, out _ );
				}

				StoreRemovedType( typeDescription );
				typeDescription?.Dispose();
			}
		}

		InvalidateCache();
	}

	/// <summary>
	/// Get hash of a type.
	/// </summary>
	public int GetTypeIdent( Type type )
	{
		return $"{type.Assembly.GetName().Name}.{type.Name}".FastHash();
	}

	[System.Obsolete( "Use GetType" )]
	internal TypeDescription GetTypeByName( string name, System.Type canBeAssignedTo )
	{
		return typedata
					.Where( x => canBeAssignedTo == null || canBeAssignedTo.IsAssignableFrom( x.Key ) )
					.Where( x => x.Value.IsNamed( name ) )
					.Select( x => x.Value )
					.FirstOrDefault();
	}

	/// <summary>
	/// Get the description for a specific type. This will return null if you don't have whitelist access to the type.
	/// For constructed generic types, this will give you the description of the generic type definition.
	/// </summary>
	public TypeDescription GetType( Type type ) => typedata.GetValueOrDefault( type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type );

	/// <summary>
	/// Convert a list of <see cref="Type"/> to a list of integers representing their TypeLibrary identity.
	/// </summary>
	internal int[] ToIdentities( Type[] types )
	{
		return types?.Select( t => GetType( t ).Identity ).ToArray();
	}

	/// <summary>
	/// Convert a list of integers representing their TypeLibrary identity to a list of <see cref="Type"/>
	/// </summary>
	internal Type[] FromIdentities( int[] identities )
	{
		return identities?.Select( i => GetTypeByIdent( i ).TargetType ).ToArray();
	}

	/// <summary>
	/// Get a list of types that implement this generic type
	/// </summary>
	public IReadOnlyList<TypeDescription> GetGenericTypes( Type type, Type[] types )
	{
		var genericType = type.MakeGenericType( types );
		return GetTypes( genericType );
	}

	/// <summary>
	/// Get descriptions for all types that derive from T
	/// </summary>
	public IReadOnlyList<TypeDescription> GetTypes( Type type )
	{
		if ( type == null )
			return Cached( HashCode.Combine( "GetTypes", type ), () => typedata.Values.ToImmutableList() );

		return Cached( HashCode.Combine( "GetTypes", type ), () => typedata.Values.Where( x => x.TargetType.IsAssignableTo( type ) ).ToImmutableList() );
	}

	/// <summary>
	/// Find a TypeDescription that derives from <typeparamref name="T"/>, by name
	/// </summary>
	public TypeDescription GetType<T>( string name ) => GetType<T>( name, false );

	/// <summary>
	/// Find a TypeDescription that derives from T by name, which can be an Alias etc.
	/// If preferAddonAssembly is true, then if there are conflicts we'll prefer types that are 
	/// in addon code.
	/// </summary>
	public TypeDescription GetType<T>( string name, bool preferAddonAssembly ) => GetType( typeof( T ), name, preferAddonAssembly );

	/// <summary>
	/// Find a TypeDescription that derives from T by name, which can be an Alias etc.
	/// If preferAddonAssembly is true, then if there are conflicts we'll prefer types that are 
	/// in addon code.
	/// </summary>
	public TypeDescription GetType( Type type, string name, bool preferAddonAssembly = false )
	{
		return Cached( HashCode.Combine( "GetType", type, name, preferAddonAssembly ), () => GetTypes( type ).Where( x => x.IsNamed( name ) ).OrderBy( x => (x.IsDynamicAssembly == preferAddonAssembly) ? 0 : 1 ).FirstOrDefault() );

	}

	/// <summary>
	/// Get descriptions for all types that derive from T
	/// </summary>
	public IReadOnlyList<TypeDescription> GetTypes<T>() => GetTypes( typeof( T ) );

	/// <summary>
	/// Get all types
	/// </summary>
	public IEnumerable<TypeDescription> GetTypes() => typedata.Values;

	/// <summary>
	/// Find the description for templated type
	/// </summary>
	public TypeDescription GetType<T>() => GetType( typeof( T ) );


	/// <summary>
	/// Find the description type
	/// </summary>
	public bool TryGetType( Type t, out TypeDescription typeDescription )
	{
		return typedata.TryGetValue( t, out typeDescription );
	}

	/// <summary>
	/// Find the description type
	/// </summary>
	public bool TryGetType<T>( out TypeDescription typeDescription ) => TryGetType( typeof( T ), out typeDescription );

	/// <summary>
	/// Find a TypeDescription by name
	/// </summary>
	public TypeDescription GetType( string name ) => GetType( null, name, false );

	/// <summary>
	/// Find a TypeDescription by name
	/// </summary>
	public TypeDescription GetTypeByIdent( int ident ) => typedata.Values.FirstOrDefault( x => x.Identity == ident );

	/// <summary>
	/// Find a <see cref="MemberDescription"/> by its <see cref="MemberDescription.Identity"/>
	/// </summary>
	public MemberDescription GetMemberByIdent( int ident )
	{
		return members.GetValueOrDefault( ident, default );
	}



	/// <summary>
	/// Find a TypeDescription that derives from <paramref name="baseType"/>, by name
	/// </summary>
	[Obsolete( "Use GetType with the arguments the other way around" )]
	public TypeDescription GetType( string name, Type baseType ) => GetType( baseType, name, false );

	/// <summary>
	/// Performs <see cref="Type.GetGenericArguments"/> with access control checks.
	/// Will throw if any arguments aren't in the whitelist.
	/// </summary>
	/// <param name="genericType">Constructed generic type to get the arguments of</param>
	public Type[] GetGenericArguments( Type genericType )
	{
		if ( !genericType.IsConstructedGenericType )
		{
			throw new ArgumentException( "Expected a generic instance type.", nameof( genericType ) );
		}

		var args = genericType.GetGenericArguments();

		foreach ( var arg in args )
		{
			AssertType( arg );
		}

		return args;
	}

	/// <summary>
	/// Return true if this type contains this attribute
	/// </summary>
	public bool HasAttribute<T>( Type type ) where T : System.Attribute
	{
		if ( !typedata.TryGetValue( type, out var data ) )
			return false;

		return data.Attributes.OfType<T>().Any();
	}

	/// <summary>
	/// Check if all properties of this class instance pass their <see cref="ValidationAttribute"/>.
	/// </summary>
	/// <param name="obj">Object to test.</param>
	/// <returns>True if all properties pass their validity checks (or if there are no checks), false otherwise.</returns>
	public bool CheckValidationAttributes<T>( T obj ) where T : class
	{
		var type = GetType<T>();

		foreach ( var prop in type.Properties )
		{
			// TODO: output dictionary of property-errors?
			var valid = prop.CheckValidationAttributes( obj, out _ );
			if ( !valid ) return false;
		}

		return true;
	}


	/// <summary>
	/// Get a SerializedObject version of this object
	/// </summary>
	public SerializedObject GetSerializedObject( object target )
	{
		ArgumentNullException.ThrowIfNull( target, "target" );

		var so = SerializedCollection.Create( target.GetType() );
		if ( so is not null )
		{
			so.SetTargetObject( target, null );
			so.PropertyToObject = PropertyToObject;
			return so;
		}

		var t = GetType( target.GetType() );
		if ( t == null ) throw new System.Exception( $"Can't serialize object type '{target.GetType()}'" );

		return new TypeSerializedObject( target, t );
	}

	internal SerializedObject PropertyToObject( SerializedProperty property )
	{
		var targetType = GetType( property.PropertyType );

		if ( targetType is null )
			return null;

		var getTarget = () => property.GetValue<object>();
		if ( getTarget() is null )
			return null;

		return new TypeSerializedObject( getTarget, targetType, property );
	}

	/// <summary>
	/// Gets a SerializedObject version of a value retrieved from a function.
	/// </summary>
	public SerializedObject GetSerializedObject( Func<object> fetchTarget, TypeDescription typeDescription, SerializedProperty parent = null )
	{
		// Some extra safety here, since this is public
		object SafeFetchTarget()
		{
			var value = fetchTarget();

			if ( value is not null && !typeDescription.TargetType.IsInstanceOfType( value ) )
			{
				throw new Exception( $"Type mismatch, expected '{typeDescription.FullName}' but found '{value.GetType().FullName}'." );
			}

			return value;
		}

		var value = SafeFetchTarget();
		if ( value is not null )
		{
			var so = SerializedCollection.Create( value?.GetType() );
			if ( so is not null )
			{
				so.ParentProperty = parent;
				so.SetTargetObject( value, parent );
				so.PropertyToObject = PropertyToObject;
				return so;
			}
		}

		return new TypeSerializedObject( (Func<object>)SafeFetchTarget, typeDescription, parent );
	}

	/// <summary>
	/// Get a SerializedObject version of this type of object, but data is stored in a dictionary
	/// </summary>
	internal SerializedObject GetSerializedObjectDictionary<T>( CaseInsensitiveDictionary<string> target ) where T : class
	{
		return GetSerializedObjectDictionary( typeof( T ), target );
	}

	/// <summary>
	/// Get a SerializedObject version of this type of object, but data is stored in a dictionary
	/// </summary>
	internal SerializedObject GetSerializedObjectDictionary( Type type, CaseInsensitiveDictionary<string> target )
	{
		ArgumentNullException.ThrowIfNull( target, "target" );
		ArgumentNullException.ThrowIfNull( type, "type" );

		var t = GetType( type );
		if ( t == null ) throw new System.Exception( $"Can't serialize object type" );

		return new DictionarySerializedObject( target, t );
	}

	/// <summary>
	/// Get a class describing the values of an enum
	/// </summary>
	public EnumDescription GetEnumDescription( Type enumType )
	{
		if ( !enumType.IsEnum ) throw new System.Exception( $"Can't get enum description for non-enum type" );

		// cache me
		return new EnumDescription( enumType );
	}

	/// <summary>
	/// Create a serialized property that uses a getter and setter
	/// </summary>
	public SerializedProperty CreateProperty<T>( string title, Func<T> get, Action<T> set, Attribute[] attributes = null, SerializedObject parent = null )
	{
		var prop = new ActionBasedSerializedProperty<T>( title, title, "", get, set, attributes, parent );
		prop.PropertyToObject = PropertyToObject;

		return prop;
	}

	/// <summary>
	/// Create a serialized property from a SerializedObject
	/// </summary>
	public SerializedProperty CreateProperty<T>( string title, SerializedObject so, Attribute[] attributes = null, SerializedObject parent = null )
	{
		var prop = new ActionBasedSerializedProperty<T>( title, title, "", () => (T)so.Targets.First(), t => { }, attributes, parent );
		prop.PropertyToObject = o => so;

		return prop;
	}
}

