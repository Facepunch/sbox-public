namespace Sandbox;

using Editor;
using System;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;

/// <summary>
/// Dynamic Diffuse Global Illumination volume that provides indirect lighting using a 3D probe grid.
/// Probes store irradiance and distance data in volume textures that can be sampled by shaders.
/// </summary>
[Expose]
[Title( "Indirect Light Volume (DDGI)" )]
[Category( "Light" )]
[Icon( "grid_view" )]
[EditorHandle( "materials/gizmo/lpv.png" )]
[Alias( "DDGIVolume" )]
public sealed partial class IndirectLightVolume : Component, Component.ExecuteInEditor, Component.DontExecuteOnServer
{
	/// <summary>
	/// Behavior when a probe is detected inside geometry.
	/// Relocation attempts to move the probe out of geometry, while Deactivate excludes it from lighting.
	/// </summary>
	public enum InsideGeometryBehavior
	{
		/// <summary>
		/// Probe is deactivated and won't contribute to lighting.
		/// </summary>
		[Icon( "block" )]
		Deactivate,

		/// <summary>
		/// Probe is relocated to escape the geometry.
		/// </summary>
		[Icon( "open_with" )]
		Relocate
	}

	/// <summary>
	/// Per-probe data including position offset and active state.
	/// </summary>
	public sealed class Probe
	{
		/// <summary>
		/// World-space displacement from the probe's grid position.
		/// </summary>
		public Vector3 Offset { get; set; }

		/// <summary>
		/// Whether this probe contributes to indirect lighting.
		/// </summary>
		public bool Active { get; set; } = true;
	}

	/// <summary>
	/// CPU-side probe data indexed by flattened probe index.
	/// </summary>
	internal Probe[] Probes { get; set; }

	//
	// Properties
	//

