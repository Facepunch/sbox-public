using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Sandbox;

internal static class CloneHelpers
{
	/// <summary>
	/// Copies plain collections of scalars or references already mapped to clones. All other
	/// cases use JSON, preserving converters, GUID rewiring and component fallback resolution.
	/// </summary>
	public static bool TryCloneCollection( object source, Type type, CloneContext context, out object clone )
	{
		clone = null;
		if ( source.GetType() != type ) return false;

		Type elementType;
		if ( type.IsSZArray )
		{
			elementType = type.GetElementType();
		}
		else if ( type.IsGenericType && type.GetGenericTypeDefinition() == typeof( List<> ) )
		{
			elementType = type.GetGenericArguments()[0];
		}
		else if ( type.IsGenericType && type.GetGenericTypeDefinition() == typeof( Dictionary<,> ) )
		{
			var args = type.GetGenericArguments();
			// JSON dictionary keys are property names, so GUID rewriting doesn't touch them.
			if ( args[0] != typeof( string ) && !IsCollectionScalar( args[0] ) ) return false;
			elementType = args[1];
		}
		else
		{
			return false;
		}

		var remapReferences = elementType.IsAssignableTo( typeof( GameObject ) ) || elementType.IsAssignableTo( typeof( Component ) );
		if ( !remapReferences && !IsCollectionScalar( elementType ) ) return false;

		if ( source is Array array )
		{
			var copy = (Array)array.Clone();
			if ( remapReferences )
			{
				for ( int i = 0; i < copy.Length; i++ )
				{
					if ( !TryRemapReference( copy.GetValue( i ), context, out var value ) ) return false;
					copy.SetValue( value, i );
				}
			}
			clone = copy;
			return true;
		}

		if ( source is IDictionary dictionary )
		{
			var copy = (IDictionary)Activator.CreateInstance( type );
			foreach ( DictionaryEntry entry in dictionary )
			{
				var value = entry.Value;
				if ( remapReferences && !TryRemapReference( value, context, out value ) ) return false;
				copy.Add( entry.Key, value );
			}
			clone = copy;
			return true;
		}

		var list = (IList)source;
		var listCopy = (IList)Activator.CreateInstance( type );
		for ( int i = 0; i < list.Count; i++ )
		{
			var value = list[i];
			if ( remapReferences && !TryRemapReference( value, context, out value ) ) return false;
			listCopy.Add( value );
		}
		clone = listCopy;
		return true;
	}

	// Copyability of a component member does not imply equivalence with a JSON roundtrip.
	// In particular, strings and GUIDs can be rewritten, and structs/resources have serialization rules.
	private static bool IsCollectionScalar( Type type ) =>
		(type.IsPrimitive && type != typeof( IntPtr ) && type != typeof( UIntPtr )) || type == typeof( decimal );

	private static bool TryRemapReference( object original, CloneContext context, out object clone )
	{
		clone = null;
		if ( original is null ) return true;

		// Use the existing path resolver for prefab roots, just as JSON serialization does.
		if ( original is PrefabScene prefab && prefab.Scene != Game.ActiveScene )
		{
			clone = GameObjectReference.FromInstance( prefab ).Resolve( Game.ActiveScene, warn: true );
			return true;
		}

		// Invalid and unmapped references need the JSON resolver's validity and fallback rules.
		if ( original is GameObject go && !go.IsValid ) return false;
		if ( original is Component component && !component.IsValid ) return false;
		return context.OriginalToClone.TryGetValue( original, out clone );
	}

	/// <summary>
	/// We want GUIDS that reference something within the original hierarchy to reference the corresponding clone in the new hierarchy.
	/// </summary>
	public static void UpdateClonedIdsInJson( in JsonNode json, Dictionary<Guid, Guid> originalIdToCloneId )
	{
		Sandbox.Json.WalkJsonTree( json, ( k, v ) =>
		{
			if ( !v.TryGetValue<Guid>( out var guid ) ) return v;
			if ( !originalIdToCloneId.TryGetValue( guid, out var updatedGuid ) ) return v;

			return updatedGuid;
		} );
	}
}

