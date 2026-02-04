namespace Sandbox;

using Editor;
using Facepunch.ActionGraphs;
using System;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// Dynamic Diffuse Global Illumination volume that provides indirect lighting using a 3D probe grid.
/// Probes store irradiance and distance data in volume textures that can be sampled by shaders.
/// </summary>
[Expose]
[Title( "Indirect Light Volume (DDGI)" )]
[Category( "Rendering" )]
[Icon( "grid_view" )]
[EditorHandle( "materials/gizmo/lpv.png" )]
[Alias( "DDGIVolume" )]
public sealed partial class IndirectLightVolume : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Behavior when a probe is detected inside geometry.
	/// Relocation moves the probe out of geometry to reduce artifacts, while Deactivate simply disables the occluded probe, sealing leaks entirely.
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
		public Vector3 Offset { get; set; }
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
	/// World-space bounding box that defines the volume coverage area.
	/// </summary>
	[Property, MakeDirty]
	public BBox Bounds { get; set; } = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 512.0f ) );

	/// <summary>
	/// Number of probes per 1024 world units. Higher values increase probe resolution.
	/// </summary>
	[Property, Range( 1, 15 ), MakeDirty]
	public int ProbeDensity { get; set; } = 8;

	/// <summary>
	/// Bias applied along surface normals to prevent self-occlusion artifacts.
	/// </summary>
	[Group( "Advanced Settings" )]
	[Property, Range( -0.0f, 50.0f ), MakeDirty]
	public float NormalBias { get; set; } = 5.0f;

	/// <summary>
	/// Controls how much less energy to conserve during probe integration.
	/// Higher values give a harsher, more contrasty look.
	/// </summary>
	[Property, Range( 1.0f, 2.0f ), MakeDirty]
	[Group( "Advanced Settings" )]
	public float Contrast { get; set; } = 1.0f;

	/// <summary>
	/// Calculated probe count along each axis based on bounds and density.
	/// </summary>
	public Vector3Int ProbeCounts => ComputeProbeCounts();

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
	/// Cancellation source for the current bake operation.
	/// </summary>
	private CancellationTokenSource _bakeCts;

	//
	// Component Lifecycle
	//

	protected override void OnEnabled()
	{
		base.OnEnabled();
		Transform.OnTransformChanged += OnDirty;

		LoadProbesFromRelocationTexture();
		OnDirty();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		Transform.OnTransformChanged -= OnDirty;

		_bakeCts?.Cancel();
		_bakeCts?.Dispose();
		_bakeCts = null;

		Scene.Get<DDGIVolumeSystem>()?.MarkDirty();
	}

	protected override void OnDirty()
	{
		base.OnDirty();
		Scene.Get<DDGIVolumeSystem>()?.MarkDirty();
	}

	//
	// Editor Actions
	//

	/// <summary>
	/// Starts the probe baking process to capture lighting into the volume textures.
	/// </summary>
	[Button( "Bake", "lightbulb" )]
	public async Task BakeProbes( CancellationToken ct = default )
	{
		if ( Scene?.SceneWorld is null )
			return;

		// Cancel any existing bake operation
		_bakeCts?.Cancel();
		_bakeCts?.Dispose();
		_bakeCts = new CancellationTokenSource();

		// Not needed if GPU RT
		ComputeProbeRelocation();

		using var updater = new DDGIProbeUpdaterCubemapper( this );

		// Update for preview
		IrradianceTexture = updater.GeneratedIrradianceTexture;
		DistanceTexture = updater.GeneratedDistanceTexture;
		RelocationTexture = GeneratedRelocationTexture;
		Scene.Get<DDGIVolumeSystem>()?.MarkDirty();

		using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource( ct, _bakeCts.Token, GameObject.EnabledToken );

		if ( !await updater.RunAsync( linkedCt.Token ) )
		{
			IrradianceTexture = default;
			DistanceTexture = default;
			RelocationTexture = default;
		}
		else
		{
			// Make sure all GPU work is done before saving textures
			Graphics.FlushGPU();

			IrradianceTexture = SaveTexture( updater.GeneratedIrradianceTexture, "Irradiance" );
			DistanceTexture = SaveTexture( updater.GeneratedDistanceTexture, "Distance", ImageFormat.RG1616F ); // BC6H ideally, but block compression fucks precision too much
			RelocationTexture = SaveTexture( GeneratedRelocationTexture, "Relocation", ImageFormat.RGBA16161616F );
		}

		Scene.Get<DDGIVolumeSystem>()?.MarkDirty();
	}

	/// <summary>
	/// Automatically sizes the volume to encompass all scene geometry.
	/// </summary>
	[Button( "Fit to Scene Bounds", "fullscreen" )]
	public void ExtendToSceneBounds()
	{
		if ( Scene is null )
			return;

		WorldPosition = Vector3.Zero;
		var sceneBounds = new BBox();

		foreach ( var obj in Scene.SceneWorld.SceneObjects )
		{
			if ( obj.Bounds.Volume > 10000000 ) // Skip skybox
				continue;

			sceneBounds = sceneBounds.AddBBox( obj.Bounds );
		}

		Bounds = sceneBounds;
	}


	//
	// Gizmos
	//

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		var bounds = Bounds;
		Gizmo.Control.BoundingBox( "Bounds", bounds, out bounds );
		Gizmo.Draw.LineBBox( bounds );
		Bounds = bounds;

		// Use gizmo pooling so it follows gizmo visibility rules (hidden when gizmos disabled, not in cubemaps)
		var debugGrid = Gizmo.Active.FindOrCreate<LPVDebugGridObject>( "lpv-grid", () => new( Gizmo.World ) );

		debugGrid.UpdateGrid( WorldTransform, Bounds, ProbeCounts, 10, Probes );
	}


	/// <summary>
	/// Saves texture to disk and reloads it.
	/// </summary>
	private Texture SaveTexture( Texture source, string suffix, ImageFormat? format = null )
	{
		if ( source is null || source.IsError )
			return source;

		if ( Scene.Editor is null )
			return source;

		var sceneFolder = Scene.Editor.GetSceneFolder();
		var filename = $"/ddgi/{GameObject?.Name ?? "DDGIVolume"}_{suffix}_{Id}.vtex_c";

		var vtexBytes = source.SaveToVtex( format );
		var path = sceneFolder.WriteFile( filename, vtexBytes );

		var saved = Texture.Load( path );
		if ( saved is not null )
			source.Dispose();

		return saved ?? source;
	}

	//
	// GPU Data
	//

	/// <summary>
	/// Builds GPU-ready structure for shader consumption.
	/// </summary>
	internal bool BuildData( out DDGIVolumeGpuData data )
	{
		data = default;

		if ( !IrradianceTexture.IsValid() || !DistanceTexture.IsValid() || !RelocationTexture.IsValid() )
			return false;

		var probeCounts = ProbeCounts;
		var spacing = ComputeSpacing( probeCounts );

		data = new DDGIVolumeGpuData
		{
			Transform = Matrix.FromTransform( WorldTransform ).Inverted,
			BBoxMin = Bounds.Mins,
			BBoxMax = Bounds.Maxs,
			NormalBias = NormalBias,
			ProbeSpacing = spacing,
			BlendDistance = 0.0f,
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
			RelocationTextureIndex = RelocationTexture.Index,
			IrradianceTextureIndex = IrradianceTexture.Index,
			DistanceTextureIndex = DistanceTexture.Index,
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
		public float BlendDistance;
		public Vector3 ReciprocalSpacing;
		public int IrradianceTextureIndex;
		public Vector3 ReciprocalCountsMinusOne;
		public int DistanceTextureIndex;
		public Vector3Int ProbeCounts;
		public int RelocationTextureIndex;
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
		const int maxProbes = 40;

		var size = Bounds.Size;
		var density = ProbeDensity * densityScale;

		return new Vector3Int(
			ComputeProbeCountForAxis( size.x, density ),
			ComputeProbeCountForAxis( size.y, density ),
			ComputeProbeCountForAxis( size.z, density )
		);

		static int ComputeProbeCountForAxis( float axisSize, float density )
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


