using ObjectFlags = Sandbox.Rendering.SceneObjectFlags;

namespace Sandbox;

public partial class SceneObject
{
	/// <summary>A mesh's or object's translucency (<c>RenderMeshTranslucencyType_t</c>).</summary>
	internal enum TranslucencyType
	{
		Opaque = 0,
		Partial = 1,
		Full = 2,
	}

	/// <summary>Native's internal flags (<c>SCENEOBJECTFLAG_INTERNAL_MASK</c>: dirty update, disabled, loaded, deleted), not kept here.</summary>
	internal const ObjectFlags InternalFlags = (ObjectFlags)0xF800000000000000;

	// SCENEOBJECTFLAG_IS_DISABLED, the internal flag RenderingEnabled is natively
	const ObjectFlags DisabledFlag = (ObjectFlags)(1UL << 60);

	// The flags a material change works out again: TRANSLUCENCY_RELATED_MESH_FLAGS, and SET_OF_SCENEOBJECT_FLAGS_CONTROLLED_BY_MATERIAL
	const ObjectFlags TranslucencyFlags = ObjectFlags.IsOpaque | ObjectFlags.MaterialSupportsShadows | ObjectFlags.IsTranslucent;
	const ObjectFlags MaterialFlags = ObjectFlags.MaterialSupportsShadows | ObjectFlags.WantsFrameBufferCopyTexture | ObjectFlags.EnvironmentMapped
		| ObjectFlags.ForwardLayerOnly | ObjectFlags.NeedsLightProbe | ObjectFlags.NoZPrepass;

	// RENDERMESH_DRAW_FLAGS_HAS_BAKED_LIGHTING
	const int BakedLightingFlags = 0x10 | 0x20;

	ObjectFlags? _flags;

	// The flags native's mesh instances were made with, together (m_nOriginalSceneObjectFlags), which a material change works
	// the translucency flags out from
	ObjectFlags _instanceFlags;

	/// <summary>
	/// The object's flags without native's internal ones. Kept here: read once from native (what native code made the object
	/// with, or worked out at creation), then set through <see cref="Flags"/> and worked out again here when materials change
	/// (for an object whose draws are worked out here, <see cref="ResolvesDraws"/>; others read native again).
	/// </summary>
	internal ObjectFlags CurrentFlags => _flags ??= native.IsNull ? ObjectFlags.None : native.GetFlags() & ~InternalFlags;

	/// <summary>
	/// Whether it's drawn into shadow maps: both shadow flags, as native's shadow layers require them
	/// (<see cref="ObjectFlags.CastShadows"/> is enabled and material-supported; a tool's helper model has only the second).
	/// </summary>
	internal bool CastsShadows => (CurrentFlags & ObjectFlags.CastShadows) == ObjectFlags.CastShadows;

	// SceneObjectFlagAccessor.CastShadowsTwoSided: managed only, native has no such flag
	bool _castShadowsTwoSided;

	/// <summary>Change flags in <paramref name="mask"/> to <paramref name="value"/>'s, here and on a native object native code made.</summary>
	internal void ChangeFlags( ObjectFlags value, ObjectFlags mask )
	{
		value &= mask;

		// Unchanged: nothing to write or tell the renderer (a water sets its capture list, and with it these, every frame).
		// Internal flags aren't kept here, so a change to one always goes through
		if ( (mask & InternalFlags) == 0 && (CurrentFlags & mask) == value ) return;

		if ( WritesNativeObject( "ChangeFlags" ) ) native.ChangeFlags( value, mask );
		_flags = (CurrentFlags & ~mask) | (value & ~InternalFlags);
		NotifyChanged( Rendering.SceneObjectChange.Flags );
	}

	static readonly StringToken WantsFrameBufferCopyName = new( "bWantsFBCopyTexture" );
	static readonly StringToken EnvironmentMappedName = new( "environmentmapped" );
	static readonly StringToken ForwardLayerOnlyName = new( "ForwardLayerOnly" );
	static readonly StringToken NoZPrepassName = new( "NoZPrepass" );
	static readonly StringToken DoNotCastShadowsName = new( "DoNotCastShadows" );
	static readonly StringToken NeedsLightProbeName = new( "NeedsLightProbe" );
	static readonly StringToken UsesBakedLightingFromProbeName = new( "UsesBakedLightingFromProbe" );
	static readonly StringToken OverlayName = new( "overlay" );
	static readonly uint StaticOverlayLayerToken = new StringToken( "StaticOverlayLayer" ).Value;