	/// <summary>
	/// Local-space bounding box that defines the volume coverage area.
	/// </summary>
	[Property]
	[Description( "Local bounds covered by the probe grid. Enclose the area that needs indirect lighting. Changing the bounds requires a rebake." )]
	public BBox Bounds
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			MarkDirty();
		}
	} = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 512.0f ) );

	/// <summary>
	/// Number of probe intervals per 1024 local units. Higher values increase probe resolution.
	/// </summary>
	[Header( "Probes" )]
	[Property, Range( 1, 15 )]
	[Description( "Controls how closely probes are spaced inside the volume. Higher values capture smaller lighting details but increase bake time and memory use. Requires a rebake." )]
	public int ProbeDensity
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			MarkDirty();
		}
	} = 8;

	/// <summary>
	/// How to handle probes detected inside geometry.
	/// </summary>
	[Property]
	[Description( "Controls probes found inside geometry when baking. Relocate tries to move them into nearby free space and excludes them if they remain inside; Deactivate excludes them without moving them. Requires a rebake." )]
	public InsideGeometryBehavior InsideGeometry { get; set; } = InsideGeometryBehavior.Relocate;

	/// <summary>
	/// Objects with any of these tags will be excluded when baking probes for this volume.
	/// </summary>
	[Property]
	[Description( "Objects with any of these tags are omitted from probe captures and relocation. Use this to exclude objects that should not affect baked lighting. Requires a rebake." )]
	public TagSet RenderExcludeTags { get; set; } = new();

	/// <summary>
	/// World-space bias along surface normals to prevent self-occlusion artifacts.
	/// </summary>
	[Header( "Sampling" )]
	[Property, Range( -0.0f, 50.0f ), Advanced]
	[Description( "Moves the lighting sample away from the surface, in world units. Increase to reduce dark edge artifacts; too much can leak light through nearby geometry. Updates immediately without rebaking." )]
	public float NormalBias
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			MarkDirty();
		}
	} = 10.0f;

	/// <summary>
	/// World-space bias toward the viewing position to reduce self-occlusion at corners in new bakes.
	/// </summary>
	[Property, Range( 0.0f, 50.0f ), Advanced]
	[Description( "Moves the lighting sample toward the camera, in world units. Helps reduce dark corners, but too much can cause light leaks or changes as the camera moves. Updates immediately. Legacy bakes must be rebaked to use view bias." )]
	public float ViewBias
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			MarkDirty();
		}
	} = 20.0f;

	/// <summary>
	/// Multiplies sampled indirect lighting by an RGB tint. Alpha is ignored.
	/// </summary>
	[Header( "Runtime Adjustments" )]
	[Property, ColorUsage( false, false ), Title( "Tint" )]
	[Description( "Tints this volume's indirect lighting. White is unchanged. Updates immediately and is not baked into probes." )]
	public Color RuntimeTint
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			MarkDirty();
		}
	} = Color.White;

	/// <summary>
	/// Multiplies sampled indirect lighting without changing the baked textures.
	/// </summary>
	[Property, Range( 0.0f, 4.0f ), Title( "Intensity" )]
	[Description( "Multiplies this volume's indirect lighting. One is unchanged and zero removes its light contribution. Updates immediately and is not baked into probes." )]
	public float RuntimeIntensity
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			MarkDirty();
		}
	} = 1.0f;

	/// <summary>
	/// Adjusts sampled indirect lighting around 0.18 in linear light. One is unchanged.
	/// Applies immediately without rebaking and is excluded from probe captures.
	/// </summary>
	[Property, Range( 0.0f, 2.0f ), Title( "Contrast" )]
	[Description( "Adjusts this volume's indirect lighting around a linear brightness of 0.18. One is unchanged; higher values deepen darks and brighten highlights, while lower values flatten contrast. Updates immediately and is not baked into probes." )]
	public float RuntimeContrast
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			MarkDirty();
		}
	} = 1.0f;

	/// <summary>
	/// Adjusts the color of sampled indirect lighting. Zero is grayscale; one is unchanged.
	/// Applies immediately without rebaking and is excluded from probe captures.
	/// </summary>
	[Property, Range( 0.0f, 2.0f ), Title( "Saturation" )]
	[Description( "Adjusts the color intensity of this volume's indirect lighting. Zero removes color, one is unchanged, and higher values strengthen color bleed. Updates immediately and is not baked into probes." )]
	public float RuntimeSaturation
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			MarkDirty();
		}
	} = 1.0f;

	/// <summary>
	/// Distance in world units over which the volume fades outward from its bounds.
	/// Remaining influence passes to overlapping volumes in the existing volume order.
	/// </summary>
	[Property, Range( 0.0f, 512.0f )]
	[Description( "Keeps indirect lighting at full strength inside the volume and fades it out beyond the bounds, in world units. Use overlapping volumes for smooth transitions between areas. Zero keeps a hard boundary. Updates immediately without rebaking." )]
	public float BlendDistance
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			MarkDirty();
		}
	}

	/// <summary>
	/// Calculated probe count along each axis based on bounds and density.
	/// </summary>
	public Vector3Int ProbeCounts => ComputeProbeCounts();

	/// <summary>
	/// Maximum world-space distance from an unrelocated probe to a point in its grid cell.
	/// </summary>
	internal float ProbeCellDiagonal => (ComputeSpacing( ProbeCounts ) * WorldScale.Abs()).Length;

	/// <summary>
	/// Index of this volume in the scene's DDGI_Volumes buffer, or -1 when it isn't uploaded.
	/// </summary>
	internal int GpuIndex { get; set; } = -1;

	/// <summary>
	/// Volume texture storing probe irradiance data (color).
	/// </summary>
	[Property, Hide]
	public Texture IrradianceTexture { get; set; }

	/// <summary>
	/// Volume texture storing probe distance/visibility data.
	/// </summary>
	[Property, Hide]
	public Texture DistanceTexture { get; set; }

	/// <summary>
	/// Volume texture storing probe relocation offsets (XYZ = offset, W = active).
	/// </summary>
	[Property, Hide]
	public Texture RelocationTexture { get; set; }

	/// <summary>
	/// Encoding of the saved probe data. Missing versions use legacy visibility;
	/// version 1 stores normalized mean distance and standard deviation.
	/// </summary>
	[Property, Hide]
	public int BakeVersion { get; set; }

	private const int CurrentBakeVersion = 1;

	/// <summary>
	/// Versions serialized probe settings while preserving existing scenes.
	/// </summary>
	public override int ComponentVersion => 2;

	[Expose, JsonUpgrader( typeof( IndirectLightVolume ), 1 )]
	static void UpgradeLegacyBakeVersion( JsonObject obj )
	{
		obj[nameof( BakeVersion )] ??= 0;
	}

	[Expose, JsonUpgrader( typeof( IndirectLightVolume ), 2 )]
	static void UpgradeLegacyBiasDefaults( JsonObject obj )
	{
		obj[nameof( NormalBias )] ??= 5.0f;
		obj[nameof( ViewBias )] ??= 5.0f;
	}

	// Preview data must not replace the saved textures or their encoding until the bake succeeds.
	private (Texture Irradiance, Texture Distance, Texture Relocation)? _bakePreview;

	/// <summary>
	/// Cancellation source for the current bake operation.
	/// </summary>
	private CancellationTokenSource _bakeCts;

	//
	// Component Lifecycle
	//

	protected override void OnEnabled()
	{
		base.OnEnabled();
		Transform.OnTransformChanged += MarkDirty;

		LoadProbesFromRelocationTexture();
		MarkDirty();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		Transform.OnTransformChanged -= MarkDirty;

		_bakeCts?.Cancel();

		MarkDirty();
	}

	void MarkDirty()
	{
		Scene?.Get<DDGIVolumeSystem>()?.MarkDirty();
	}

	//
	// Editor Actions
	//
	[Expose, Hide]
	internal bool IsSceneSaved => Scene?.Editor?.GetSceneFolder() is not null;

	/// <summary>
	/// Explains why baking is unavailable until the scene has been saved.
	/// </summary>
	[Header( "Actions" )]
	[ShowIf( nameof( IsSceneSaved ), false )]
	[InfoBox( "Save the scene before baking indirect light volumes.", "warning", EditorTint.Yellow )]
	[Button( "Bake", "lightbulb" ), ReadOnly]
	public void BakeProbesUnavailableMessage() { }

	/// <summary>
	/// Starts the probe baking process to capture lighting into the volume textures.
	/// </summary>
	[Header( "Actions" )]
	[ShowIf( nameof( IsSceneSaved ), true )]
	[Button( "Bake", "lightbulb" )]
	public async Task BakeProbes( CancellationToken ct = default )
	{
		if ( Scene?.SceneWorld is null || !IsSceneSaved || _bakeCts is not null )
			return;

		using var bakeCts = CancellationTokenSource.CreateLinkedTokenSource( ct, GameObject.EnabledToken );
		_bakeCts = bakeCts;
		var previousProbes = Probes;
		var completed = false;

		try
		{
			bakeCts.Token.ThrowIfCancellationRequested();
			ComputeProbeRelocation();

			using var updater = new DDGIProbeUpdaterCubemapper( this );
			_bakePreview = (updater.GeneratedIrradianceTexture, updater.GeneratedDistanceTexture, GeneratedRelocationTexture);
			MarkDirty();

			if ( !await updater.RunAsync( bakeCts.Token ) || !IsSceneSaved )
				return;

			bakeCts.Token.ThrowIfCancellationRequested();
			Graphics.FlushGPU();

			// New paths preserve textures referenced by the previous scene save, including on failure.
			var bakeId = Guid.NewGuid();
			var irradiance = SaveTexture( updater.GeneratedIrradianceTexture, "Irradiance", bakeId );
			var distance = SaveTexture( updater.GeneratedDistanceTexture, "Distance", bakeId, ImageFormat.BC6H );
			var relocation = SaveTexture( GeneratedRelocationTexture, "Relocation", bakeId, ImageFormat.RGBA16161616F );

			IrradianceTexture = irradiance;
			DistanceTexture = distance;
			RelocationTexture = relocation;
			BakeVersion = CurrentBakeVersion;
			completed = true;
		}
		finally
		{
			if ( !completed )
				Probes = previousProbes;

			_bakePreview = null;
			GeneratedRelocationTexture?.Dispose();
			GeneratedRelocationTexture = null;
			_bakeCts = null;
			MarkDirty();
		}
	}

	/// <summary>
	/// Automatically sizes the volume to encompass all scene geometry.
	/// </summary>
	[Button( "Fit to Scene Bounds", "fullscreen" )]
	public void ExtendToSceneBounds()
	{
		if ( Scene is null )
			return;

		WorldScale = 1;
		WorldRotation = Rotation.Identity;
		var sceneBounds = BBox.FromPositionAndSize( WorldPosition );

		foreach ( var renderer in Scene.GetAll<Renderer>() )
		{
			if ( renderer is not IHasBounds bounds )
				continue;
			sceneBounds = sceneBounds.AddBBox( bounds.LocalBounds.Transform( renderer.WorldTransform ) );
		}
		foreach ( var terrain in Scene.GetAll<Terrain>() )
		{
			var collision = terrain.EnableCollision; // isnt great but poking around in the heightmap is worse
			terrain.EnableCollision = true;
			sceneBounds = sceneBounds.AddBBox( terrain.GetWorldBounds() );
			if ( !collision )
				terrain.EnableCollision = false;
		}
		foreach ( var mesh in Scene.GetAll<MeshComponent>() )
		{
			var model = mesh.Model;
			if ( model is null )
				continue;
			sceneBounds = sceneBounds.AddBBox( model.RenderBounds.Transform( mesh.WorldTransform ) );
		}
		sceneBounds = sceneBounds.Translate( -WorldPosition ).Grow( 16 );

		Bounds = sceneBounds;
	}


	//
	// Gizmos
	//

	private IDisposable _boundsUndoScope;

	protected override void DrawGizmos()
	{
		if ( !Gizmo.Pressed.Any )
		{
			_boundsUndoScope?.Dispose();
			_boundsUndoScope = null;
		}

		if ( !Gizmo.IsSelected )
			return;

		if ( Gizmo.Control.BoundingBox( "Bounds", Bounds, out var newBounds ) )
		{
			_boundsUndoScope ??= Scene.Editor?.UndoScope( "Resize Indirect Light Volume" ).WithComponentChanges( this ).Push();

			Bounds = newBounds;
		}

		Gizmo.Draw.LineBBox( Bounds );

		Gizmo.Draw.Color = new Color( 0.25f, 0.9f, 1, 0.05f );
		Gizmo.Draw.SolidBox( Bounds );

		// Use gizmo pooling so it follows gizmo visibility rules (hidden when gizmos disabled, not in cubemaps)
		var debugGrid = Gizmo.Active.FindOrCreate<LPVDebugGridObject>( "lpv-grid", () => new( Gizmo.World ) );

		debugGrid.UpdateGrid( WorldTransform, Bounds, ProbeCounts, 5, GpuIndex, Probes );
	}


	/// <summary>
	/// Saves texture to disk and reloads it.
	/// </summary>
	private Texture SaveTexture( Texture source, string suffix, Guid bakeId, ImageFormat? format = null )
	{
		if ( source is null || source.IsError )
			throw new InvalidOperationException( $"Cannot save invalid {suffix} probe texture." );

		var sceneFolder = Scene.Editor.GetSceneFolder();
		var filename = $"/ddgi/{GameObject?.Name ?? "DDGIVolume"}_{suffix}_{Id}_{bakeId:N}.vtex_c";

		var vtexBytes = source.SaveToVtex( format );
		var path = sceneFolder.WriteFile( filename, vtexBytes );

		var saved = Texture.Load( path );
		if ( !saved.IsValid() || saved.IsError )
			throw new InvalidOperationException( $"Could not load saved {suffix} probe texture." );

		return saved;
	}

	//
	// GPU Data
	//

	/// <summary>
	/// Builds GPU-ready structure for shader consumption.
	/// </summary>
	internal bool BuildData( out DDGIVolumeGpuData data, bool usePreview = true, bool applyAdjustments = true )
	{
		data = default;
		var preview = usePreview ? _bakePreview : null;
		var (irradiance, distance, relocation) = preview ?? (IrradianceTexture, DistanceTexture, RelocationTexture);

		if ( !irradiance.IsValid() || !distance.IsValid() || !relocation.IsValid() )
			return false;

		var probeCounts = ProbeCounts;
		var spacing = ComputeSpacing( probeCounts );
		var probeToWorld = Matrix.FromTransform( WorldTransform );

		data = new DDGIVolumeGpuData
		{
			Transform = probeToWorld.Inverted,
			ProbeToWorldTransform = probeToWorld,
			BBoxMin = Bounds.Mins,
			BBoxMax = Bounds.Maxs,
			NormalBias = NormalBias,
			ViewBias = ViewBias,
			RuntimeContrast = applyAdjustments ? RuntimeContrast : 1.0f,
			RuntimeSaturation = applyAdjustments ? RuntimeSaturation : 1.0f,
			RuntimeIntensity = applyAdjustments ? MathF.Max( RuntimeIntensity, 0.0f ) : 1.0f,
			RuntimeTint = applyAdjustments ? RuntimeTint : Color.White,
			BlendDistance = applyAdjustments ? MathF.Max( BlendDistance, 0.0f ) : 0.0f,
			ProbeSpacing = spacing,
			ProbeCellDiagonal = ProbeCellDiagonal,
			ReciprocalSpacing = new(
				spacing.x > 0.0f ? 1.0f / spacing.x : 0.0f,
				spacing.y > 0.0f ? 1.0f / spacing.y : 0.0f,
				spacing.z > 0.0f ? 1.0f / spacing.z : 0.0f
			),
			ReciprocalCountsMinusOne = new(
				probeCounts.x > 1 ? 1.0f / (probeCounts.x - 1) : 0.0f,
				probeCounts.y > 1 ? 1.0f / (probeCounts.y - 1) : 0.0f,
				probeCounts.z > 1 ? 1.0f / (probeCounts.z - 1) : 0.0f
			),
			ProbeCounts = probeCounts,
			RelocationTextureIndex = relocation.Index,
			IrradianceTextureIndex = irradiance.Index,
			DistanceTextureIndex = distance.Index,
			BakeVersion = preview.HasValue ? CurrentBakeVersion : BakeVersion,
		};

		return true;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 0 )]
	internal struct DDGIVolumeGpuData
	{
		public Matrix Transform;
		public Vector3 BBoxMin;
		public Vector3 BBoxMax;
		public float NormalBias;
		public Vector3 ProbeSpacing;

		/// <summary>
		/// World-space cell diagonal used to bound captured probe distances.
		/// </summary>
		public float ProbeCellDiagonal;
		public Vector3 ReciprocalSpacing;
		public int IrradianceTextureIndex;
		public Vector3 ReciprocalCountsMinusOne;
		public int DistanceTextureIndex;
		public Vector3Int ProbeCounts;
		public int RelocationTextureIndex;

		/// <summary>
		/// Selects legacy visibility or normalized distance-moment decoding in the shader.
		/// </summary>
		public int BakeVersion;

		/// <summary>
		/// Transforms grid positions to the world space used by captures and relocation offsets.
		/// </summary>
		public Matrix ProbeToWorldTransform;

		/// <summary>
		/// World-space sampling bias toward the camera, used by versioned bakes.
		/// </summary>
		public float ViewBias;

		/// <summary>
		/// Contrast applied after sampling, neutral during probe captures.
		/// </summary>
		public float RuntimeContrast;

		/// <summary>
		/// Saturation applied after sampling, neutral during probe captures.
		/// </summary>
		public float RuntimeSaturation;

		/// <summary>
		/// Intensity multiplier, neutral during probe captures.
		/// </summary>
		public float RuntimeIntensity;

		/// <summary>
		/// RGB tint multiplier, white during probe captures.
		/// </summary>
		public Vector3 RuntimeTint;

		/// <summary>
		/// Outward fade distance in world units, disabled during probe captures.
		/// </summary>
		public float BlendDistance;
	}

	//
	// Probe Grid Calculations
	//

	/// <summary>
	/// Calculates probe count along each axis based on volume size and density.
	/// </summary>
	private Vector3Int ComputeProbeCounts()
	{
		const float densityScale = 1.0f / 1024.0f;
		const int minProbes = 4;
		int maxProbes = ComponentVersion >= 2 ? 128 : 40;

		var size = Bounds.Size;
		var density = ProbeDensity * densityScale;

		return new Vector3Int(
			ComputeProbeCountForAxis( size.x, density ),
			ComputeProbeCountForAxis( size.y, density ),
			ComputeProbeCountForAxis( size.z, density )
		);

		int ComputeProbeCountForAxis( float axisSize, float density )
		{
			var count = (int)MathF.Ceiling( axisSize * density ) + 1;
			return Math.Clamp( count, minProbes, maxProbes );
		}
	}

	/// <summary>
	/// Calculates spacing between probes based on volume size and probe counts.
	/// </summary>
	internal Vector3 ComputeSpacing( Vector3Int counts )
	{
		var size = Bounds.Size;
		return new Vector3(
			counts.x > 1 ? size.x / (counts.x - 1) : 0.0f,
			counts.y > 1 ? size.y / (counts.y - 1) : 0.0f,
			counts.z > 1 ? size.z / (counts.z - 1) : 0.0f
		);
	}

	/// <summary>
	/// Gets the local-space position of a probe at the given grid index.
	/// </summary>
	internal Vector3 GetProbeLocalPosition( Vector3Int index )
	{
		var spacing = ComputeSpacing( ProbeCounts );
		return Bounds.Mins + index * spacing;
	}

	/// <summary>
	/// Gets the world-space position of a probe at the given grid index.
	/// </summary>
	internal Vector3 GetProbeWorldPosition( Vector3Int index )
	{
		return WorldTransform.PointToWorld( GetProbeLocalPosition( index ) );
	}

	/// <summary>
	/// Gets the relocated world position of a probe.
	/// </summary>
	internal Vector3 GetRelocatedProbeWorldPosition( Vector3Int index )
	{
		var basePosition = GetProbeWorldPosition( index );
		var probe = GetProbe( index );
		return basePosition + (probe?.Offset ?? Vector3.Zero);
	}

	/// <summary>
	/// Gets probe data at the given grid index.
	/// </summary>
	internal Probe GetProbe( Vector3Int index )
	{
		if ( Probes is null )
			return null;

		var counts = ProbeCounts;
		var flatIndex = index.x + index.y * counts.x + index.z * counts.x * counts.y;

		if ( flatIndex < 0 || flatIndex >= Probes.Length )
			return null;

		return Probes[flatIndex];
	}

	[Menu( "Editor", "Scene/Bake Indirect Light Volumes", "snowing", Priority = 1100 )]
	public static async Task BakeAll()
	{
		if ( Application.Editor is null )
			return;

		var components = Application.Editor.Scene.GetAll<IndirectLightVolume>().ToArray();

		await Application.Editor.ForEachAsync( components, "Baking Indirect Light Volumes in Scene", ( x, ct ) => x.BakeProbes( ct ) );
	}
}


