using Sandbox.Rendering;
using System.Linq;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Incremental <see cref="SceneWorld"/> mirror for <c>r_managed_scene</c>.
/// Queues object changes from any thread and applies changed fields before rendering.
/// Attribute emptiness is polled because values can change in place without notifications.
/// </summary>
internal sealed partial class SceneMirror : ISceneObjectListener, IDisposable
{
	readonly SceneWorld source;

	/// <summary>
	/// The mirrored world, or for an extra world the main world's it's drawn in.
	/// </summary>
	public RenderWorld World { get; }

	/// <summary>
	/// 0 for a main world's mirror. For an extra world's, the sub-world its objects have in <see cref="World"/> (another
	/// mirror's), which only views drawing it show (<see cref="RenderView.SubWorlds"/>).
	/// </summary>
	public int SubWorld { get; }

	/// <summary>
	/// For a main world's mirror, the sub-worlds of <see cref="World"/> its extra worlds' mirrors hold, as bits.
	/// </summary>
	public ulong HostedSubWorlds { get; set; }

	/// <summary>
	/// Unsupported object counts by type, for comparison reports.
	/// </summary>
	public Dictionary<string, int> Unsupported { get; } = new();

	readonly Dictionary<SceneObject, RenderObject> objects = new();

	/// <summary>
	/// What a scene object is in the mirror, or null if it isn't drawn.
	/// </summary>
	internal RenderObject Find( SceneObject sceneObject ) => sceneObject is not null && objects.TryGetValue( sceneObject, out var obj ) ? obj : null;
	readonly HashSet<SceneDirectionalLight> suns = new();

	// Objects whose attribute emptiness must be polled.
	readonly Dictionary<MeshObject, RenderAttributes> attributed = new();

	// Changes as they're reported, from any thread, and the batch being applied
	readonly object gate = new();
	Dictionary<SceneObject, SceneObjectChange> pending = new();
	Dictionary<SceneObject, SceneObjectChange> applying = new();

	bool sunDirty = true;

	const SceneObjectChange Everything = (SceneObjectChange)~0 & ~SceneObjectChange.Removed & ~SceneObjectChange.Deleted;

	/// <summary>
	/// Start mirroring a world: take over its change listener, and queue everything already in it.
	/// </summary>
	public SceneMirror( SceneWorld source ) : this( source, new RenderWorld(), 0 )
	{
	}

	/// <summary>
	/// Start mirroring an extra world into <paramref name="host"/>, another mirror's world, as its sub-world
	/// <paramref name="subWorld"/>: drawn in the same layers and depth by the views that show it, as native draws a camera's
	/// extra worlds (<c>CCameraRenderer::CreateView</c>, <c>AddWorldToRenderList</c>).
	/// </summary>
	public SceneMirror( SceneWorld source, RenderWorld host, int subWorld )
	{
		this.source = source;
		World = host;
		SubWorld = subWorld;

		lock ( source.InternalSceneObjects )
		{
			Listeners.Add( source, this );
			foreach ( var sceneObject in source.InternalSceneObjects )
				pending[sceneObject] = SceneObjectChange.Added;
		}
	}

	/// <summary>
	/// A world's change listener when more than one mirror listens: a world drawn by one camera and as another's extra world.
	/// </summary>
	sealed class Listeners : ISceneObjectListener
	{
		// Replaced, never changed, so changes from other threads can read it without a lock
		volatile SceneMirror[] mirrors;

		public void OnChanged( SceneObject sceneObject, SceneObjectChange change )
		{
			foreach ( var mirror in mirrors ) mirror.OnChanged( sceneObject, change );
		}

		/// <summary>
		/// Listen to <paramref name="source"/>, with any mirror listening already.
		/// </summary>
		public static void Add( SceneWorld source, SceneMirror mirror )
		{
			switch ( source.ChangeListener )
			{
				case Listeners many:
					many.mirrors = [.. many.mirrors, mirror];
					break;

				case SceneMirror one:
					source.ChangeListener = new Listeners { mirrors = [one, mirror] };
					break;

				default:
					source.ChangeListener = mirror;
					break;
			}
		}

		/// <summary>
		/// Stop listening to <paramref name="source"/>, leaving any other mirror listening.
		/// </summary>
		public static void Remove( SceneWorld source, SceneMirror mirror )
		{
			lock ( source.InternalSceneObjects )
			{
				if ( source.ChangeListener == mirror )
				{
					source.ChangeListener = null;
					return;
				}

				if ( source.ChangeListener is not Listeners many ) return;

				var left = many.mirrors.Where( x => x != mirror ).ToArray();
				if ( left.Length == 1 ) source.ChangeListener = left[0];
				else many.mirrors = left;
			}
		}
	}

	/// <summary>
	/// Queue changes from any thread for the next sync.
	/// </summary>
	public void OnChanged( SceneObject sceneObject, SceneObjectChange change )
	{
		lock ( gate )
		{
			// Native lights with a deleted light until its frame ends: the frame it was deleted in decides how long it's kept
			if ( (change & SceneObjectChange.Deleted) != 0 ) deletedFrames[sceneObject] = Application.FrameCount;
			pending[sceneObject] = pending.GetValueOrDefault( sceneObject ) | change;
		}
	}

	// The frame each object was deleted in, until it's removed, and those kept for the rest of it
	readonly Dictionary<SceneObject, ulong> deletedFrames = new();
	readonly List<SceneObject> lingering = new();

	/// <summary>
	/// Stop listening. A main world's mirror keeps its last state; an extra world's takes its objects out of the world it was
	/// drawn in.
	/// </summary>
	public void Dispose()
	{
		Listeners.Remove( source, this );
		if ( SubWorld == 0 ) return;

		foreach ( var (_, obj) in objects )
		{
			if ( obj is not null ) World.Remove( obj );
		}

		objects.Clear();
	}

