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

	/// <summary>
	/// Volume texture storing probe irradiance data (color).
	/// </summary>
	public Texture GeneratedIrradianceTexture { get; set; }

	/// <summary>
	/// Volume texture storing probe distance/visibility data.
	/// </summary>
	public Texture GeneratedDistanceTexture { get; set; }

	int _renderedFace = 0;
	Vector3Int _renderedIndex = 0;

	static ComputeShader DepthCopyShader = new ComputeShader( "common/DDGI/ddgi_depth_copy_cs" );
	static ComputeShader IntegrateShader = new ComputeShader( "common/DDGI/ddgi_integrate_cs" );

	public DDGIProbeUpdaterCubemapper( IndirectLightVolume volume )
	{
		_volume = volume;
		_pendingProbes = new List<Vector3Int>();

		// Initialize camera for probe captures
		_camera = new SceneCamera
		{
			World = volume.Scene.SceneWorld,
			Rotation = Rotation.Identity,
			ClearFlags = ClearFlags.All,
			BackgroundColor = Color.Black,
			AmbientLightColor = Color.Black,
		};

		_camera.OnRenderStageHook += ( stage, camera ) =>
		{
			if ( stage != Stage.AfterTransparent )
				return;

			// We don't render depth to cubemap, so we copy it manually using a compute shader
			var depth = Texture.FromNative( Graphics.SceneLayer.GetDepthTarget() );

			CopyDepthToColor( depth, _captureDepth, _renderedFace );

			depth.Dispose();

			_renderedFace++;

			if ( _renderedFace == 6 )
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
		var width = srcDepth.Width;
		var height = srcDepth.Height;

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

		// Irradiance: 6x6 octahedral map + 2 pixel border per probe
		const int irradianceOctSize = 8;
		var irradianceSize = new Vector3Int( counts.x * irradianceOctSize, counts.y * irradianceOctSize, counts.z );
		GeneratedIrradianceTexture = CreateVolumeTexture( irradianceSize, ImageFormat.RGBA16161616F, $"Irradiance" );

		// Distance: 14x14 octahedral map + 2 pixel border per probe
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

	public async Task<bool> RunAsync( CancellationToken token )
	{
		FastTimer pauseTimer = FastTimer.StartNew();

		using var progress = Application.Editor.ProgressSection();

		progress.Title = "Baking Indirect Light Volume";
		progress.TotalCount = _pendingProbes.Count;
		var progressToken = progress.GetCancel();

		while ( _pendingProbes.Count > 0 )
		{
			progress.Subtitle = $"Rendering probe {progress.Current + 1:n0} / {progress.TotalCount:n0}";

			if ( !await RenderProbe() )
				return false;

			if ( token.IsCancellationRequested || progressToken.IsCancellationRequested )
			{
				progress.Subtitle = $"Cancelled!";
				return false;
			}

			progress.Current++;

			if ( pauseTimer.ElapsedMilliSeconds > 20 )
			{
				await Task.Delay( 1 );
			}
		}

		progress.Subtitle = $"Complete!";

		return true;
	}

	private async Task<bool> RenderProbe()
	{
		if ( _pendingProbes.Count() == 0 )
			return false;

		var probeIndex = _pendingProbes.First();
		_pendingProbes.RemoveAt( 0 );

		_renderedFace = 0;
		_renderedIndex = probeIndex;

		_camera.Position = _volume.GetRelocatedProbeWorldPosition( probeIndex );
		_camera.Attributes.Set( "NoPrepassCulling", true );

		// Merge our global Scene attributes into the camera
		_volume.Scene.RenderAttributes.MergeTo( _camera.Attributes );

		await Task.Yield();

		_camera.RenderToCubeTexture( _captureTexture );

		await Task.Yield();

		return true;
	}

	private void InitializeProbeQueue()
	{
		var scene = _volume.Scene;
		if ( scene is null )
			return;

		var probeCounts = _volume.ProbeCounts;
		var halfExtents = _volume.ComputeSpacing( probeCounts ) * _volume.WorldTransform.Scale.Abs();
		var probeBounds = new BBox( -halfExtents, halfExtents );

		var emptyProbesList = new List<Vector3Int>();

		for ( int z = 0; z < probeCounts.z; z++ )
		{
			for ( int y = 0; y < probeCounts.y; y++ )
			{
				for ( int x = 0; x < probeCounts.x; x++ )
				{
					var probeIndex = new Vector3Int( x, y, z );
					var probePosition = _volume.GetProbeWorldPosition( probeIndex );

					var trace = scene.Trace.Box( probeBounds, probePosition, probePosition )
							.IgnoreGameObjectHierarchy( _volume.GameObject )
							.Run();

					if ( trace.Hit )
						_pendingProbes.Add( probeIndex );
					else
						emptyProbesList.Add( probeIndex );
				}
			}
		}

		// Make probes closer to the camera and that affect geometry render first
		/*var eye = Application.Editor.Camera.WorldTransform;
		_pendingProbes = _pendingProbes.OrderBy( x =>
		{
			return Vector3.DistanceBetween( _volume.GetProbeWorldPosition( x ), eye.Position );
		} ).ToList();*/

		// Append empty probes at the end of the list
		_pendingProbes.AddRange( emptyProbesList );
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

		attrs.Set( "IrradianceVolume", _volume.IrradianceTexture );
		attrs.Set( "DistanceVolume", _volume.DistanceTexture );

		attrs.Set( "ProbeIndex", probeIndex );
		attrs.Set( "ProbeCounts", _volume.ProbeCounts );
		attrs.Set( "EnergyLoss", _volume.Contrast );

		// Irradiance pass (6x6 interior + 2 border = 8x8 tile)
		attrs.SetCombo( "D_PASS", 0 );
		IntegrateShader.DispatchWithAttributes( attrs, 1, 1, 1 );

		// Distance pass (14x14 interior + 2 border = 16x16 tile)
		attrs.SetCombo( "D_PASS", 1 );
		IntegrateShader.DispatchWithAttributes( attrs, 1, 1, 1 );

		RenderAttributes.Pool.Return( attrs );
	}

	public void Dispose()
	{
		_camera?.Dispose();
		_captureTexture?.Dispose();
		_captureDepth?.Dispose();

		GeneratedIrradianceTexture?.Dispose();
		GeneratedDistanceTexture?.Dispose();
	}

}