/// <summary>
/// Shared state of one GameObject.Clone call.
/// </summary>
internal sealed class CloneContext
{
	/// <summary>
	/// A mapping of original objects to their clones, used for all reference types.
	/// </summary>
	public readonly Dictionary<object, object> OriginalToClone;

	private Dictionary<Guid, Guid> _originalIdToCloneId;

	public GameObject Root { get; }
	public bool IsCloningPrefab => Root is PrefabScene;

	public CloneContext( Dictionary<object, object> originalToClone, GameObject root = null )
	{
		OriginalToClone = originalToClone;
		Root = root;
	}

	/// <summary>
	/// A mapping of original GUIDs to cloned GUIDs, used for GameObject and Component references in JSON and prefab lookups.
	/// Built on first use, most clones never need it.
	/// </summary>
	public Dictionary<Guid, Guid> OriginalIdToCloneId
	{
		get
		{
			if ( _originalIdToCloneId is not null )
				return _originalIdToCloneId;

			var map = new Dictionary<Guid, Guid>( OriginalToClone.Count );
			foreach ( var (original, cloned) in OriginalToClone )
			{
				if ( original is GameObject go )
					map[go.Id] = ((GameObject)cloned).Id;
				else if ( original is Component comp )
					map[comp.Id] = ((Component)cloned).Id;
			}

			_originalIdToCloneId = map;
			return map;
		}
	}
}

/// <summary>
/// Clones one serialized member of a component type. Everything that only depends on the member (how it is
/// read and written, whether its type can be copied) is decided once here, see <see cref="ReflectionQueryCache.ClonePlan"/>.
/// We use a heuristic <see cref="ReflectionQueryCache.IsTypeCloneableByCopy"/> to determine if a type can be cloned by copy.
/// If we cannot copy something we rewire references, rebuild plain collections, or as a last resort roundtrip through JSON.
/// </summary>
internal sealed class MemberCloner
{
	private readonly Type _type;
	private readonly bool _copyable;
	private readonly bool _cloneableSafe;

	// Value types that are safe to copy go straight from source to target without boxing.
	private readonly Action<object, object> _copyValue;

	// Everything else is read boxed, and written only if the member has a usable setter.
	private readonly Func<object, object> _get;
	private readonly Action<object, object> _set;

	public MemberCloner( MemberDescription member )
	{
		_type = member switch
		{
			PropertyDescription p => p.PropertyType,
			FieldDescription f => f.FieldType,
			_ => throw new InvalidOperationException( "Member is neither a property nor a field" )
		};

		var writable = IsWritable( member );
		_copyable = ReflectionQueryCache.IsTypeCloneableByCopy( _type );

		if ( _type.IsValueType && _copyable )
		{
			_copyValue = writable ? BuildCopy( member ) : static ( _, _ ) => { };
			return;
		}

		_cloneableSafe = ReflectionQueryCache.IsICloneableSafe( _type );
		_get = BuildGetter( member );
		_set = writable ? BuildSetter( member ) : null;
	}