	/// <summary>
	/// <c>CMeshSystem::ComputeSceneObjectTranslucencyFlags</c>: the opaque and translucent flags for a translucency, left alone
	/// for a shadow-only object or a decal.
	/// </summary>
	static ObjectFlags ComputeTranslucency( TranslucencyType type, ObjectFlags flags )
	{
		if ( (flags & ~(ObjectFlags.StaticObject | ObjectFlags.IsHammerGeometry)) == ObjectFlags.CastShadowsEnabled ) return flags;
		if ( (flags & ObjectFlags.IsDecal) != 0 ) return flags;

		switch ( type )
		{
			case TranslucencyType.Full:
				flags &= ~(ObjectFlags.IsOpaque | ObjectFlags.MaterialSupportsShadows);
				flags |= ObjectFlags.IsTranslucent;
				break;
			case TranslucencyType.Partial:
				flags |= ObjectFlags.IsOpaque | ObjectFlags.IsTranslucent;
				break;
			case TranslucencyType.Opaque:
				flags |= ObjectFlags.IsOpaque;
				flags &= ~ObjectFlags.IsTranslucent;
				break;
		}

		return flags;
	}

	/// <summary>
	/// The flags of an object managed code just made from a model with <paramref name="created"/> (native's <c>CreateSceneObject</c>):
	/// the model's translucency, then the material flags.
	/// </summary>
	private protected void FlagsAtCreation( Model model, ObjectFlags created )
	{
		if ( !ResolvesDraws || model is null )
		{
			_flags = null;
			NativeTranslucencyFromMaterials( model );
			return;
		}

		_instanceFlags = created;
		_flags = UpdateFlagsFromMaterials( ComputeTranslucency( ModelDraws.For( model ).Translucency, created ) & ~InternalFlags );
		TranslucencyToNative();
	}

	/// <summary>
	/// The flags after <c>CMeshSystem::ChangeModel</c>: mesh instances made with the object's flags at the time, the material
	/// flags, then the new model's translucency flags over them.
	/// </summary>
	void FlagsAtModelChange( Model model )
	{
		if ( !ResolvesDraws || model is null )
		{
			_flags = null;
			NativeTranslucencyFromMaterials( model );
			return;
		}

		// GetFlags() as native has them then: loaded cleared, disabled while it doesn't render
		var current = CurrentFlags;
		_instanceFlags = current | (RenderingEnabled ? 0 : DisabledFlag);
		var computed = ComputeTranslucency( ModelDraws.For( model ).Translucency, _instanceFlags );
		var flags = UpdateFlagsFromMaterials( current );
		_flags = (flags & ~TranslucencyFlags) | (computed & TranslucencyFlags & ~InternalFlags);
		TranslucencyToNative();
	}

	/// <summary>
	/// Without Source 2's device native gave the object the translucency of its meshes' compiled types, where managed code
	/// works it out from their materials as a device does (<see cref="ModelDraws.Translucency"/>): a native object native code
	/// made gets the same flags.
	/// </summary>
	void TranslucencyToNative()
	{
		if ( _flags is not { } flags || native.IsNull ) return;
		if ( ((native.GetFlags() ^ flags) & TranslucencyFlags) != 0 ) native.ChangeFlags( flags & TranslucencyFlags, TranslucencyFlags );
	}

	/// <summary>
	/// An object whose draws native's stand for (<see cref="ResolvesDraws"/> false: a map's model) keeps native's flags, but
	/// without Source 2's device those carry its meshes' compiled translucency: the translucency flags are worked out from the
	/// model's materials (<see cref="ModelDraws.Translucency"/>), as a device does at load, on both. A map's door with glass
	/// panes in it is opaque and translucent, as on native.
	/// </summary>
	void NativeTranslucencyFromMaterials( Model model )
	{
		if ( model is null || model.native.IsNull || native.IsNull ) return;

		var current = CurrentFlags;
		var computed = ComputeTranslucency( ModelDraws.For( model ).Translucency, current );
		_flags = (current & ~TranslucencyFlags) | (computed & TranslucencyFlags & ~InternalFlags);
		TranslucencyToNative();
	}

