using NativeEngine;
using Sandbox.Rendering;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Probe updater implementation using Cubermapper for probe radiance collection.
/// </summary>
class DDGIProbeUpdaterCubemapper : IDisposable
{
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

	static ComputeShader DepthCopyShader = new ComputeShader( "common/DDGI/ddgi_depth_copy_cs" );
	static ComputeShader IntegrateShader = new ComputeShader( "common/DDGI/ddgi_integrate_cs" );

	/// <summary>
	/// Creates the capture camera and temporary textures for one volume bake.
	/// </summary>
	public DDGIProbeUpdaterCubemapper( IndirectLightVolume volume )
	{
		_volume = volume;
		_pendingProbes = new List<Vector3Int>();

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

			// We don't render depth to cubemap, so we copy it manually using a compute shader
			int face = Graphics.SceneView.GetRenderTargetArrayIndex();
			var depth = Graphics.GetDepthTarget( out var owned );
			try
			{
				CopyDepthToColor( depth, _captureDepth, face );
			}
			finally
			{
				if ( owned && !depth.IsNull ) depth.DestroyStrongHandle();
			}

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
	void CopyDepthToColor( ITexture srcDepth, Texture dstColor, int dstArraySlice )
	{
		var width = dstColor.Width;
		var height = dstColor.Height;

		var attrs = RenderAttributes.Pool.Get();
		attrs.Set( "SourceDepth", srcDepth );
		attrs.Set( "DestTextureArray", dstColor );
		attrs.Set( "TextureSize", new Vector2Int( width, height ) );
		attrs.Set( "DestArraySlice", dstArraySlice );

		DepthCopyShader.DispatchWithAttributes( attrs, width, height, 1 );
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
	public async Task<bool> RunAsync( CancellationToken token )
	{
		FastTimer pauseTimer = FastTimer.StartNew();

		using var progress = Application.Editor.ProgressSection();

		progress.Title = "Baking Indirect Light Volume";
		progress.TotalCount = _pendingProbes.Count * 2;
		var progressToken = progress.GetCancel();

		for ( _pass = 0; _pass < 2; _pass++ )
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
			foreach ( var probeIndex in _pendingProbes )
			{
				if ( token.IsCancellationRequested || progressToken.IsCancellationRequested )
					return false;

				progress.Subtitle = $"Rendering probe {progress.Current + 1:n0} / {progress.TotalCount:n0}";
				if ( !await RenderProbe( probeIndex, token, progressToken ) )
					return false;
				if ( token.IsCancellationRequested || progressToken.IsCancellationRequested )
					return false;

				progress.Current++;

				if ( pauseTimer.ElapsedMilliSeconds > 20 )
				{
					await Task.Delay( 1 );
				}
			}
		}

		if ( token.IsCancellationRequested || progressToken.IsCancellationRequested )
			return false;

		progress.Subtitle = $"Complete!";

		return true;
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

	private async Task<bool> RenderProbe( Vector3Int probeIndex, CancellationToken token, CancellationToken progressToken )
	{
		_renderedIndex = probeIndex;

		_camera.Position = _volume.GetRelocatedProbeWorldPosition( probeIndex );
		_camera.Attributes.Set( "NoPrepassCulling", true );

		// Merge our global Scene attributes into the camera
		_volume.Scene.RenderAttributes.MergeTo( _camera.Attributes );
		_camera.Attributes.Set( "DDGI_Volumes", _captureVolumes );
		_camera.Attributes.Set( "DDGI_VolumeCount", _captureVolumeCount );

		await Task.Yield();

		if ( token.IsCancellationRequested || progressToken.IsCancellationRequested )
			return false;

		foreach ( var texture in _captureTextures )
			texture.MarkUsed( ushort.MaxValue );

		_camera.RenderToCubeTexture( _captureTexture );

		await Task.Yield();
		return true;
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
					_pendingProbes.Add( new Vector3Int( x, y, z ) );
				}
			}
		}
	}

	private void InitializeCaptureTexture()
	{
		const int cubemapSize = 64;
		int numMips = (int)MathF.Log2( cubemapSize ) + 1;

		_captureTexture = Texture.CreateCube( cubemapSize, cubemapSize )
								.WithUAVBinding()
								.WithMips( numMips )
								.WithFormat( ImageFormat.RGBA16161616F )
								.Finish();

		_captureDepth = Texture.CreateCube( cubemapSize, cubemapSize )
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
