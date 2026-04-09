using System.Text.Json.Nodes;

namespace Sandbox.Engine;

/// <summary>
/// A wrapper to allow the unification of editing materials. This is usually a member on a Component which implements MaterialAccessor.ITarget.
/// </summary>
/// <example>
/// <code>
/// MaterialAccessor materialAccessor;
///
///	[Property]
///	public MaterialAccessor Materials => materialAccessor ??= new MaterialAccessor( this );
/// </code>
/// </example>
public sealed class MaterialAccessor : IJsonPopulator
{
	readonly ITarget _target;
	readonly Dictionary<int, Material> _overrides = new();

	/// <summary>
	/// The target of a MaterialAccessor. This is the object that will be modified when setting or clearing material overrides.
	/// </summary>
	public interface ITarget
	{
		/// <summary>
		/// Return true if this target is valid
		/// </summary>
		public bool IsValid { get; }

		/// <summary>
		/// The number of materials on this target
		/// </summary>
		/// <returns></returns>
		int GetMaterialCount();

		/// <summary>
		/// Get the original material, before overrides, matching this index
		/// </summary>
		Material Get( int index );

		/// <summary>
		/// Set the override material for this index.
		/// </summary>
		void SetOverride( int index, Material material );

		/// <summary>
		/// Wipe all overrides
		/// </summary>
		void ClearOverrides();
	}

	/// <summary>
	/// Create a new material accessor for this object. 
	/// </summary>
	public MaterialAccessor( ITarget renderer )
	{
		_target = renderer;
	}

	/// <summary>
	/// Total number of material slots
	/// </summary>
	public int Count
	{
		get => _target.GetMaterialCount();
	}

	/// <summary>
	/// Get the original material for the specified index.
	/// </summary>
	public Material GetOriginal( int i ) => _target.Get( i );

	/// <summary>
	/// Does this index have an override material?
	/// </summary>
	public bool HasOverride( int i ) => _overrides.ContainsKey( i );

	/// <summary>
	/// Get the override material for this slot. Or null if not set.
	/// </summary>
	public Material GetOverride( int i )
	{
		return _overrides.GetValueOrDefault( i );
	}

	/// <summary>
	/// Set an override material for this slot. If the material is null, it will clear the override.
	/// </summary>
	public void SetOverride( int i, Material material )
	{
		if ( material is null )
		{
			if ( !_overrides.Remove( i ) )
				return;
		}
		else
		{
			_overrides[i] = material;
		}

		_target.SetOverride( i, material );
	}


	JsonNode IJsonPopulator.Serialize()
	{
		if ( _overrides.Count == 0 )
			return null;

		var obj = new JsonObject();

		var materialsByIndex = new JsonObject();

		foreach ( var value in _overrides )
		{
			materialsByIndex.Add( value.Key.ToString(), Json.ToNode( value.Value ) );
		}

		obj["indexed"] = materialsByIndex;
		return obj;
	}

	void IJsonPopulator.Deserialize( JsonNode e )
	{
		// Clear all existing overrides before applying new ones,
		_overrides.Clear();
		_target.ClearOverrides();

		if ( e is not JsonObject jso )
		{
			return;
		}

		if ( jso.TryGetPropertyValue( "indexed", out var indexedMaterials ) && indexedMaterials is JsonObject indexedMaterialsObj )
		{
			foreach ( var o in indexedMaterialsObj )
			{
				int index = o.Key.ToInt( -1 );
				if ( index < 0 ) continue;

				SetOverride( index, Json.FromNode<Material>( o.Value ) );
			}
		}
	}

	/// <summary>
	/// Apply to the object. You don't need to call this when setting overrides, as it will automatically apply them to the target when you set them.
	/// This is here as a convenience if this object holds data, and you need to apply it to another object that didn't exist when the
	/// overrides were originally set, or loaded.
	/// </summary>
	public void Apply()
	{
		foreach ( var value in _overrides )
		{
			_target.SetOverride( value.Key, value.Value );
		}
	}
}