	/// <summary>
	/// Flags after a material change: the translucency flags from the draws' materials
	/// (<c>CMeshSystem::UpdateTranslucencyFlagsOnMaterialChange</c>, unless <paramref name="translucency"/> is false), then
	/// <c>UpdateFlagsBasedOnMaterial</c>. An object native code made is read from native again.
	/// </summary>
	void FlagsAtMaterialChange( bool translucency = true )
	{
		if ( !ResolvesDraws || Model is not { } model || model.native.IsNull )
		{
			_flags = null;
			return;
		}

		var flags = CurrentFlags;
		if ( translucency )
		{
			var draws = ModelDraws.For( model );
			var materials = DrawMaterials( draws );
			var translucent = 0;
			foreach ( var material in materials )
			{
				if ( material is not null && material.native.GetIntAttributeOrDefault( TranslucentName, 0 ) != 0 ) translucent++;
			}

			var type = translucent == 0 ? TranslucencyType.Opaque : translucent == materials.Length ? TranslucencyType.Full : TranslucencyType.Partial;
			flags = (flags & ~TranslucencyFlags) | (ComputeTranslucency( type, _instanceFlags ) & TranslucencyFlags & ~InternalFlags);
		}

		_flags = UpdateFlagsFromMaterials( flags );
	}

	/// <summary>
	/// <c>CSceneObject::UpdateFlagsBasedOnMaterial</c>: scenesystem's <c>DefaultMaterialBasedSceneObjectUpdater</c> over the
	/// object's materials (<c>SceneObject_GetMaterialsForObject</c>), and, where Source 2's device draws (the editor), the
	/// standard pipeline's <c>MaterialDependentSceneObjectUpdater</c>. An overlay material puts the object in the static overlay layer.
	/// </summary>
	ObjectFlags UpdateFlagsFromMaterials( ObjectFlags flags )
	{
		var model = Model;
		if ( model is null || model.native.IsNull ) return flags;

		var controlled = ObjectFlags.MaterialSupportsShadows;
		var overlay = false;
		int count = 0, notCastingShadows = 0;

		void Add( Material material, int drawFlags )
		{
			count++;
			if ( material is null ) return;
			var native = material.native;
			if ( native.GetIntAttributeOrDefault( WantsFrameBufferCopyName, 0 ) != 0 ) controlled |= ObjectFlags.WantsFrameBufferCopyTexture;
			if ( native.GetBoolAttributeOrDefault( EnvironmentMappedName, false ) ) controlled |= ObjectFlags.EnvironmentMapped;
			if ( native.GetBoolAttributeOrDefault( ForwardLayerOnlyName, false ) ) controlled |= ObjectFlags.ForwardLayerOnly;
			if ( native.GetBoolAttributeOrDefault( NoZPrepassName, false ) ) controlled |= ObjectFlags.NoZPrepass;
			if ( native.GetBoolAttributeOrDefault( DoNotCastShadowsName, false ) ) notCastingShadows++;
			if ( (drawFlags & BakedLightingFlags) == 0 && native.GetBoolAttributeOrDefault( NeedsLightProbeName, false ) && native.GetBoolAttributeOrDefault( UsesBakedLightingFromProbeName, true ) )
				controlled |= ObjectFlags.NeedsLightProbe;
			if ( native.GetIntAttributeOrDefault( OverlayName, 0 ) != 0 ) overlay = true;
		}

		// SceneObject_GetMaterialsForObject: a whole-object entry alone, else every mesh instance's draws with the per index
		// overrides over them by draw position
		Material whole = null;
		var hasWhole = false;
		if ( _overrideList is not null )
		{
			foreach ( var entry in _overrideList )
			{
				if ( entry.Attribute != 0 ) continue;
				whole = entry.Material;
				hasWhole = true;
				break;
			}
		}

		if ( hasWhole )
		{
			Add( whole, 0 );
		}
		else
		{
			var draws = ModelDraws.For( model );
			var materials = DrawMaterials( draws );
			for ( int i = 0; i < materials.Length; i++ )
			{
				var draw = draws.Draws[i];
				if ( _indexOverrides is not null && i < _indexOverrides.Count && _indexOverrides[i] is { } indexed ) Add( indexed, 0 );
				else Add( materials[i], model.native.GetDrawCallFlags( draw.Mesh, draw.SceneObject, draw.Index ) );
			}
		}

		if ( count == notCastingShadows ) controlled &= ~ObjectFlags.MaterialSupportsShadows;
		flags = (flags & ~MaterialFlags) | controlled;

		if ( overlay ) _layerMatch = StaticOverlayLayerToken;

		// The standard pipeline's updater, installed only with a Vulkan device: a translucent override list entry makes the
		// object translucent

		return flags;
	}
}