	/// <summary>
	/// Whether the world has been synced at least once: a child view syncs only a mirror nothing's drawn yet.
	/// </summary>
	public bool HasSynced { get; private set; }

	/// <summary>
	/// Apply what changed since the last sync, for a camera about to render the world.
	/// </summary>
	public void Sync( SceneCamera camera, bool skyboxView = false )
	{
		HasSynced = true;

		// A reload queues the objects drawing what it changed
		CheckReloads();

		lock ( gate )
		{
			(pending, applying) = (applying, pending);
		}

		// Deleted in an earlier frame: native has stopped drawing them
		var frame = Application.FrameCount;
		for ( int i = lingering.Count - 1; i >= 0; i-- )
		{
			if ( DeletedThisFrame( lingering[i], frame ) ) continue;
			Remove( lingering[i] );
			lingering.RemoveAt( i );
		}

		foreach ( var (sceneObject, change) in applying )
			Apply( sceneObject, change, frame );

		applying.Clear();

		SyncAttributes();
		SweepCaches( frame );

		// An extra world's objects are lit by the main world's sun, sky, probe volumes and fog, as native lights the view
		if ( SubWorld == 0 ) SyncLighting( camera, skyboxView );
	}

	/// <summary>
	/// Whether an object was deleted (<see cref="SceneObjectChange.Deleted"/>) in this frame, which native still draws it in.
	/// </summary>
	bool DeletedThisFrame( SceneObject sceneObject, ulong frame )
	{
		lock ( gate ) return deletedFrames.TryGetValue( sceneObject, out var deleted ) && deleted == frame;
	}

	void Apply( SceneObject sceneObject, SceneObjectChange change, ulong frame )
	{
		if ( (change & (SceneObjectChange.Removed | SceneObjectChange.Deleted)) != 0 )
		{
			// A light deleted this frame: native lights with it until the frame ends, so the frames rendered meanwhile keep it
			// too - even when something native finished rendering views and destroyed it already (a bitmap render does), which
			// is when Removed comes. A deleted mesh native stops drawing at once
			if ( sceneObject is SceneLight && DeletedThisFrame( sceneObject, frame ) )
			{
				if ( !lingering.Contains( sceneObject ) ) lingering.Add( sceneObject );
				return;
			}

			Remove( sceneObject );
			return;
		}

		// Native deletion can precede queued change processing.
		if ( !sceneObject.IsValid() ) return;

		if ( !objects.TryGetValue( sceneObject, out var obj ) )
		{
			obj = Add( sceneObject );
			change = Everything;
		}

		if ( obj is not null && (change & SceneObjectChange.Tags) != 0 )
			obj.Tags = TagsOf( sceneObject );

		switch ( obj )
		{
			case MeshObject mesh:
				ApplyMesh( sceneObject, mesh, change );
				break;

			case LightObject light:
				ApplyLight( (SceneLight)sceneObject, light, change );
				break;

			case EnvMapObject envMap:
				ApplyEnvMap( (SceneCubemap)sceneObject, envMap, change );
				break;

			case CustomObject custom:
				ApplyCustom( sceneObject, custom, change );
				break;

			case DecalObject decal:
				ApplyDecal( (DecalSceneObject)sceneObject, decal, change );
				break;

			case AggregateObject aggregate:
				ApplyAggregate( sceneObject, aggregate, change );
				break;

			case null when sceneObject is SceneDirectionalLight:
				sunDirty = true;
				break;

			case null when sceneObject is SceneLightProbe probe && lightProbes.TryGetValue( probe, out var volume ):
				ApplyLightProbe( probe, volume, change );
				break;
		}
	}

	readonly Dictionary<SceneLightProbe, Basalt.LightProbeVolume> lightProbes = new();

	readonly Dictionary<ulong, SharedLightmap> lightmaps = new();
	readonly Dictionary<RenderAttributes, ulong> lightmapKeys = new( ReferenceEqualityComparer.Instance );

	sealed class SharedLightmap
	{
		public RenderAttributes Attributes;
		public int Users;
	}

	/// <summary>
	/// Reference-counted lightmap attributes shared by lighting key to preserve material batching.
	/// Remove on last use to prevent reuse after map unload.
	/// </summary>
	RenderAttributes AcquireLightmap( SceneObject sceneObject )
	{
		var key = SceneNative.LightmapKey( sceneObject );
		if ( key == 0 ) return null;

		if ( !lightmaps.TryGetValue( key, out var shared ) )
		{
			var attributes = SceneNative.CreateLightmapAttributes( sceneObject );
			if ( attributes is null ) return null;

			shared = new SharedLightmap { Attributes = attributes };
			lightmaps[key] = shared;
			lightmapKeys[attributes] = key;
		}

		shared.Users++;
		return shared.Attributes;
	}

	void ReleaseLightmap( RenderAttributes attributes )
	{
		if ( attributes is null || !lightmapKeys.TryGetValue( attributes, out var key ) || !lightmaps.TryGetValue( key, out var shared ) ) return;
		if ( --shared.Users > 0 ) return;

		lightmaps.Remove( key );
		lightmapKeys.Remove( attributes );
	}

	/// <summary>
	/// Mirror probe placement and selection data; create attributes once (<c>CLightBinner2::GetLightProbeVolumeData</c>).
	/// </summary>
	static void ApplyLightProbe( SceneLightProbe probe, Basalt.LightProbeVolume volume, SceneObjectChange change )
	{
		if ( (change & SceneObjectChange.Transform) != 0 )
			volume.Transform = probe.Transform;

		if ( (change & SceneObjectChange.Visibility) != 0 )
			volume.Enabled = probe.RenderingEnabled;

		if ( volume.Attributes is not null ) return;

		SceneNative.ReadLightProbeVolume( probe, volume );
		volume.Attributes = Source2Parameters.From( SceneNative.CreateLightProbeAttributes( probe ) );
	}

