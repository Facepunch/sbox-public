using NativeEngine;
using Sandbox.Rendering;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Probe updater implementation using Cubermapper for probe radiance collection.
/// </summary>
class DDGIProbeUpdaterCubemapper : IDisposable
{
	private const int CaptureSize = 64;
	private readonly IndirectLightVolume _volume;
	private readonly SceneCamera _camera;
	private List<Vector3Int> _pendingProbes;
	private Texture _captureTexture;
	private Texture _captureDepth;
	private Texture _firstPassIrradiance;
	private GpuBuffer<IndirectLightVolume.DDGIVolumeGpuData> _captureVolumes;
	private readonly List<Texture> _captureTextures = new();
	private int _captureVolumeCount;
	private int _pass;

	/// <summary>
	/// Volume texture storing probe irradiance data (color).
	/// </summary>
	public Texture GeneratedIrradianceTexture { get; set; }

	/// <summary>
	/// Volume texture storing probe distance/visibility data.
	/// </summary>
	public Texture GeneratedDistanceTexture { get; set; }

	Vector3Int _renderedIndex = 0;

	// Made by the constructor, on the main thread. As static initializers they were first reached from the camera's stage hook,
	// on a thread recording a face, where Material.Create throws: the type failed to initialize, and the bake integrated nothing.
	static ComputeShader DepthCopyShader;
	static ComputeShader IntegrateShader;

	/// <summary>
	/// Creates the capture camera and temporary textures for one volume bake.
	/// </summary>
	public DDGIProbeUpdaterCubemapper( IndirectLightVolume volume )
	{
		_volume = volume;
		_pendingProbes = new List<Vector3Int>();

		DepthCopyShader ??= new ComputeShader( "common/DDGI/ddgi_depth_copy_cs" );
		IntegrateShader ??= new ComputeShader( "common/DDGI/ddgi_integrate_cs" );

		// Initialize camera for probe captures
		_camera = new SceneCamera
		{
			World = volume.Scene.SceneWorld,
			Rotation = Rotation.Identity,
			FieldOfView = 90.0f,
			ClearFlags = ClearFlags.All,
			BackgroundColor = Color.Black,
			AmbientLightColor = Color.Black,
		};

		if ( volume.RenderExcludeTags is not null )
		{
			_camera.ExcludeTags.SetFrom( volume.RenderExcludeTags );
		}

		// Hide In Game meshes are still visible in the editor, keep them out of bakes
		_camera.ExcludeTags.Add( MeshComponent.HideInGameTag );

		_camera.OnRenderStageHook += ( stage, camera ) =>
		{
			if ( stage != Stage.AfterTransparent )
				return;

			// A cube face's depth is separate; copy it into the array slice with a compute shader.
			int face = Graphics.CubeFace;
			if ( Graphics.DepthTexture is { } depthTexture )
				CopyDepthToColor( depthTexture, _captureDepth, face );

			// Recording order is arbitrary; GPU submission follows the face order.
			if ( face == 5 )
				OnRenderFinish( _renderedIndex );
		};

		// Initialize cube texture for capturing radiance
		InitializeCaptureTexture();

		// Initialize probe queue
		InitializeProbeQueue();

		EnsureResources();
	}

	/// <summary>
	/// Copies depth texture values to a color texture array slice using a compute shader.
	/// </summary>
	void CopyDepthToColor( Texture srcDepth, Texture dstColor, int dstArraySlice )
	{
		var attrs = RenderAttributes.Pool.Get();
		attrs.Set( "SourceDepth", srcDepth );
		attrs.Set( "DestTextureArray", dstColor );
		attrs.Set( "TextureSize", new Vector2Int( CaptureSize, CaptureSize ) );
		attrs.Set( "DestArraySlice", dstArraySlice );

		DepthCopyShader.DispatchWithAttributes( attrs, CaptureSize, CaptureSize, 1 );
		RenderAttributes.Pool.Return( attrs );
	}

	void EnsureResources()
	{
		var counts = _volume.ProbeCounts;

		// Irradiance: 8x8 octahedral map per probe (borderless)
		const int irradianceOctSize = 8;
		var irradianceSize = new Vector3Int( counts.x * irradianceOctSize, counts.y * irradianceOctSize, counts.z );
		GeneratedIrradianceTexture = CreateVolumeTexture( irradianceSize, ImageFormat.RGBA16161616F, $"Irradiance" );

		// Distance: 16x16 octahedral map per probe (borderless)
		const int distanceOctSize = 16;
		var distanceSize = new Vector3Int( counts.x * distanceOctSize, counts.y * distanceOctSize, counts.z );
		GeneratedDistanceTexture = CreateVolumeTexture( distanceSize, ImageFormat.RGBA16161616F, $"Distance" );
	}

