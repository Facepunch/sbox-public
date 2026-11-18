namespace Sandbox;

/// <summary>
/// One draw of a scene object's model as the object draws it (<c>RenderTools::GetSceneObjectDraw</c>): its mesh and draw
/// index, the material after material groups and overrides, whether its mesh group is on, and its flags.
/// </summary>
internal readonly record struct SceneObjectDraw( int Mesh, int DrawCall, Material Material, bool Visible, bool Translucent, bool ShadowFastPath, bool Lightmapped );

public partial class SceneObject
{
	/// <summary>
	/// Whether this object's draws are worked out in managed code (<see cref="GetDraws"/>): one managed code made from a model
	/// that isn't a map's. Native code that made an object (a map's) may have changed its draws behind managed code's back.
	/// </summary>
	internal bool ResolvesDraws { get; private set; }

	// The material each of the model's draws has on native's mesh instances (CMeshInstance_Imp's draw data), flat across its
	// meshes and their scene objects: the model's, then material groups and mesh-instance overrides in the order they came
	Material[] _drawMaterials;

	// Per draw, whether its mesh instance has draw data of its own (CMeshInstance_Imp::SetAndOwnDrawDataOverride: an override
	// or a material group set on it), whose flags native works out from its materials. Others share the model's mesh's
	bool[] _ownDrawData;

	// Whether the override list had entries when native last worked the draws' flags out from their materials
	// (UpdateTranslucencyFlagsOnMaterialChange, UpdateDrawCallFlags' bMaterialOverride)
	bool _flagsWithOverrides;

	// The override list (CSceneObjectExtraData_t::m_materialOverrideList), in order: an attribute 0 entry overrides everything
	List<(uint Attribute, int Value, Material Material)> _overrideList;

	// Per index overrides (CSceneObjectExtraData_t::m_materialIndexOverrides), by the model's material index (Model.Materials);
	// null where an index has none
	List<Material> _indexOverrides;

	ulong? _meshGroupMask;

	static readonly StringToken TranslucentName = new( "translucent" );
	static readonly StringToken AlphaTestName = new( "alphatest" );
	static readonly StringToken ShadowFastPathName = new( "ShadowFastPath" );
	const int Missing = int.MinValue;

	// RENDERMESH_DRAW_FLAGS_USE_SHADOW_FAST_PATH, _HAS_BAKED_LIGHTING_FROM_LIGHTMAP, and IModel.GetDrawCallFlags' alpha blended bit
	const int ShadowFastPathFlag = 0x1, LightmapFlag = 0x20, AlphaBlendedFlag = unchecked((int)0x80000000);

	/// <summary>Start working out draws in managed code, for an object managed code just made from <paramref name="model"/>.</summary>
	internal void StartResolvingDraws( Model model )
	{
		ResolvesDraws = model is not null && !(model.ResourcePath?.StartsWith( "maps/", StringComparison.OrdinalIgnoreCase ) ?? false);
		_drawMaterials = null;
		_ownDrawData = null;
		_flagsWithOverrides = false;
	}

	/// <summary>
	/// The model's draws as native's mesh system lays them out, read once per model: per mesh its group mask, per draw its
	/// material and the index <c>FindModelDrawCall</c> gives it across the mesh's scene objects.
	/// </summary>
	internal sealed class ModelDraws
	{
		/// <summary>A draw: its mesh, its index in the mesh, its scene object and index there, and its default material.</summary>
		public readonly record struct Draw( int Mesh, int DrawCall, int SceneObject, int Index, ulong MeshGroupMask, Material Material );

		public Draw[] Draws;

		// The model's material groups (group 0 is its defaults), and each default material's index in them
		public Material[][] Groups;
		public Dictionary<string, int> DefaultIndex;

		/// <summary>
		/// The meshes' translucency together, as <c>CMeshSystem::CreateMeshInstanceChain</c> combines each mesh's compiled type:
		/// any partly blended mesh, or opaque and blended meshes together, make it partly blended.
		/// </summary>
		public TranslucencyType Translucency;

		/// <summary>Per scene object of a mesh (a mesh instance), in draw order: its mesh group mask and local bounds.</summary>
		public (ulong MeshGroupMask, BBox Bounds)[] Instances;

		static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Model, ModelDraws> cache = new();

		public static ModelDraws For( Model model ) => cache.GetValue( model, Read );