	/// <summary>
	/// Create a supported render object, or null. Suns update world lighting separately.
	/// </summary>
	RenderObject Add( SceneObject sceneObject )
	{
		// Probe volumes belong to the world's lighting list, not its draw list. An extra world's aren't drawn: the list is the
		// main world's, for every view of it
		if ( sceneObject is SceneLightProbe probe && SubWorld == 0 )
		{
			var volume = new Basalt.LightProbeVolume();
			lightProbes[probe] = volume;
			World.LightProbeVolumes.Add( volume );
			objects[sceneObject] = null;
			return null;
		}

		RenderObject obj = sceneObject switch
		{
			SceneCubemap => new EnvMapObject( null, default, Transform.Zero ),
			SceneDirectionalLight => null,
			SceneSkyBox => null,
			SceneSpotLight => new LightObject( LightObject.LightKind.Spot, sceneObject.Transform ),
			ScenePointLight => new LightObject( LightObject.LightKind.Point, sceneObject.Transform ),
			SceneModel => new MeshObject( null, sceneObject.Transform ),
			SceneCustomObject custom => new BridgeCustomObject( custom ),
			SceneDynamicObject dynamic => new BridgeDynamicObject( dynamic ),
			DecalSceneObject => new DecalObject( sceneObject.Transform ),
			SceneAggregateObject => AggregateFor( sceneObject ),

			// A map's world geometry is native aggregates, which have no managed type of their own
			_ when sceneObject.GetType() == typeof( SceneObject ) => AggregateFor( sceneObject ) ?? (RenderObject)new MeshObject( null, sceneObject.Transform ),
			_ => null,
		};

		objects[sceneObject] = obj;

		if ( obj is not null )
		{
			obj.SubWorld = SubWorld;
			World.Add( obj );
		}
		else if ( sceneObject is SceneDirectionalLight sun ) suns.Add( sun );
		else if ( sceneObject is not SceneSkyBox ) Count( sceneObject, 1 );

		return obj;
	}

	void Remove( SceneObject sceneObject )
	{
		lock ( gate ) deletedFrames.Remove( sceneObject );
		if ( !objects.Remove( sceneObject, out var obj ) ) return;

		if ( obj is MeshObject mesh )
		{
			Release( mesh.Mesh );
			attributed.Remove( mesh );
			sharing.Remove( mesh );
			ReleaseLightmap( mesh.LightmapAttributes.Engine() );
			mesh.LightmapAttributes = null;
		}
		else if ( obj is AggregateObject aggregate )
		{
			Release( aggregate.Mesh );
			ReleaseLightmap( aggregate.LightmapAttributes.Engine() );
			aggregate.LightmapAttributes = null;
		}

		if ( sceneObject is SceneLightProbe probe && lightProbes.Remove( probe, out var volume ) ) World.LightProbeVolumes.Remove( volume );
		else if ( obj is not null ) World.Remove( obj );
		else if ( sceneObject is SceneDirectionalLight sun ) sunDirty |= suns.Remove( sun );
		else if ( sceneObject is not SceneSkyBox ) Count( sceneObject, -1 );
	}

	void Count( SceneObject sceneObject, int by )
	{
		var name = sceneObject.GetType().Name;
		var count = Unsupported.GetValueOrDefault( name ) + by;
		if ( count > 0 ) Unsupported[name] = count;
		else Unsupported.Remove( name );
	}