	private Texture CreateVolumeTexture( Vector3Int size, ImageFormat format, string name )
	{
		return Texture.CreateVolume( size.x, size.y, size.z, format )
			.WithName( name )
			.WithUAVBinding()
			.Finish();
	}

	/// <summary>
	/// Captures direct lighting, then one bounce from the completed first pass.
	/// Returns false when either cancellation source interrupts the bake.
	/// </summary>
	public async Task<bool> RunAsync( CancellationToken token, Editor.IProgressSection section = null )
	{
		using var popup = section is null ? Application.Editor.ProgressSection() : null;
		var progress = section ?? popup;

		var totalCaptures = _pendingProbes.Count * 2;
		progress.Title = "Baking Indirect Light Volume";
		progress.TotalCount = totalCaptures;
		var progressToken = progress.GetCancel();

		await Task.Yield();

		int completedCaptures = 0;
		var pauseTimer = FastTimer.StartNew();

		for ( _pass = 0; _pass < 2; _pass++ )
		{
			BeginPass();
			foreach ( var probeIndex in _pendingProbes )
			{
				if ( token.IsCancellationRequested || progressToken.IsCancellationRequested )
					return false;

				RenderProbe( probeIndex );
				completedCaptures++;

				// Update the editor between slices of capture work.
				if ( pauseTimer.ElapsedMilliSeconds >= 20 || completedCaptures == totalCaptures )
				{
					progress.Subtitle = $"Rendering probe {completedCaptures:n0} / {totalCaptures:n0}";
					progress.Current = completedCaptures;
					await Task.Delay( 1 );
					pauseTimer.Start();
				}
			}
		}

		if ( token.IsCancellationRequested || progressToken.IsCancellationRequested )
			return false;

		progress.Subtitle = $"Complete!";

		return true;
	}

	/// <summary>
	/// Start a pass: the second keeps the completed first as a frozen bounce source, and each captures with the volumes as they
	/// are when it starts.
	/// </summary>
	void BeginPass()
	{
		if ( _pass == 1 )
		{
			// The volume preview keeps the completed first pass as a frozen bounce source.
			var irradiance = CreateVolumeTexture(
				new Vector3Int( GeneratedIrradianceTexture.Width, GeneratedIrradianceTexture.Height, GeneratedIrradianceTexture.Depth ),
				ImageFormat.RGBA16161616F, "Irradiance" );
			_firstPassIrradiance = GeneratedIrradianceTexture;
			GeneratedIrradianceTexture = irradiance;
		}

		UpdateCaptureVolumes();
	}

	/// <summary>
	/// The renderer probes capture through, rather than the frame's (<see cref="SceneRenderer.Current"/>): for tools that
	/// compare them.
	/// </summary>
	internal ISceneRenderer Renderer { get; set; }

	/// <summary>
	/// Bake both passes now, without the editor's progress or yielding between probes: for tools and tests.
	/// </summary>
	internal void BakeNow()
	{
		for ( _pass = 0; _pass < 2; _pass++ )
		{
			BeginPass();
			foreach ( var probeIndex in _pendingProbes )
			{
				PrepareProbe( probeIndex );
				CaptureProbe();
			}
		}
	}

	/// <summary>
	/// Captures use a fixed set of volume textures for each bounce, independent of editor updates.
	/// </summary>
	private void UpdateCaptureVolumes()
	{
		_captureTextures.Clear();
		var data = new List<IndirectLightVolume.DDGIVolumeGpuData>();
		foreach ( var volume in _volume.Scene.GetAll<IndirectLightVolume>().OrderBy( x => x.Bounds.Volume ) )
		{
			if ( !volume.Active || !volume.Enabled || (_pass == 0 && volume == _volume) )
				continue;

			// Retain other volumes' saved textures for the whole pass, even if they finish a rebake.
			if ( volume.BuildData( out var entry, usePreview: volume == _volume, applyAdjustments: false ) )
			{
				data.Add( entry );
				if ( volume != _volume )
				{
					_captureTextures.Add( volume.IrradianceTexture );
					_captureTextures.Add( volume.DistanceTexture );
					_captureTextures.Add( volume.RelocationTexture );
				}
			}
		}

		_captureVolumes?.Dispose();
		_captureVolumeCount = data.Count;
		_captureVolumes = new GpuBuffer<IndirectLightVolume.DDGIVolumeGpuData>( Math.Max( 1, data.Count ) );
		if ( data.Count > 0 )
			_captureVolumes.SetData( data );
	}

