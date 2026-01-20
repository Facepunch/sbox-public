using System.Diagnostics;

namespace Editor.Assets;


public class AssetPreview : IDisposable
{
	public Asset Asset { get; private set; }

	public Scene Scene { get; private set; }

	public CameraComponent Camera => Scene.Camera;

	public Vector3 SceneCenter;
	public Vector3 SceneSize;
	public Vector2Int ScreenSize = 100;
	public GameObject PrimaryObject;

	/// <summary>
	/// If you want to slow down the cycle speed when displaying in a scene widget, you can do that here
	/// </summary>
	public virtual float PreviewWidgetCycleSpeed => 1.0f;

	/// <summary>
	/// If true, this preview will render the thumbnail multiple times in the cycle and pick the one with the least alpha and most luminance
	/// </summary>
	public virtual bool UsePixelEvaluatorForThumbs => false;

	/// <summary>
	/// Is this preview animated? If it's not animated then it's a waste of time rendering a video.
	/// </summary>
	public virtual bool IsAnimatedPreview => true;

	/// <summary>
	/// How long should the video be
	/// </summary>
	public virtual float VideoLength => 3.0f;

	protected bool IsRenderingThumbnail { get; private set; }
	protected bool IsRenderingVideo { get; private set; }

	public AssetPreview( Asset asset )
	{
		Asset = asset;
	}

	public virtual void Dispose()
	{
		Asset = null;

		Scene?.Destroy();
		Scene = default;
	}