	void ApplyMesh( SceneObject sceneObject, MeshObject mesh, SceneObjectChange change )
	{
		// Native draw states include body groups and all material overrides, including whole-object overrides.
		// Tint/flag changes do not require draw readback.
		var meshChanged = false;
		const SceneObjectChange Draws = SceneObjectChange.Model | SceneObjectChange.Material | SceneObjectChange.Visibility;
		if ( (change & Draws) != 0 )
		{
			var drawn = sceneObject.RenderingEnabled ? VariantFor( sceneObject, ModelFor( sceneObject.Model ) ) : null;
			if ( drawn != mesh.Mesh )
			{
				Release( mesh.Mesh );
				Hold( drawn );
				mesh.Mesh = drawn;
				meshChanged = true;

				// Shared world lightmap attributes.
				ReleaseLightmap( mesh.LightmapAttributes.Engine() );
				mesh.LightmapAttributes = drawn is { HasLightmappedDraws: true } ? Source2Parameters.From( AcquireLightmap( sceneObject ) ) : null;
			}
		}

		if ( (change & SceneObjectChange.Tint) != 0 )
			mesh.Tint = sceneObject.ColorTint;

		// Fixed LOD from the renderer or map.
		if ( (change & (Draws | SceneObjectChange.Settings)) != 0 )
			mesh.LodOverride = sceneObject.native.GetCurrentLODLevel();

		if ( (change & SceneObjectChange.Flags) != 0 )
		{
			// Static state controls shadow caching and world-tag filtering.
			var flags = sceneObject.Flags;
			mesh.CastShadows = flags.CastShadows;
			mesh.IsStatic = flags.IsStatic;
			mesh.IsWorld = mesh.IsStatic || flags.HasFlag( SceneObjectFlags.IsHammerGeometry );

			// Its renderer's own command lists (Renderer.ExecuteBefore, ExecuteAfter)
			if ( flags.WantsExecuteBefore || flags.WantsExecuteAfter )
			{
				var callbacks = mesh.Callbacks as BridgeDrawCallbacks ?? new BridgeDrawCallbacks( sceneObject );
				callbacks.Before = flags.WantsExecuteBefore;
				callbacks.After = flags.WantsExecuteAfter;
				mesh.Callbacks = callbacks;
			}
			else
			{
				mesh.Callbacks = null;
			}
		}

		// Material changes recompute native blend flags.
		if ( (change & (SceneObjectChange.Flags | Draws)) != 0 )
			ApplyMeshLayers( sceneObject, mesh );

		if ( (change & SceneObjectChange.Transform) != 0 )
			mesh.Transform = sceneObject.Transform;

		// Local lighting origins move with the object.
		if ( (change & (SceneObjectChange.Transform | SceneObjectChange.Flags | Draws)) != 0 )
			mesh.LightingOrigin = sceneObject.native.HasLightingOrigin() ? sceneObject.native.GetLightingOrigin() : null;

		// Track mutable attributes; SyncAttributes omits empty sets.
		if ( (change & SceneObjectChange.Attributes) != 0 )
		{
			if ( sceneObject.CreatedAttributes is { } attributes ) attributed[mesh] = attributes;
			else attributed.Remove( mesh );
			sharing.Remove( mesh );
			mesh.Attributes = null;
		}

		// Restore posed local bounds after mesh changes reset them to bind-pose bounds.
		const SceneObjectChange Pose = SceneObjectChange.Bones | SceneObjectChange.Transform | SceneObjectChange.Model;
		if ( ((change & Pose) != 0 || meshChanged) && sceneObject is SceneModel model && mesh.Mesh is { IsSkinned: true } )
		{
			// The matrices native skins with, straight into the object's own: a bone each, as many as its mesh's bind pose has
			model.GetBoneWorldMatrices( mesh.PoseBoneMatrices( mesh.Mesh.BindPose.Length ) );

			// Native evaluates morph flex rules each frame, from the model's: only a new model or mesh changes whether it morphs
			if ( meshChanged || (change & SceneObjectChange.Model) != 0 )
			{
				mesh.MorphSource = model.Model is not { MorphCount: > 0 } ? null
					: mesh.MorphSource is SceneModelMorphs morphs && morphs.Source == model ? morphs : new SceneModelMorphs( model );
			}

			// Native bounds include deformation padding.
			var volumes = MemoryMarshal.Cast<SceneDeformationVolumeData, DeformationVolume>( model.DeformationVolumes );
			if ( !volumes.SequenceEqual( mesh.DeformationVolumes ) ) mesh.DeformationVolumes = volumes.ToArray();
			var anchors = model.DeformationAnchors;
			if ( !anchors.SequenceEqual( mesh.DeformationAnchors ) ) mesh.DeformationAnchors = anchors.ToArray();

			mesh.LocalBounds = ToLocal( model.Bounds, mesh.Transform );
		}
	}

	/// <summary>
	/// An aggregate's model and fragments as native draws them, or null if the object isn't one.
	/// </summary>
	AggregateObject AggregateFor( SceneObject sceneObject )
	{
		var model = SceneNative.AggregateModel( sceneObject, out var lightmapped );
		if ( ModelFor( model )?.Mesh is not { } mesh ) return null;

		Hold( mesh );

		return new AggregateObject( mesh, SceneNative.ReadAggregateFragments( sceneObject ) ) { Lightmapped = lightmapped };
	}

	/// <summary>
	/// Mirror an aggregate's flags, tint and lightmaps. Its fragments are fixed when it's made.
	/// </summary>
	void ApplyAggregate( SceneObject sceneObject, AggregateObject aggregate, SceneObjectChange change )
	{
		if ( (change & SceneObjectChange.Visibility) != 0 )
			aggregate.Visible = sceneObject.RenderingEnabled;

		if ( (change & SceneObjectChange.Tint) != 0 )
			aggregate.Tint = sceneObject.ColorTint;

		if ( (change & SceneObjectChange.Flags) != 0 )
		{
			var flags = sceneObject.Flags;
			ApplyLayerFlags( aggregate, flags );
			aggregate.CastShadows = flags.CastShadows;
			aggregate.IsStatic = flags.IsStatic;
			aggregate.IsWorld = aggregate.IsStatic || flags.HasFlag( SceneObjectFlags.IsHammerGeometry );
			aggregate.OcclusionCulling = !flags.HasFlag( SceneObjectFlags.NoOcclusionCulling );
		}

		if ( (change & SceneObjectChange.Added) != 0 && aggregate.Lightmapped )
		{
			ReleaseLightmap( aggregate.LightmapAttributes.Engine() );
			aggregate.LightmapAttributes = Source2Parameters.From( AcquireLightmap( sceneObject ) );
		}
	}

	static void ApplyLight( SceneLight sceneLight, LightObject light, SceneObjectChange change )
	{
		if ( (change & SceneObjectChange.Transform) != 0 )
			light.Transform = sceneLight.Transform;

		if ( (change & SceneObjectChange.Settings) != 0 )
		{
			light.Color = sceneLight.LightColor;
			light.Radius = sceneLight.Radius;
			light.Attenuation = sceneLight.QuadraticAttenuation;
			light.CastShadows = sceneLight.ShadowsEnabled;
			light.ShadowHardness = sceneLight.ShadowHardness;

			if ( sceneLight is SceneSpotLight spot )
			{
				light.ConeInner = spot.ConeInner;
				light.ConeOuter = spot.ConeOuter;
			}
		}

		// Pack native light descriptions; map attenuation/falloff/baking may bypass managed properties (Light.LegacyLightData).
		if ( (change & (SceneObjectChange.Settings | SceneObjectChange.Transform)) != 0 && SceneNative.PackSceneLight( sceneLight, out var packed, out var flags, out var bakeIndex ) )
		{
			light.NativePacked = packed;
			light.Baked = (flags & LightTypeFlagsBaked) != 0;
			light.MixedShadows = (flags & LightTypeFlagsMixedShadows) != 0;
			light.BakeIndex = bakeIndex;
		}
	}