		static ModelDraws Read( Model model )
		{
			var native = model.native;
			var draws = new List<Draw>();
			var instances = new List<(ulong, BBox)>();
			var translucency = (TranslucencyType)(-1);
			for ( int m = 0; m < native.GetNumMeshes(); m++ )
			{
				native.GetMeshInfo( m, out _, out _, out _, out _, out var meshTranslucency, out _, out _ );
				var type = (TranslucencyType)meshTranslucency;
				if ( (int)translucency == -1 ) translucency = type;
				else if ( type == TranslucencyType.Partial || (type != translucency) ) translucency = TranslucencyType.Partial;

				var mask = native.GetMeshGroupMaskForMesh( m );
				var index = 0;
				for ( int s = 0; s < native.GetNumSceneObjects( m ); s++ )
				{
					native.GetSceneObjectBounds( m, s, out var mins, out var maxs );
					instances.Add( (mask, new BBox( mins, maxs )) );
					for ( int d = 0; d < native.GetNumDrawCalls( m, s ); d++ )
						draws.Add( new Draw( m, index++, s, d, mask, Material.FromNative( native.GetMaterial( m, s, d ) ) ) );
				}
			}

			var groups = new Material[native.GetNumMaterialGroups()][];
			for ( int g = 0; g < groups.Length; g++ )
			{
				groups[g] = new Material[native.GetNumMaterialsInGroup( g )];
				for ( int i = 0; i < groups[g].Length; i++ ) groups[g][i] = Material.FromNative( native.GetMaterialInGroup( g, i ) );
			}

			var defaults = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
			if ( groups.Length > 0 )
			{
				for ( int i = 0; i < groups[0].Length; i++ )
				{
					if ( groups[0][i]?.Name is { } name ) defaults.TryAdd( name, i );
				}
			}

			return new ModelDraws
			{
				Draws = draws.ToArray(),
				Groups = groups,
				DefaultIndex = defaults,
				Translucency = (int)translucency == -1 ? TranslucencyType.Opaque : translucency,
				Instances = instances.ToArray(),
			};
		}
	}

	Material[] DrawMaterials( ModelDraws model )
	{
		if ( _drawMaterials is { } materials && materials.Length == model.Draws.Length ) return materials;
		_ownDrawData = new bool[model.Draws.Length];
		_drawMaterials = new Material[model.Draws.Length];
		for ( int i = 0; i < _drawMaterials.Length; i++ ) _drawMaterials[i] = model.Draws[i].Material;
		return _drawMaterials;
	}

	/// <summary>The mesh-instance override (<c>SetMaterialOverrideForMeshInstances</c>): every draw's material, or the model's again for null.</summary>
	void OverrideMeshInstances( Material material )
	{
		if ( !ResolvesDraws || Model is not { } model ) return;
		var draws = ModelDraws.For( model );
		var materials = DrawMaterials( draws );
		for ( int i = 0; i < materials.Length; i++ ) materials[i] = material ?? draws.Draws[i].Material;

		// Setting or resetting overrides gives every mesh instance draw data of its own
		Array.Fill( _ownDrawData, true );
		FlagsRecomputed();
	}

	/// <summary>
	/// A material group (<c>CMeshInstance_Imp::ApplyMaterialGroupOverride</c>): each draw's default replaced by the group's.
	/// False for a group the model doesn't have, which native ignores.
	/// </summary>
	bool ApplyMaterialGroup( string name )
	{
		if ( Model is not { } model ) return false;
		var group = model.GetMaterialGroupIndex( name );
		if ( group < 0 ) return false;
		if ( !ResolvesDraws ) return true;

		var draws = ModelDraws.For( model );
		var materials = DrawMaterials( draws );

		// Mesh instance by mesh instance (a scene object of a mesh): one whose draws any replacement changes takes draw data of
		// its own with every replacement in it
		for ( int first = 0; first < materials.Length; )
		{
			var end = first;
			while ( end < materials.Length && draws.Draws[end].Mesh == draws.Draws[first].Mesh && draws.Draws[end].SceneObject == draws.Draws[first].SceneObject ) end++;

			var changes = false;
			for ( int i = first; i < end; i++ ) changes |= Replacement( draws, group, i ) is { } replacement && replacement != materials[i];

			if ( changes )
			{
				for ( int i = first; i < end; i++ )
				{
					if ( Replacement( draws, group, i ) is { } replacement ) materials[i] = replacement;
					_ownDrawData[i] = true;
				}
			}

			first = end;
		}

		FlagsRecomputed();
		return true;
	}

	/// <summary>A draw's material in a material group (<c>CModel::GetMaterialReplacementForGroup</c>), or null.</summary>
	static Material Replacement( ModelDraws draws, int group, int draw )
	{
		if ( draws.Draws[draw].Material?.Name is not { } original || !draws.DefaultIndex.TryGetValue( original, out var index ) ) return null;
		if ( group >= draws.Groups.Length || index >= draws.Groups[group].Length ) return null;
		return draws.Groups[group][index];
	}

