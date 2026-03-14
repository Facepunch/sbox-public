using System.Linq.Expressions;
using System.Text.Json.Nodes;
using Facepunch.ActionGraphs;

namespace Sandbox;

public abstract partial class Component
{
	// Set only during the cloning process
	// We store this on the component to avoid the need reverse lookup table during the clone process
	private Component _cloneOriginal = null;

	/// <summary>
	/// Runs after this clone has been created by a cloned GameObject.
	/// </summary>
	/// <param name="original">The original component that was cloned.</param>
	/// <param name="originalToClonedObject">During the cloning process, we build a mapping from original objects to their clone, so we will need to add ourselves to it.</param>
	internal void InitClone( Component original, Dictionary<object, object> originalToClonedObject )
	{
		originalToClonedObject[original] = this;
		_cloneOriginal = original;
	}

	/// <summary>
	/// Runs after all objects of the original hierarchy have been cloned/created.
	/// Here we initialize the clones properties and fields with the values from the original object.
	/// <param name="originalToClonedObject">A mapping of original objects to their clones, used for all reference types.</param>
	/// <param name="originalIdToCloneId">A mapping of original GUIDs to cloned GUIDs, used for GameObject and Component references in JSON.</param>
	/// </summary>
	internal void PostClone( Dictionary<object, object> originalToClonedObject, Dictionary<Guid, Guid> originalIdToCloneId )
	{
		if ( !_cloneOriginal.IsValid() )
		{
			// Nothing todo this component is not a proper clone. It was created through side effects while cloning properties.
			return;
		}

		using var targetScope = ActionGraph.PushTarget( InputDefinition.Target( typeof( GameObject ), GameObject ) );

		ClonePropertiesAndFields( _cloneOriginal, originalToClonedObject, originalIdToCloneId );

		CheckRequireComponent();

		_cloneOriginal = null;
	}

	private void ClonePropertiesAndFields( object original, Dictionary<object, object> originalToClonedObject, Dictionary<Guid, Guid> originalIdToCloneId )
	{
		foreach ( var member in ReflectionQueryCache.OrderedSerializableMembers( GetType() ) )
		{
			CloneHelpers.CloneMember(
				this,
				original,
				member,
				originalToClonedObject,
				originalIdToCloneId );
		}
	}
}

/// <summary>
/// Provides helper methods for cloning objects and their members.
/// We use a heuristic <see cref="ReflectionQueryCache.IsTypeCloneableByCopy"/> to determine if a type can be cloned by copy to speed up cloning.
/// If we cannot copy something and we have to "clone" we do so by serializing to and deserializing from JSON.
/// However, our goal is to copy as much as possible to avoid the serialization overhead.
/// </summary>
internal static class CloneHelpers
{
	public static void CloneMember(
		object target,
		object original,
		MemberDescription member,
		Dictionary<object, object> originalToClonedObject,
		Dictionary<Guid, Guid> originalIdToCloneId )
	{
		object originalValue = null;
		Type valueType = null;

		if ( member is PropertyDescription prop )
		{
			valueType = prop.PropertyType;

			// Fast path: value types that are safe to copy by value are transferred via a pre-compiled
			// delegate, avoiding the boxing allocation that PropertyInfo.GetValue would cause.
			if ( valueType.IsValueType && ReflectionQueryCache.IsTypeCloneableByCopy( valueType ) )
			{
				MemberCopyCache.CopyTo( prop, original, target );
				return;
			}

			originalValue = prop.GetValue( original );
		}
		else if ( member is FieldDescription field )
		{
			valueType = field.FieldType;

			// Fast path: same as above for fields.
			if ( valueType.IsValueType && ReflectionQueryCache.IsTypeCloneableByCopy( valueType ) )
			{
				MemberCopyCache.CopyTo( field, original, target );
				return;
			}

			originalValue = field.GetValue( original );
		}
		else
		{
			throw new InvalidOperationException( "Member is neither a property nor a field" );
		}

		if ( originalValue is null || ReflectionQueryCache.IsTypeCloneableByCopy( valueType ) )
		{
			SetMemberValue( member, target, originalValue );
			return;
		}

		// If the original object has already been cloned simply point to it.
		// For now only do this for Component and GameObjects ( matches original clone via JSON behaviour )
		var isGameObjectorComponent = (originalValue is GameObject || originalValue is Component);
		// There is an ambiguity when we reference the root of the prefab, it could either mean we want to reference the cloned root or the original prefab.
		// To maintain old clone behaviour we reference the cloned root gameobject except when the cloned property is of type PrefabScene, in that case we reference the original prefab.
		var isPrefabReference = originalValue is PrefabScene && valueType == typeof( PrefabScene );
		if ( isGameObjectorComponent && !isPrefabReference && originalToClonedObject.TryGetValue( originalValue, out var existingClone ) )
		{
			SetMemberValue( member, target, existingClone );
			return;
		}

		// Fast path: if the type implements ICloneable and is not a BCL/system type (whose Clone()
		// is only a shallow copy that would silently skip GUID rewiring), call Clone() directly
		// to avoid the JSON roundtrip overhead.
		if ( originalValue is ICloneable cloneable && ReflectionQueryCache.IsICloneableSafe( valueType ) )
		{
			SetMemberValue( member, target, cloneable.Clone() );
			return;
		}

		// Fallback to JSON
		var clonedJson = Json.ToNode( originalValue, valueType );
		UpdateClonedIdsInJson( clonedJson, originalIdToCloneId );

		var targetValue = member is PropertyDescription ? ((PropertyDescription)member).GetValue( target ) : ((FieldDescription)member).GetValue( target );

		if ( targetValue is IJsonPopulator jsonPopulator )
		{
			if ( targetValue == null )
				targetValue = Activator.CreateInstance( valueType );

			jsonPopulator.Deserialize( clonedJson );

			SetMemberValue( member, target, targetValue );
		}
		else
		{
			var clonedValue = Json.FromNode( clonedJson, valueType );

			SetMemberValue( member, target, clonedValue );
		}
	}