	// LIGHTTYPE_FLAGS in native's lightdesc.h
	const uint LightTypeFlagsMixedShadows = 16, LightTypeFlagsBaked = 32;

	/// <summary>
	/// Mirror custom-object placement, culling bounds and flags.
	/// </summary>
	static void ApplyCustom( SceneObject sceneObject, CustomObject custom, SceneObjectChange change )
	{
		const SceneObjectChange Placement = SceneObjectChange.Transform | SceneObjectChange.Bounds;
		if ( (change & Placement) != 0 )
		{
			// Unplaced custom objects may report zero scale; native uses identity.
			var transform = sceneObject.Transform;
			custom.Transform = transform == default ? Transform.Zero : transform;

			// Read native bounds: SceneObject.Bounds collapses infinity to a point.
			// Use large finite bounds to keep culling math valid.
			var bounds = sceneObject.native.GetBounds();
			if ( !float.IsFinite( bounds.Mins.x + bounds.Mins.y + bounds.Mins.z + bounds.Maxs.x + bounds.Maxs.y + bounds.Maxs.z ) || bounds.Size.x > 1e9f )
				bounds = new BBox( new Vector3( -1e9f ), new Vector3( 1e9f ) );

			custom.LocalBounds = ToLocal( bounds, custom.Transform );
		}

		const SceneObjectChange State = SceneObjectChange.Tint | SceneObjectChange.Flags | SceneObjectChange.Visibility;
		if ( (change & State) != 0 )
		{
			custom.Tint = sceneObject.ColorTint;
			custom.IsOpaque = sceneObject.Flags.IsOpaque && !sceneObject.Flags.IsTranslucent;
			custom.WantsFrameBufferCopy = sceneObject.Flags.WantsFrameBufferCopy;
			custom.DepthPrepass = !sceneObject.Flags.HasFlag( SceneObjectFlags.NoZPrepass );
			custom.CastShadows = sceneObject.Flags.CastShadows;
			custom.Visible = sceneObject.RenderingEnabled;
			ApplyLayerFlags( custom, sceneObject.Flags );
			custom.LayerMatch = LayerMatchFor( sceneObject.native.GetLayerMatchIDValue() );
		}
	}

	static readonly uint StaticOverlayMatch = new StringToken( "StaticOverlayLayer" ).Value;
	static readonly uint OverlayWithDepthMatch = new StringToken( "OverlayWithDepth" ).Value;
	static readonly uint OverlayWithoutDepthMatch = new StringToken( "OverlayWithoutDepth" ).Value;
	static readonly StringToken RenderOrderName = new( "RenderOrder" );
	static readonly StringToken LightGroupName = new( "LightGroup" );

	/// <summary>
	/// Mirror native blend/prepass flags and layer match (<c>CSceneObject::m_nLayerMatchID</c>).
	/// MeshRenderFeature owns classification.
	/// </summary>
	static void ApplyMeshLayers( SceneObject sceneObject, MeshObject mesh )
	{
		var flags = sceneObject.Flags;
		ApplyLayerFlags( mesh, flags );
		mesh.DrawsOpaque = flags.HasFlag( SceneObjectFlags.IsOpaque );
		mesh.DrawsTranslucent = flags.HasFlag( SceneObjectFlags.IsTranslucent );
		mesh.DepthPrepass = !flags.HasFlag( SceneObjectFlags.NoZPrepass );
		mesh.OcclusionCulling = !flags.HasFlag( SceneObjectFlags.NoOcclusionCulling );
		mesh.WantsFrameBufferCopy = flags.HasFlag( SceneObjectFlags.WantsFrameBufferCopyTexture );
		mesh.Decal = flags.HasFlag( SceneObjectFlags.IsDecal );
		mesh.NeedsLightProbe = flags.HasFlag( SceneObjectFlags.NeedsLightProbe );
		mesh.LightGroup = (uint)sceneObject.native.GetIntValue( LightGroupName, 0 );

		mesh.LayerMatch = LayerMatchFor( sceneObject.native.GetLayerMatchIDValue() );
		mesh.RenderOrder = mesh.LayerMatch == LayerMatch.StaticOverlay ? sceneObject.native.GetIntValue( RenderOrderName, 0 ) : 0;
	}

	/// <summary>
	/// Describe unusual mesh flags, fading or deformation; empty for ordinary meshes.
	/// </summary>
	static string LayerDescription( MeshObject mesh )
	{
		var parts = new List<string>();
		if ( !mesh.GameLayers ) parts.Add( "not game" );
		if ( mesh.Overlay ) parts.Add( "overlay" );
		if ( mesh.Bloom ) parts.Add( "bloom" );
		if ( mesh.AfterUI ) parts.Add( "after UI" );
		if ( mesh.Decal ) parts.Add( "decal" );
		if ( mesh.LayerMatch != LayerMatch.None ) parts.Add( $"matched {mesh.LayerMatch}" );
		if ( mesh.Tint.a < 1 ) parts.Add( $"faded {mesh.Tint.a:0.##}" );
		if ( !mesh.DrawsOpaque && !mesh.DrawsTranslucent ) parts.Add( "neither opaque nor translucent" );
		if ( mesh.WantsFrameBufferCopy ) parts.Add( "reads frame buffer" );
		if ( mesh.IsDeformed ) parts.Add( $"{mesh.DeformationVolumes.Length} deformation volumes" );
		return parts.Count == 0 ? "" : $" ({string.Join( ", ", parts )})";
	}

