using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// We cache results for some expensive reflection queries.
/// This results in large performance improvements during various operations (Cloning, NetworkSpawn, Serialization...)
/// </summary>
internal static class ReflectionQueryCache
{
	private static Dictionary<Type, bool> _isTypeCloneableByCopy = new();
	private static Dictionary<Type, bool> _isICloneableSafe = new();
	private static Dictionary<Type, MemberDescription[]> _orderedMemberCache = new();
	private static Dictionary<Type, PropertyDescription[]> _requiredComponentMemberCache = new();

	public record SyncVarPropertyAndAttribute( PropertyInfo Property, SyncAttribute Attribute );
	private static Dictionary<Type, SyncVarPropertyAndAttribute[]> _syncVarMemberCache = new();

	internal static bool IsEmpty => _isTypeCloneableByCopy.Count == 0
		&& _isICloneableSafe.Count == 0
		&& _orderedMemberCache.Count == 0
		&& _requiredComponentMemberCache.Count == 0
		&& _syncVarMemberCache.Count == 0
		&& MemberCopyCache.IsEmpty;

	/// <summary>
	/// Clears the type cache, called after HotLoad and after a game ended.
	/// Called from <see cref="Sandbox.Engine.GlobalContext.OnHotload"/> and <see cref="Game.Close"/>.
	/// </summary>
	public static void ClearTypeCache()
	{
		_isTypeCloneableByCopy.Clear();
		_isICloneableSafe.Clear();
		_orderedMemberCache.Clear();
		_requiredComponentMemberCache.Clear();
		_syncVarMemberCache.Clear();
		MemberCopyCache.Clear();
	}

	/// <summary>
	/// Returns true if this type's ICloneable.Clone() is safe to call during cloning,
	/// meaning the type declares its own Clone() rather than inheriting a shallow-copy
	/// implementation from a BCL base type (Delegate, Array, etc.).
	/// Result is cached since GetMethod is expensive.
	/// </summary>
	public static bool IsICloneableSafe( Type t )
	{
		if ( _isICloneableSafe.TryGetValue( t, out var cached ) )
			return cached;

		var isSafe = t.GetMethod( nameof( ICloneable.Clone ), Type.EmptyTypes )?.DeclaringType == t;

		_isICloneableSafe[t] = isSafe;
		return isSafe;
	}

	/// <summary>
	/// Returns all properties and fields that should be (de)serialized.
	/// Also sorts the members for historic reasons.
	/// </summary>
	public static IEnumerable<MemberDescription> OrderedSerializableMembers( Type t )
	{
		if ( _orderedMemberCache.TryGetValue( t, out var members ) )
		{
			return members;
		}

		var type = Game.TypeLibrary.GetType( t );

		if ( type is null )
		{
			Log.Warning( $"TypeLibrary could not find {t}" );
			return Array.Empty<MemberDescription>();
		}

		// It's fucked that we need to order the members for cloning, but some games actually rely on that order.
		// See https://github.com/Facepunch/sbox/issues/1785
		var fieldAndPropertyMembers = type.Members.Where( ShouldSerializeMember ).OrderBy( x => x.Name ).ToArray();
		_orderedMemberCache[t] = fieldAndPropertyMembers;

		return fieldAndPropertyMembers;
	}

	private static bool ShouldSerializeMember( MemberDescription memberDesc )
	{
		if ( memberDesc is not PropertyDescription && memberDesc is not FieldDescription ) return false;
		if ( memberDesc.IsStatic ) return false;

		return memberDesc.HasAttribute<PropertyAttribute>() && !memberDesc.HasAttribute<JsonIgnoreAttribute>();
	}

	/// <summary>
	/// Returns all properties that have a [RequireComponent] attribute.
	/// </summary>
	public static IEnumerable<PropertyDescription> RequiredComponentMembers( Type t )
	{
		if ( _requiredComponentMemberCache.TryGetValue( t, out var members ) )
		{
			return members;
		}

		var type = Game.TypeLibrary.GetType( t );

		if ( type is null )
		{
			Log.Warning( $"TypeLibrary could not find {t}" );
			return Array.Empty<PropertyDescription>();
		}

		var requiredComponentProps = type.Properties
			.Where( IsRequiredComponent )
			.ToArray();

		_requiredComponentMemberCache[t] = requiredComponentProps;

		return requiredComponentProps;
	}

	private static bool IsRequiredComponent( PropertyDescription prop )
	{
		return prop.HasAttribute<RequireComponentAttribute>();
	}