	/// <summary>An override list entry (<c>CSceneSystem::SceneObject_SetMaterialOverride</c>): replaced, added, or removed for null.</summary>
	void SetOverrideEntry( Material material, uint attribute, int value )
	{
		_overrideList ??= new();
		var at = _overrideList.FindIndex( x => x.Attribute == attribute && x.Value == value );
		if ( material is null ) _overrideList.RemoveAll( x => x.Attribute == attribute && x.Value == value );
		else if ( at >= 0 ) _overrideList[at] = (attribute, value, material);
		else _overrideList.Add( (attribute, value, material) );
		FlagsRecomputed();
	}

	/// <summary>A per index override (<c>CSceneObject::SetMaterialOverrideByIndex</c>): null for none.</summary>
	void SetIndexOverride( int index, Material material )
	{
		if ( index < 0 ) return;
		_indexOverrides ??= new();
		while ( _indexOverrides.Count <= index ) _indexOverrides.Add( null );
		_indexOverrides[index] = material;
		FlagsRecomputed();
	}

	void FlagsRecomputed()
	{
		_flagsWithOverrides = _overrideList is { Count: > 0 };
	}

	/// <summary>
	/// The per index override for a draw's material (<c>GetMaterialOverride( pLayer, pMaterial )</c>): the entry at the material's
	/// index in the model's materials (<c>CModel::GetMaterialIndex</c>), or null.
	/// </summary>
	Material IndexOverride( Model model, Material material )
	{
		if ( material is null || _indexOverrides is not { Count: > 0 } ) return null;
		var materials = model.Materials;
		for ( int i = 0; i < materials.Length && i < _indexOverrides.Count; i++ )
		{
			if ( ReferenceEquals( materials[i], material ) ) return _indexOverrides[i];
		}

		return null;
	}

	/// <summary>
	/// The object's draws as native draws them (<c>RenderTools::GetSceneObjectDraw</c> over native's mesh instances), worked out
	/// from the model and the object's mesh groups, material group and overrides.
	/// </summary>
	internal void GetDraws( List<SceneObjectDraw> into )
	{
		into.Clear();
		if ( Model is not { } model || model.native.IsNull ) return;

		var draws = ModelDraws.For( model );
		var materials = DrawMaterials( draws );
		var mask = MeshGroupMask;

		Material whole = null;
		if ( _overrideList is not null )
		{
			foreach ( var entry in _overrideList )
			{
				if ( entry.Attribute != 0 ) continue;
				whole = entry.Material;
				break;
			}
		}

		for ( int i = 0; i < draws.Draws.Length; i++ )
		{
			var draw = draws.Draws[i];
			var material = materials[i];

			// The draw's flags: worked out from its material (UpdateDrawCallFlags) when its mesh instance has draw data of its
			// own, else the model's mesh's. Those are read each time: native works them out again in place, for every object
			// drawing the mesh, when one whose instances share it changes material (and a mesh loaded without a device keeps
			// those it was compiled with)
			var flags = model.native.GetDrawCallFlags( draw.Mesh, draw.SceneObject, draw.Index );
			var translucent = (flags & AlphaBlendedFlag) != 0;
			var fastPath = (flags & ShadowFastPathFlag) != 0;
			if ( _ownDrawData[i] && material is not null )
			{
				translucent = material.native.GetIntAttributeOrDefault( TranslucentName, 0 ) != 0;
				var alphaTest = material.native.GetIntAttributeOrDefault( AlphaTestName, 0 ) != 0;
				fastPath = material.native.GetBoolAttributeOrDefault( ShadowFastPathName, false ) && !_flagsWithOverrides && !translucent && !alphaTest;
			}

			var lightmapped = (flags & LightmapFlag) != 0;
			var drawn = whole ?? IndexOverride( model, material ) ?? ListOverride( material ) ?? material;
			into.Add( new SceneObjectDraw( draw.Mesh, draw.DrawCall, drawn, (draw.MeshGroupMask & mask) != 0, translucent, fastPath, lightmapped ) );
		}
	}

	/// <summary>The override list's entry for a draw's material: the last whose attribute the material has with the entry's value.</summary>
	Material ListOverride( Material material )
	{
		if ( material is null || _overrideList is null ) return null;
		for ( int i = _overrideList.Count - 1; i >= 0; i-- )
		{
			var entry = _overrideList[i];
			if ( entry.Material is null ) continue;
			// Native's GetIntAttribute: a material without the attribute matches nothing
			if ( material.native.GetIntAttributeOrDefault( new StringToken( entry.Attribute ), Missing ) is var value && value != Missing && value == entry.Value ) return entry.Material;
		}

		return null;
	}
}