	/// <summary>
	/// Translate native layer-match IDs; MeshRenderFeature decides membership.
	/// </summary>
	static LayerMatch LayerMatchFor( uint match )
	{
		if ( match == 0 ) return LayerMatch.None;
		if ( match == StaticOverlayMatch ) return LayerMatch.StaticOverlay;
		if ( match == OverlayWithDepthMatch ) return LayerMatch.OverlayWithDepth;
		if ( match == OverlayWithoutDepthMatch ) return LayerMatch.OverlayWithoutDepth;
		return LayerMatch.Unsupported;
	}

	/// <summary>
	/// Mirror game, overlay and effect flags from native <c>RenderOptions</c>.
	/// </summary>
	static void ApplyLayerFlags( RenderObject obj, SceneObject.SceneObjectFlagAccessor flags )
	{
		obj.GameLayers = !flags.HasFlag( SceneObjectFlags.ExcludeGameLayer );
		obj.Overlay = flags.HasFlag( SceneObjectFlags.GameOverlayLayer );
		obj.Bloom = flags.HasFlag( SceneObjectFlags.EffectsBloomLayer );
		obj.AfterUI = flags.HasFlag( SceneObjectFlags.UIOverlayLayer );
	}

	/// <summary>
	/// A decal's box and everything it projects, as <c>CLightBinnerStandard</c> reads a <c>CDecalSceneObject</c>.
	/// </summary>
	static void ApplyDecal( DecalSceneObject source, DecalObject decal, SceneObjectChange change )
	{
		if ( (change & SceneObjectChange.Transform) != 0 )
			decal.Transform = source.Transform == default ? Transform.Zero : source.Transform;

		if ( (change & SceneObjectChange.Visibility) != 0 )
			decal.Visible = source.RenderingEnabled;

		if ( (change & SceneObjectChange.Settings) != 0 )
		{
			decal.Color = Source2Texture.From( source.ColorTexture );
			decal.Normal = Source2Texture.From( source.NormalTexture );
			decal.RoughnessMetalnessOcclusion = Source2Texture.From( source.RMOTexture );
			decal.Height = Source2Texture.From( source.HeightTexture );
			decal.Emission = Source2Texture.From( source.EmissionTexture );
			decal.Tint = source.Color;
			decal.SortOrder = source.SortOrder;
			decal.ExclusionBitMask = source.ExclusionBitMask;
			decal.AttenuationAngle = source.AttenuationAngle;
			decal.EmissionEnergy = source.EmissionEnergy;
			decal.SequenceIndex = source.SequenceIndex;
			decal.ColorMix = source.ColorMix;
			decal.BlendMode = (Basalt.DecalBlendMode)source.BlendMode;
			decal.ParallaxStrength = source.ParallaxStrength;
			decal.SamplerIndex = source.SamplerIndex;
			decal.CoverageAmount = source.CoverageAmount;
			decal.CoverageRange = source.CoverageRange;
		}
	}

	static void ApplyEnvMap( SceneCubemap cubemap, EnvMapObject envMap, SceneObjectChange change )
	{
		if ( (change & SceneObjectChange.Transform) != 0 )
			envMap.Transform = cubemap.Transform;

		if ( (change & SceneObjectChange.Settings) != 0 )
		{
			envMap.Cubemap = Source2Texture.From( cubemap.Texture );
			envMap.ProjectionBounds = cubemap.ProjectionBounds;
			envMap.Tint = cubemap.TintColor;
			envMap.Feathering = cubemap.Feathering;
			envMap.Priority = cubemap.Priority;
		}
	}

	/// <summary>
	/// Omit empty attributes to preserve instancing (<c>CBaseSceneObjectDesc::BindMaterial</c>), and let objects whose
	/// attributes hold the same values share one set (<see cref="SharedAttributes"/>), so they instance and keep the material
	/// bound between them as objects with none do. Poll every sync because values mutate without notifications.
	/// </summary>
	void SyncAttributes()
	{
		foreach ( var (mesh, attributes) in attributed )
		{
			var native = attributes.Get();
			if ( native.IsNull || native.IsEmpty() )
			{
				mesh.Attributes = null;
				sharing.Remove( mesh );
				continue;
			}

			mesh.Attributes = Source2Parameters.From( SharedAttributes( mesh, attributes, native ) );
		}
	}

	// The attribute set each object draws with, someone's own whose values are the same as its own, and the sets lately shared
	readonly Dictionary<MeshObject, RenderAttributes> sharing = new();
	readonly List<RenderAttributes> shareable = new();

	/// <summary>
	/// How many of the sets lately shared a changed object's attributes are compared with before it draws with its own. Bounds the
	/// cost of an object whose attributes change every frame; each compare is one native call.
	/// </summary>
	const int MaxShareable = 32;

	/// <summary>
	/// The attribute set an object draws with: the one it shared last sync, while it still holds the same values as its own, or
	/// another of the same values, or its own. A dresser gives every renderer of a citizen <c>skin_tint</c> and <c>skin_age</c>,
	/// most often the defaults, so a crowd's bodies and clothing all have attributes, which made each draw alone and rebind its
	/// material. Values can change in place on either side, so every object compares its own with the set it shares each sync.
	/// </summary>
	RenderAttributes SharedAttributes( MeshObject mesh, RenderAttributes own, NativeEngine.CRenderAttributes native )
	{
		if ( sharing.TryGetValue( mesh, out var shared ) && SameValues( native, shared ) ) return shared;

		for ( int i = 0; i < shareable.Count; i++ )
		{
			var candidate = shareable[i];
			if ( candidate.Get().IsNull )
			{
				shareable.RemoveAt( i-- );
				continue;
			}

			if ( !SameValues( native, candidate ) ) continue;

			sharing[mesh] = candidate;
			return candidate;
		}

		// Its own, for the next to share
		sharing[mesh] = own;
		shareable.Insert( 0, own );
		if ( shareable.Count > MaxShareable ) shareable.RemoveAt( shareable.Count - 1 );
		return own;
	}