	private static void SetMemberValue(
		MemberDescription member,
		object target,
		object value )
	{
		if ( member is PropertyDescription prop )
		{
			prop.SetValue( target, value );
		}
		else if ( member is FieldDescription field )
		{
			field.SetValue( target, value );
		}
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
/// Caches pre-compiled expression-tree delegates that copy a single member's value directly
/// from source to target without boxing. Only used for value types that are safe to clone by copy.
/// Cleared via <see cref="ReflectionQueryCache.ClearTypeCache"/> after hotload and game close.
/// </summary>
internal static class MemberCopyCache
{
	// Compiled expression delegates come from cant be upgraded via hot upload -> skip.
	// The cache is still cleared explicitly via ReflectionQueryCache.ClearTypeCache() during hotload.
	[SkipHotload]
	private static readonly Dictionary<MemberDescription, Action<object, object>> _cache = new();

	internal static void Clear() => _cache.Clear();

	internal static void CopyTo( PropertyDescription prop, object source, object target )
	{
		if ( !_cache.TryGetValue( prop, out var del ) )
		{
			del = BuildPropertyDelegate( prop );
			_cache[prop] = del;
		}

		del( source, target );
	}

	internal static void CopyTo( FieldDescription field, object source, object target )
	{
		if ( !_cache.TryGetValue( field, out var del ) )
		{
			del = BuildFieldDelegate( field );
			_cache[field] = del;
		}

		del( source, target );
	}

	private static Action<object, object> BuildPropertyDelegate( PropertyDescription prop )
	{
		var propInfo = prop.PropertyInfo;
		var declaringType = propInfo.DeclaringType;

		if ( propInfo.SetMethod is null )
			return static ( _, _ ) => { };

		// Mirror the same access guard as PropertyDescription.SetValue:
		// engine types must not write to non-public or init-only setters.
		if ( !prop.TypeDescription.IsDynamicAssembly && (!prop.IsSetMethodPublic || prop.IsSetMethodInitOnly) )
			return static ( _, _ ) => { };

		var sourceParam = Expression.Parameter( typeof( object ), "source" );
		var targetParam = Expression.Parameter( typeof( object ), "target" );

		var getExpr = Expression.Property( Expression.Convert( sourceParam, declaringType ), propInfo );
		var setExpr = Expression.Call( Expression.Convert( targetParam, declaringType ), propInfo.SetMethod, getExpr );

		return Expression.Lambda<Action<object, object>>( setExpr, sourceParam, targetParam ).Compile();
	}

	private static Action<object, object> BuildFieldDelegate( FieldDescription field )
	{
		var fieldInfo = field.FieldInfo;
		var declaringType = fieldInfo.DeclaringType;

		var sourceParam = Expression.Parameter( typeof( object ), "source" );
		var targetParam = Expression.Parameter( typeof( object ), "target" );

		var getExpr = Expression.Field( Expression.Convert( sourceParam, declaringType ), fieldInfo );
		var setExpr = Expression.Assign( Expression.Field( Expression.Convert( targetParam, declaringType ), fieldInfo ), getExpr );

		return Expression.Lambda<Action<object, object>>( setExpr, sourceParam, targetParam ).Compile();
	}
}