	/// <summary>
	/// Returns all properties that have a [Sync] attribute.
	/// </summary>
	public static IEnumerable<SyncVarPropertyAndAttribute> SyncProperties( Type t )
	{
		if ( _syncVarMemberCache.TryGetValue( t, out var members ) )
		{
			return members;
		}

		var properties = new List<PropertyInfo>();
		var currentType = t;

		// Collect all properties from the type and its base types
		while ( currentType != null )
		{
			var ourProperties = currentType.GetProperties( BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly );
			properties.AddRange( ourProperties );
			currentType = currentType.BaseType;
		}

		// Find properties with the [Sync] attribute and create records
		var syncProperties = properties
			.Select( prop =>
			{
				var syncAttribute = prop.GetCustomAttributes( typeof( SyncAttribute ), inherit: true ).FirstOrDefault() as SyncAttribute;
				if ( syncAttribute != null )
				{
					return new SyncVarPropertyAndAttribute( prop, syncAttribute );
				}
				return null;
			} )
			.Where( x => x != null )
			.ToArray();

		_syncVarMemberCache[t] = syncProperties;

		return syncProperties;
	}

	/// <summary>
	/// Determines if a type can be cloned by a simple copy.
	/// This recursively walks through all properties and fields of the type to determine if they are cloneable by copy.
	/// Since this is fairly expensive, we cache the results for each type.
	/// </summary>
	public static bool IsTypeCloneableByCopy( Type t )
	{
		if ( _isTypeCloneableByCopy.TryGetValue( t, out var result ) )
		{
			return result;
		}

		// Use a HashSet to track types being processed to avoid infinite recursion
		_processingTypesCache.Clear();
		var isCloneableByCopy = IsTypeCloneableByCopyInternal( t, _processingTypesCache );

		_isTypeCloneableByCopy[t] = isCloneableByCopy;

		return isCloneableByCopy;
	}

	// Avoid allocation -> cache this
	private static HashSet<Type> _processingTypesCache = new();

	private static bool IsTypeCloneableByCopyInternal( Type t, HashSet<Type> processingTypes )
	{
		if ( t.IsPrimitive || t.IsEnum || t == typeof( string ) )
		{
			return true;
		}

		// Resource references can just be copied,
		if ( t.HasBaseType( "Sandbox.Resource" ) )
		{
			return true;
		}

		// Immutable lists are safe to copy, if their containing type is safe to copy
		if ( IsImmutableType( t ) )
		{
			return IsTypeCloneableByCopyInternal( t.GetGenericArguments()[0], processingTypes );
		}

		// Other Ref types are not cloneable by copy
		if ( !t.IsValueType )
		{
			return false;
		}

		if ( processingTypes.Contains( t ) )
		{
			// If the type is already being processed, return to avoid infinite recursion
			return true;
		}

		processingTypes.Add( t );

		// For value types check if all properties are cloneable by copy
		foreach ( var prop in t.GetProperties() )
		{
			if ( ShouldSkipPropertyTypeCheck( prop ) )
			{
				continue;
			}

			var isCloneable = IsTypeCloneableByCopyInternal( prop.PropertyType, processingTypes );
			_isTypeCloneableByCopy[prop.PropertyType] = isCloneable;
			if ( !isCloneable )
			{
				processingTypes.Remove( t );
				return false;
			}
		}

		foreach ( var field in t.GetFields() )
		{
			if ( ShouldSkipFieldTypeCheck( field ) )
			{
				continue;
			}

			var isCloneable = IsTypeCloneableByCopyInternal( field.FieldType, processingTypes );
			_isTypeCloneableByCopy[field.FieldType] = isCloneable;
			if ( !isCloneable )
			{
				processingTypes.Remove( t );
				return false;
			}
		}

		processingTypes.Remove( t );
		return true;
	}

	private static bool ShouldSkipPropertyTypeCheck( PropertyInfo prop )
	{
		var alwaysCheck = prop.HasAttribute( typeof( JsonIncludeAttribute ) ) || prop.HasAttribute( typeof( PropertyAttribute ) );
		var ignoredByDefault = prop.HasAttribute( typeof( JsonIgnoreAttribute ) ) || !prop.CanWrite || prop.SetMethod is null || prop.SetMethod.IsPrivate || prop.SetMethod.IsVirtual || (prop.GetMethod is not null && (prop.GetMethod.IsStatic || prop.GetMethod.IsVirtual));
		return !alwaysCheck && ignoredByDefault;
	}

	private static bool ShouldSkipFieldTypeCheck( FieldInfo field )
	{
		var alwaysCheck = field.HasAttribute( typeof( JsonIncludeAttribute ) ) || field.HasAttribute( typeof( PropertyAttribute ) );
		var ignoredByDefault = field.HasAttribute( typeof( JsonIgnoreAttribute ) ) || field.IsPrivate || field.IsStatic;
		return !alwaysCheck && ignoredByDefault;
	}

	private static bool IsImmutableType( Type t )
	{
		if ( !t.IsGenericType ) return false;

		return t.GetGenericTypeDefinition() == typeof( ImmutableList<> ) || t.GetGenericTypeDefinition() == typeof( ImmutableArray<> );
	}
}