	static bool SameValues( NativeEngine.CRenderAttributes native, RenderAttributes other )
	{
		var theirs = other.Get();
		return !theirs.IsNull && (native == theirs || NativeEngine.CRenderAttributes.AreEqual( native, theirs, false ));
	}

	/// <summary>
	/// Refresh dirty sun state, per-view sky selection and camera lighting (<c>CameraRenderer.Configure</c>).
	/// Uses the first enabled sun; native uses the last encountered by its binner.
	/// </summary>
	void SyncLighting( SceneCamera camera, bool skyboxView )
	{
		var lighting = World.Lighting;

		if ( sunDirty )
		{
			sunDirty = false;

			SceneDirectionalLight sun = null;
			foreach ( var candidate in suns )
			{
				if ( candidate.IsValid() && candidate.RenderingEnabled )
				{
					sun = candidate;
					break;
				}
			}

			if ( sun is not null )
			{
				lighting.SunColor = sun.LightColor;
				// Map suns can have identity transforms; use the native light direction.
				lighting.SunDirection = -sun.WorldDirection;
				lighting.SunShadows = sun.ShadowsEnabled;
				lighting.SunShadowCascades = sun.ShadowCascadeCount;
				lighting.SunShadowSplitRatio = sun.ShadowCascadeSplitRatio;
				lighting.SunShadowHardness = sun.ShadowHardness;
				lighting.SunShadowBias = sun.ShadowBias;

				// Lightmaps and probes reference the sun by bake index.
				var packs = SceneNative.PackSceneLight( sun, out var packed, out var flags, out var bakeIndex );
				lighting.SunBakeIndex = packs ? bakeIndex : -1;
				lighting.SunBaked = packs && bakeIndex >= 0 ? packed : null;

				var baked = packs && (flags & LightTypeFlagsBaked) != 0;
				// A baked sun still renders cascades, without its static casters: native's binner only asks local lights for
				// mixed shadows (CLightBinnerStandard::AddLight), and volumetric fog is lit by the sun only through them
				lighting.SunShadowsBaked = baked && !sun.IncludeStaticShadowCasters;
				lighting.SunContactShadows = sun.ContactShadows;
				lighting.SunFogStrength = sun.FogStrength;
			}
			else
			{
				lighting.SunColor = Color.Black;
				lighting.SunBakeIndex = -1;
				lighting.SunBaked = null;
				lighting.SunShadowsBaked = false;
				lighting.SunContactShadows = false;
			}
		}

		// Sky selection is per view: cameras can exclude different skies without changing the world.
		// Native's child skybox view does not inherit the parent camera's object-tag filters.
		var sky = skyboxView ? source.GetSky() : source.GetSky( camera.RenderTags, camera.ExcludeTags );
		lighting.SkyMaterial = Source2Material.From( sky?.Material );
		if ( sky is not null )
		{
			lighting.SkyTint = sky.SkyTint;
			lighting.SkyRotation = sky.Transform.Rotation;
			lighting.SkyFog = new SkyFog( (SkyFogType)sky.FogParams.FogType, sky.FogParams.FogMinStart, sky.FogParams.FogMinEnd, sky.FogParams.FogMaxStart, sky.FogParams.FogMaxEnd );
		}

		lighting.AmbientColor = source.AmbientLightColor + camera.AmbientLightColor;
		lighting.GradientFog = source.GradientFog.ToBasalt();

		// Per-view wind (SetWindParams).
		SceneNative.ReadWorldWind( source, out var windStrength, out var windDirection );
		lighting.WindStrengthFreq = windStrength;
		lighting.WindDirection = windDirection;

		var from = camera.CubemapFog;
		var to = lighting.CubemapFog;
		to.Enabled = from.Enabled;
		to.Texture = Source2Texture.From( from.Texture );
		to.StartDistance = from.StartDistance;
		to.EndDistance = from.EndDistance;
		to.FalloffExponent = from.FalloffExponent;
		to.LodBias = from.LodBias;
		to.HeightWidth = from.HeightWidth;
		to.HeightStart = from.HeightStart;
		to.HeightExponent = from.HeightExponent;
		to.Tint = from.Tint;
		to.Transform = from.Transform;

		// Camera fog settings (SceneCamera.GatherVolumetricFog).
		var fogFrom = camera.VolumetricFog;
		var fogTo = lighting.VolumetricFog;
		fogTo.Enabled = fogFrom.Enabled;
		fogTo.Anisotropy = fogFrom.Anisotropy;
		fogTo.Scattering = fogFrom.Scattering;
		fogTo.DrawDistance = fogFrom.DrawDistance;
		fogTo.FadeInStart = fogFrom.FadeInStart;
		fogTo.FadeInEnd = fogFrom.FadeInEnd;
		fogTo.IndirectStrength = fogFrom.IndirectStrength;
		fogTo.BakedIndirectTexture = Source2Texture.From( fogFrom.BakedIndirectTexture );

		SyncFogVolumes();
	}

	int fogVolumesVersion = -1;
	readonly List<FogVolume> fogVolumePool = new();

	/// <summary>
	/// Refresh changed fog volumes in native order, reusing pooled objects (<c>SceneWorld.FogVolumes</c>).
	/// </summary>
	void SyncFogVolumes()
	{
		if ( source.FogVolumesVersion == fogVolumesVersion ) return;
		fogVolumesVersion = source.FogVolumesVersion;

		var volumes = World.FogVolumes;
		volumes.Clear();
		foreach ( var from in source.FogVolumes )
		{
			if ( fogVolumePool.Count <= volumes.Count ) fogVolumePool.Add( new FogVolume() );
			var to = fogVolumePool[volumes.Count];
			to.Transform = from.Transform;
			to.Bounds = from.BoundingBox;
			to.Strength = from.FogStrength;
			to.Exponent = from.FalloffExponent;
			to.Color = from.Color;
			to.Spherical = false;
			volumes.Add( to );
		}
	}