	/// <summary>
	/// Create the world, camera, lighting
	/// </summary>
	public virtual Task InitializeScene()
	{
		Scene = Scene.CreateEditorScene();
		Scene.Name = "Asset Preview";

		using ( Scene.Push() )
		{
			{
				var go = new GameObject( true, "camera" );
				var cc = go.AddComponent<CameraComponent>();
				cc.BackgroundColor = Color.Transparent;
				cc.WorldRotation = new Angles( 20, 180 + 45, 0 );
				cc.FieldOfView = 30.0f;
				cc.ZFar = 15000.0f;
				cc.ZNear = 0.1f;
			}

			{
				var go = new GameObject( true, "ambient" );
				var c = go.AddComponent<AmbientLight>();
				c.Color = Color.Cyan * 0.05f;
			}

			// lighting
			var right = Scene.Camera.WorldRotation.Right;

			{
				var go = new GameObject( true, "sun" );
				var sun = go.AddComponent<DirectionalLight>();
				sun.Shadows = true;
				sun.WorldRotation = new Angles( 50, 45, 0 );
				sun.LightColor = Color.White * 0.6f;
			}

			{
				var go = new GameObject( true, "envmap" );
				var c = go.AddComponent<EnvmapProbe>();
				c.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
				c.Texture = Texture.Load( "textures/cubemaps/default2.vtex" );
				c.Bounds = BBox.FromPositionAndSize( Vector3.Zero, 100000 );
			}
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Create the model or whatever
	/// </summary>
	public virtual Task InitializeAsset()
	{
		return Task.CompletedTask;
	}

	/// <summary>
	/// Create a widget to always show in the asset preview
	/// </summary>
	public virtual Widget CreateWidget( Widget parent )
	{
		return null;
	}

	/// <summary>
	/// Create a widget to show only when hovering over the asset preview
	/// </summary>
	public virtual Widget CreateToolbar()
	{
		return null;
	}

	public void FrameScene()
	{
		var distance = MathX.SphereCameraDistance( SceneSize.Length * 0.5f, Camera.FieldOfView );
		var aspect = (float)ScreenSize.x / ScreenSize.y;
		if ( aspect > 1 ) distance *= aspect;

		Camera.WorldPosition = (PrimaryObject.WorldRotation * SceneCenter) + Camera.WorldRotation.Forward * -distance;
	}

	float _time;

	/// <summary>
	/// Cycle is a float 0-1, timestep is the time since the last frame
	/// </summary>
	public virtual void UpdateScene( float cycle, float timeStep )
	{
		using ( Scene.Push() )
		{
			if ( PrimaryObject.IsValid() )
			{
				PrimaryObject.WorldRotation = new Angles( 0, (cycle * 360.0f), 0 );
				FrameScene();
			}
		}

		TickScene( timeStep );
	}

	public void TickScene( float timeStep )
	{
		using ( Scene.Push() )
		{
			Scene.EditorTick( _time += timeStep, timeStep );
		}
	}

	public async Task<byte[]> CreateVideo( float secondsLength, VideoWriter.Config config )
	{
		var path = System.IO.Path.GetTempFileName();

		//Camera.Size = new Vector2( config.Width, config.Height );
		Camera.BackgroundColor = "#32415e";

		var writer = EditorUtility.CreateVideoWriter( path, config );

		var frameRate = config.FrameRate;
		var frameStep = 1.0f / frameRate;
		var frames = secondsLength * frameRate;

		var timeTaken = Stopwatch.StartNew();
		using var bitmap = new Bitmap( config.Width, config.Height );
		IsRenderingVideo = true;

		for ( float i = 0; i < frames; i += 1.0f )
		{
			float delta = i / frames;
			UpdateScene( delta, frameStep );

			Camera.RenderToBitmap( bitmap );
			writer.AddFrame( bitmap );

			if ( timeTaken.Elapsed.TotalMilliseconds > 1.5f )
			{
				await Task.Delay( 1 );
				timeTaken.Restart();
			}
		}

		await writer.FinishAsync();
		writer.Dispose();

		IsRenderingVideo = false;

		var bytes = await System.IO.File.ReadAllBytesAsync( path );

		// delete temporary file
		System.IO.File.Delete( path );

		return bytes;
	}

	public virtual Task RenderToPixmap( Pixmap pixmap )
	{
		Camera.RenderToPixmap( pixmap );
		return Task.CompletedTask;
	}

	public virtual Task RenderToBitmap( Bitmap bitmap )
	{
		ScreenSize = bitmap.Size;
		Camera.RenderToBitmap( bitmap );
		return Task.CompletedTask;
	}

	[Asset.ThumbnailRenderer]
	public static async Task<Bitmap> RenderAssetThumbnail( Asset asset )
	{
		using AssetPreview v = CreateForAsset( asset );

		// unsupported
		if ( v is null )
			return null;

		v.IsRenderingThumbnail = true;
		await v.InitializeScene();
		await v.InitializeAsset();

		Bitmap best = null;
		double bestPixels = 0;

		//
		// Render multiple times, pick the one with the best alpha
		// (unless UsePixelEvaluatorForThumbs is false)
		//
		for ( float f = 0.0f; f < 1.0f; f += 0.1f )
		{
			v.UpdateScene( f, 0.2f );

			var pix = new Bitmap( 256, 256 );
			v.ScreenSize = pix.Size;
			await v.RenderToBitmap( pix );

			if ( !v.UsePixelEvaluatorForThumbs )
			{
				best = pix;
				break;
			}

			double pixels = 0;
			for ( int x = 0; x < pix.Width; x += 1 )
				for ( int y = 0; y < pix.Height; y += 1 )
				{
					var c = pix.GetPixel( x, y );
					pixels += c.a;
					pixels += c.Luminance;
				}

			if ( best == null || pixels > bestPixels )
			{
				best = pix;
				bestPixels = pixels;
			}
		}

		return best;
	}

	public static AssetPreview CreateForAsset( Asset asset )
	{
		var type = EditorTypeLibrary.GetTypesWithAttribute<AssetPreviewAttribute>().Where( x => x.Attribute.Extension == asset.AssetType.FileExtension ).FirstOrDefault();
		if ( type.Type is null ) return null;

		return type.Type.Create<AssetPreview>( new[] { asset } );
	}
}