	private void RenderProbe( Vector3Int probeIndex )
	{
		PrepareProbe( probeIndex );
		CaptureProbe();
	}

	/// <summary>
	/// Move the capture camera to a probe, with the scene's attributes and the volumes the pass captures with.
	/// </summary>
	void PrepareProbe( Vector3Int probeIndex )
	{
		_renderedIndex = probeIndex;

		_camera.Position = _volume.GetRelocatedProbeWorldPosition( probeIndex );
		_camera.Attributes.Set( "NoPrepassCulling", true );

		// Merge our global Scene attributes into the camera
		_volume.Scene.RenderAttributes.MergeTo( _camera.Attributes );
		_camera.Attributes.Set( "DDGI_Volumes", _captureVolumes );
		_camera.Attributes.Set( "DDGI_VolumeCount", _captureVolumeCount );
	}

	/// <summary>
	/// Capture the probe's cube; the camera's stage hook copies each face's depth and integrates the probe after the sixth.
	/// </summary>
	void CaptureProbe()
	{
		foreach ( var texture in _captureTextures )
			texture.MarkUsed( ushort.MaxValue );

		if ( Renderer is null ) _camera.RenderToCubeTexture( _captureTexture );
		else Renderer.RenderToCube( _camera, _captureTexture, default );
	}

	private void InitializeProbeQueue()
	{
		var counts = _volume.ProbeCounts;
		for ( int z = 0; z < counts.z; z++ )
		{
			for ( int y = 0; y < counts.y; y++ )
			{
				for ( int x = 0; x < counts.x; x++ )
				{
					var index = new Vector3Int( x, y, z );
					if ( _volume.GetProbe( index )?.Active == false )
						continue;

					_pendingProbes.Add( index );
				}
			}
		}
	}

	private void InitializeCaptureTexture()
	{
		int numMips = (int)MathF.Log2( CaptureSize ) + 1;

		_captureTexture = Texture.CreateCube( CaptureSize, CaptureSize )
								.WithUAVBinding()
								.WithMips( numMips )
								.WithFormat( ImageFormat.RGBA16161616F )
								.Finish();

		_captureDepth = Texture.CreateCube( CaptureSize, CaptureSize )
								.WithUAVBinding()
								.WithFormat( ImageFormat.R32F )
								.Finish();
	}

	private void OnRenderFinish( Vector3Int probeIndex )
	{
		Integrate( probeIndex );
	}


	private void Integrate( Vector3Int probeIndex )
	{
		// Encode our sample into the volume's textures
		var attrs = RenderAttributes.Pool.Get();
		attrs.Set( "SourceProbe", _captureTexture );
		attrs.Set( "SourceDepth", _captureDepth );

		attrs.Set( "IrradianceVolume", GeneratedIrradianceTexture );
		attrs.Set( "DistanceVolume", GeneratedDistanceTexture );

		attrs.Set( "ProbeIndex", probeIndex );
		attrs.Set( "ProbeCounts", _volume.ProbeCounts );
		// Farther geometry cannot occlude any point that samples this probe. Including it
		// inflates variance at silhouettes and lets exterior probes leak through walls.
		attrs.Set( "ProbeMaxDistance", Math.Max( 1.0f, _volume.ProbeCellDiagonal + (_volume.GetProbe( probeIndex )?.Offset.Length ?? 0.0f) ) );

		// Irradiance pass (8x8 borderless tile)
		attrs.SetCombo( "D_PASS", 0 );
		IntegrateShader.DispatchWithAttributes( attrs, 1, 1, 1 );

		// Geometry is unchanged between bounces; keep the first pass distance data read-only.
		if ( _pass == 0 )
		{
			attrs.SetCombo( "D_PASS", 1 );
			IntegrateShader.DispatchWithAttributes( attrs, 1, 1, 1 );
		}

		RenderAttributes.Pool.Return( attrs );
	}

	/// <summary>
	/// Releases temporary bake resources and references to shared saved textures.
	/// </summary>
	public void Dispose()
	{
		_camera?.Dispose();
		_captureTexture?.Dispose();
		_captureDepth?.Dispose();
		_captureVolumes?.Dispose();
		_captureTextures.Clear();
		_firstPassIrradiance?.Dispose();

		GeneratedIrradianceTexture?.Dispose();
		GeneratedDistanceTexture?.Dispose();
	}

}