	/// <summary>
	/// Summarize mirrored lighting and models for parity diagnostics.
	/// </summary>
	public string Describe()
	{
		var lighting = World.Lighting;
		var text = new System.Text.StringBuilder();
		var others = suns.Count( x => x.IsValid() && x.RenderingEnabled ) - 1;
		text.Append( $"sun {lighting.SunColor} towards {lighting.SunDirection} (shadows {lighting.SunShadows}, {lighting.SunShadowCascades} cascades{(others > 0 ? $", {others} more: " + string.Join( ", ", suns.Where( x => x.IsValid() && x.RenderingEnabled ).Skip( 1 ).Select( x => $"{x.LightColor} towards {-x.WorldDirection}" ) ) : "")}), ambient {lighting.AmbientColor}, gradient fog {lighting.GradientFog.Enabled}, cubemap fog {lighting.CubemapFog.Enabled}, volumetric fog {lighting.VolumetricFog.Enabled} with {World.FogVolumes.Count} volumes" );

		foreach ( var obj in World.Objects )
		{
			switch ( obj )
			{
				case LightObject light:
					text.Append( $"; {light.Kind} light at {light.Transform.Position} colour {light.Color} radius {light.Radius} attenuation {light.Attenuation} shadows {light.CastShadows}{(light.Baked ? $" baked{(light.MixedShadows ? " with mixed shadows" : "")}" : "")}{(light.BakeIndex >= 0 ? $" bake index {light.BakeIndex}" : "")}{(light.NativePacked is null ? "" : " (native packed)")}" );
					break;
				case EnvMapObject envMap:
					text.Append( $"; probe {(envMap.Cubemap is not { } cube ? "(none)" : cube.Engine()?.ResourcePath ?? $"{cube.Width}px {cube.Format} (made at runtime)")} box {envMap.ProjectionBounds} priority {envMap.Priority} tint {envMap.Tint}" );
					break;
			}
		}

		var meshes = World.Objects.ToArray().OfType<MeshObject>().ToArray();
		text.Append( $"; {meshes.Count( x => x.LightmapAttributes is not null )} of {meshes.Length} meshes lightmapped, {meshes.Count( x => x.Mesh is { HasLightmappedDraws: true } )} with lightmapped draws; sun bake index {lighting.SunBakeIndex}{(lighting.SunBaked is { } baked ? $", packed towards {baked.Row0.x:0.###},{baked.Row1.x:0.###},{baked.Row2.x:0.###} color {baked.Color}" : "")}" );
		// Compare probe packing within float tolerance, excluding setup-time cubemap indices.
		var probes = 0;
		var probesAsNative = 0;
		foreach ( var (sceneObject, renderObject) in objects )
		{
			if ( renderObject is not EnvMapObject envMap ) continue;
			probes++;
			if ( SceneNative.PacksAsNative( sceneObject, envMap ) ) probesAsNative++;
		}
		text.Append( $"; {probesAsNative} of {probes} envmap probes packed as native packs them" );

		var volumes = World.LightProbeVolumes;
		text.Append( $"; {volumes.Count} light probe volumes ({volumes.Count( x => x.Attributes is not null )} with constants, light groups {string.Join( " ", volumes.SelectMany( x => x.LightGroups ).Distinct() )}), {meshes.Count( x => x.NeedsLightProbe )} meshes need one, {meshes.Count( x => x.NeedsLightProbe && Basalt.LightProbeVolume.Choose( volumes, World.BoundsCenter[x.Index], World.BoundsCenter[x.Index], World.BoundsExtents[x.Index], x.LightGroup ) >= 0 )} find one" );

		// Report unusual layer membership per model.
		foreach ( var group in meshes.GroupBy( x => (x.Mesh?.Geometry?.Name ?? "(hidden)", LayerDescription( x )) ) )
			text.Append( $"; {group.Count()}x {group.Key.Item1}{group.Key.Item2}" );

		foreach ( var custom in World.Objects.ToArray().OfType<BridgeCustomObject>() )
			text.Append( $"; {custom.Source.GetType().Name} {(custom.IsOpaque ? "opaque" : "translucent")}{(custom.CastShadows ? " casting shadows" : "")}{(custom.Visible ? "" : " hidden")} bounds {custom.LocalBounds}" );

		return text.ToString();
	}

	/// <summary>
	/// A scene object's tags as string tokens, as native holds them.
	/// </summary>
	static uint[] TagsOf( SceneObject sceneObject )
	{
		var count = sceneObject.native.GetTagCount();
		if ( count <= 0 ) return [];

		var tags = new uint[count];
		for ( int i = 0; i < count; i++ )
			tags[i] = sceneObject.native.GetTagAt( i );

		return tags;
	}

	/// <summary>
	/// A world box in an object's space: the box around its corners taken back through the transform.
	/// </summary>
	static BBox ToLocal( BBox world, Transform transform )
	{
		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );

		for ( int i = 0; i < 8; i++ )
		{
			var corner = new Vector3( (i & 1) == 0 ? world.Mins.x : world.Maxs.x, (i & 2) == 0 ? world.Mins.y : world.Maxs.y, (i & 4) == 0 ? world.Mins.z : world.Maxs.z );
			var local = transform.PointToLocal( corner );
			mins = Vector3.Min( mins, local );
			maxs = Vector3.Max( maxs, local );
		}

		return new BBox( mins, maxs );
	}
}