	public void Clone( object target, object original, CloneContext context )
	{
		if ( _copyValue is not null )
		{
			_copyValue( original, target );
			return;
		}

		var value = _get( original );

		if ( value is null || _copyable )
		{
			// Embedded resources are deep-copied to carry any inline generator data over, only when in the editor.
			if ( !Application.IsEditor || !ReflectionQueryCache.IsInlineEmbeddedResource( value, _type ) )
			{
				_set?.Invoke( target, value );
				return;
			}
		}

		// If the original object has already been cloned simply point to it.
		// For now only do this for Component and GameObjects ( matches original clone via JSON behaviour )
		var isGameObjectOrComponent = value is GameObject || value is Component;
		// There is an ambiguity when we reference the root of the prefab, it could either mean we want to reference the cloned root or the original prefab.
		// To maintain old clone behaviour we reference the cloned root gameobject except when the cloned property is of type PrefabScene, in that case we reference the original prefab.
		var isPrefabReference = value is PrefabScene && _type == typeof( PrefabScene );
		if ( isGameObjectOrComponent && !isPrefabReference && context.OriginalToClone.TryGetValue( value, out var existingClone ) )
		{
			_set?.Invoke( target, existingClone );
			return;
		}

		if ( _set is not null )
		{
			// Types with their own Clone() (not a BCL shallow copy that would skip GUID rewiring) clone directly.
			if ( _cloneableSafe && value is ICloneable cloneable )
			{
				_set( target, cloneable.Clone() );
				return;
			}

			// Plain collections of copyable values or GameObject/Component references are rebuilt directly,
			// producing what the JSON roundtrip would without the serializer.
			if ( CloneHelpers.TryCloneCollection( value, _type, context, out var clonedCollection ) )
			{
				_set( target, clonedCollection );
				return;
			}
		}

		// Fallback to JSON. A get-only member can only receive the data if it holds a populator.
		var targetValue = _get( target );
		if ( _set is null && targetValue is not IJsonPopulator )
			return;

		var clonedJson = Json.ToNode( value, _type );
		CloneHelpers.UpdateClonedIdsInJson( clonedJson, context.OriginalIdToCloneId );

		if ( targetValue is IJsonPopulator jsonPopulator )
		{
			jsonPopulator.Deserialize( clonedJson );
			_set?.Invoke( target, targetValue );
		}
		else
		{
			_set( target, Json.FromNode( clonedJson, _type ) );
		}
	}

	/// <summary>
	/// Mirrors the setter guard in <see cref="PropertyDescription.SetValue"/>: engine types must not write to non-public or init-only setters.
	/// Fields are always writable, reflection writes readonly fields too.
	/// </summary>
	private static bool IsWritable( MemberDescription member )
	{
		if ( member is not PropertyDescription prop )
			return true;

		if ( prop.PropertyInfo.SetMethod is null )
			return false;

		if ( !prop.TypeDescription.IsDynamicAssembly && (!prop.IsSetMethodPublic || prop.IsSetMethodInitOnly) )
			return false;

		return true;
	}

	private static MemberExpression Access( MemberDescription member, Expression instance )
	{
		if ( member is PropertyDescription prop )
			return Expression.Property( Expression.Convert( instance, prop.PropertyInfo.DeclaringType ), prop.PropertyInfo );

		var fieldInfo = ((FieldDescription)member).FieldInfo;
		return Expression.Field( Expression.Convert( instance, fieldInfo.DeclaringType ), fieldInfo );
	}

	// Expression trees can't assign readonly fields, reflection can.
	private static FieldInfo ReadOnlyField( MemberDescription member ) => member is FieldDescription { IsInitOnly: true } field ? field.FieldInfo : null;

	private static Action<object, object> BuildCopy( MemberDescription member )
	{
		if ( ReadOnlyField( member ) is { } readOnlyField )
			return ( source, target ) => readOnlyField.SetValue( target, readOnlyField.GetValue( source ) );

		var source = Expression.Parameter( typeof( object ), "source" );
		var target = Expression.Parameter( typeof( object ), "target" );
		var body = Expression.Assign( Access( member, target ), Access( member, source ) );
		return Expression.Lambda<Action<object, object>>( body, source, target ).Compile();
	}

	private static Func<object, object> BuildGetter( MemberDescription member )
	{
		var instance = Expression.Parameter( typeof( object ), "instance" );
		var body = Expression.Convert( Access( member, instance ), typeof( object ) );
		return Expression.Lambda<Func<object, object>>( body, instance ).Compile();
	}

	private static Action<object, object> BuildSetter( MemberDescription member )
	{
		if ( ReadOnlyField( member ) is { } readOnlyField )
			return readOnlyField.SetValue;

		var instance = Expression.Parameter( typeof( object ), "instance" );
		var value = Expression.Parameter( typeof( object ), "value" );
		var access = Access( member, instance );
		var body = Expression.Assign( access, Expression.Convert( value, access.Type ) );
		return Expression.Lambda<Action<object, object>>( body, instance, value ).Compile();
	}
}
