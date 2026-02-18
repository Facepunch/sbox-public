using Sandbox.MovieMaker;
using Sandbox.Rendering;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// Controls how many sub-frames are rendered for every final exported frame. Higher will make motion
/// smoother, but at the expense of taking longer to render. Values are in Hz.
/// </summary>
public enum MotionQuality
{
	Low = 4,
	Medium = 16,
	High = 64
}

/// <summary>
/// Describes how long the sensor is exposed for each output frame. Between 0 and 360, with 360
/// being exposed for the entire frame.
/// </summary>
public enum Exposure
{
	Instant = 0,
	VeryFast = 30,
	Fast = 90,
	Medium = 180,
	Slow = 270,
	VerySlow = 360
}

public enum ExportMode
{
	/// <summary>
	/// Write a single .mp4 or .webm video file.
	/// </summary>
	[Icon( "video_file" )]
	VideoFile,

	/// <summary>
	/// Write separate .png or .jpg image files for each frame.
	/// </summary>
	[Icon( "auto_awesome_motion" )]
	ImageSequence,

	/// <summary>
	/// Write a single .png or .jpg image file containing a grid of frames.
	/// </summary>
	[Icon( "grid_view" )]
	ImageAtlas
}

public sealed class VideoExportConfig
{
	/// <summary>
	/// Output frame rate of the final video.
	/// </summary>
	[Feature( "Dimensions", Icon = "aspect_ratio" )]
	public int FrameRate { get; set; } = 60;

	/// <summary>
	/// Output resolution of the final video.
	/// </summary>
	[Feature( "Dimensions", Icon = "aspect_ratio" )]
	public Vector2Int Resolution { get; set; } = new( 1920, 1080 );

	/// <summary>
	/// How many frames to render and discard before exporting, to warm up any temporal ray traced elements.
	/// </summary>
	[JsonIgnore, Hide]
	internal int WarmupFrameCount { get; set; } = 256;

	[Feature( "Encoding", Icon = "terminal" )]
	public ExportMode Mode { get; set; }

	[Feature( "Encoding", Icon = "terminal" ), JsonIgnore, ShowIf( nameof( Mode ), ExportMode.VideoFile )]
	public bool UseRecommendedBitrate
	{
		get => CustomBitrate <= 0;
		set => CustomBitrate = value ? 0 : RecommendedBitrate;
	}

	private bool ShowCustomBitrate => Mode == ExportMode.VideoFile && UseRecommendedBitrate;
	private bool ShowRecommendedBitrate => Mode == ExportMode.VideoFile && !UseRecommendedBitrate;

	/// <summary>
	/// How many Mbit/s to attempt to export at. If this value is too low, some frames may get skipped for some reason.
	/// </summary>
	[Feature( "Encoding", Icon = "terminal" ), Title( "Bitrate (Mbit/s)" )]
	[ShowIf( nameof( ShowCustomBitrate ), true )]
	public int CustomBitrate { get; set; }

	[JsonIgnore]
	public int Bitrate => UseRecommendedBitrate ? RecommendedBitrate : CustomBitrate;

	private const float RecommendedBitsPerPixel = 0.2f;

	/// <inheritdoc cref="CustomBitrate"/>
	[Feature( "Encoding", Icon = "terminal" ), Title( "Bitrate (Mbit/s)" ), JsonIgnore]
	[ShowIf( nameof( ShowRecommendedBitrate ), true )]
	public int RecommendedBitrate => (int)MathF.Ceiling( Resolution.x * Resolution.y * FrameRate * RecommendedBitsPerPixel / 1_000_000 );

	[Feature( "Encoding", Icon = "terminal" ), ShowIf( nameof( Mode ), ExportMode.VideoFile )]
	public VideoWriter.Codec Codec { get; set; } = VideoWriter.Codec.H264;

	/// <summary>
	/// Describes how long the sensor is exposed for each output frame.
	/// </summary>
	[Feature( "Motion", Icon = "animation" )]
	public Exposure Exposure { get; set; } = Exposure.Medium;

	/// <summary>
	/// Controls how many sub-frames are rendered for every final exported frame. Higher will make motion
	/// smoother, but at the expense of taking longer to render.
	/// </summary>
	[HideIf( nameof( Exposure ), Exposure.Instant )]
	[Feature( "Motion", Icon = "animation" )]
	public MotionQuality MotionQuality { get; set; } = MotionQuality.Medium;

	[JsonIgnore, Hide]
	public int SubFramesPerFrame => Exposure == Exposure.Instant ? 1 : Math.Max( 1, (int)MotionQuality );

	public VideoWriter.Config GetVideoWriterConfig( string filePath ) => new()
	{
		FrameRate = FrameRate,
		Width = Resolution.x,
		Height = Resolution.y,
		Bitrate = UseRecommendedBitrate ? RecommendedBitrate : CustomBitrate,
		Codec = Codec,
		Container = GetContainerForExtension( Path.GetExtension( filePath ) )
	};

	private static VideoWriter.Container GetContainerForExtension( string extension )
	{
		return extension.ToLowerInvariant() switch
		{
			".mp4" => VideoWriter.Container.MP4,
			".webm" => VideoWriter.Container.WebM,
			".webp" => VideoWriter.Container.WebP,
			_ => VideoWriter.Container.MP4
		};
	}
}

/// <summary>
/// Renders frames from a <see cref="Session"/> to a <see cref="Pixmap"/> / <see cref="Bitmap"/> / <see cref="Texture"/>.
/// </summary>
public sealed class SessionRenderer
{
	private readonly Session _session;
	private Task? _renderTask;

	public bool IsRendering => _renderTask is { IsCompleted: false };

	public SessionRenderer( Session session )
	{
		_session = session;
	}

	public delegate Task FrameCallback( MovieTime time, byte[] pixels, CancellationToken ct );

	public async Task RenderAsync( MovieTimeRange timeRange, VideoExportConfig config, FrameCallback onFrame,
		CancellationToken ct = default )
	{
		lock ( this )
		{
			_renderTask = RenderCoreAsync( timeRange, config, onFrame, ct );
		}

		try
		{
			await _renderTask;
		}
		finally
		{
			lock ( this )
			{
				_renderTask = null;
			}
		}
	}

	private async Task RenderCoreAsync( MovieTimeRange timeRange, VideoExportConfig config, FrameCallback onFrame, CancellationToken ct )
	{
		if ( _renderTask is { } prevTask )
		{
			try
			{
				await prevTask;
			}
			catch ( TaskCanceledException )
			{
				//
			}
		}

		var frameCount = timeRange.Duration.GetFrameCount( config.FrameRate );

		using var captureCamera = new SceneCamera( "Video Export Camera" );

		using var subFrameTex = Texture.CreateRenderTarget( "VideoExportSubFrame", ImageFormat.RGBA16161616, config.Resolution );
		using var accumulatedTex = Texture.Create( config.Resolution.x, config.Resolution.y, ImageFormat.RGBA32323232F )
			.WithName( "VideoExportAccumulated" )
			.WithUAVBinding()
			.WithGPUOnlyUsage()
			.Finish();

		var framePixels = new byte[config.Resolution.x * config.Resolution.y * 4];

		var subFrameCount = config.SubFramesPerFrame;
		var exposureFraction = (int)config.Exposure / 360f;
		var exposureStart = 0.5f - exposureFraction * 0.5f;
		var exposureEnd = 0.5f + exposureFraction * 0.5f;

		var accumulate = new ComputeShader( "moviemaker_accumulate_cs" );

		accumulate.Attributes.Set( "Subframe", subFrameTex );
		accumulate.Attributes.Set( "Accumulated", accumulatedTex );
		accumulate.Attributes.Set( "InvFrames", 1f / subFrameCount );

		var alphaDivide = new ComputeShader( "moviemaker_alphadivide_cs" );

		alphaDivide.Attributes.Set( "Accumulated", accumulatedTex );

		_session.PlayheadTime = timeRange.Start;

		var prevTime = timeRange.Start;

		Task? prevFrameTask = null;

		try
		{
			for ( var i = -1; i < frameCount; i++ )
			{
				if ( ct.IsCancellationRequested ) return;

				var isWarmup = i < 0;

				if ( !isWarmup && subFrameCount > 1 )
				{
					accumulatedTex.Clear( new Color( 0f, 0f, 0f, 0f ) );
				}

				var frameTime = timeRange.Start + (isWarmup ? 0 : MovieTime.FromFrames( i, config.FrameRate ));

				for ( var j = 0; j < (isWarmup ? config.WarmupFrameCount : subFrameCount); ++j )
				{
					var subFrameFraction = isWarmup ? 0f : (float)j / subFrameCount;
					var subFrameTime = MathX.Lerp( exposureStart, exposureEnd, subFrameFraction ) / config.FrameRate;
					var nextTime = frameTime + MovieTime.FromSeconds( subFrameTime );

					_session.PlayheadTime = nextTime;
					_session.Editor?.TimelinePanel?.Timeline.PanToPlayheadTime();

					BeforeRenderFrame( captureCamera, config, nextTime - prevTime );

					// Render a (sub)frame!

					RenderToTextureMethod.Invoke( captureCamera, [subFrameTex, (Vector2?)null, default( ViewSetup )] );

					if ( !isWarmup && subFrameCount > 1 )
					{
						accumulate.Dispatch( subFrameTex.Width, subFrameTex.Height, 1 );
					}

					// Yield to let the scene viewport render when it wants to so we get a preview,
					// and also so temporary resources get cleaned up periodically

					await Task.Yield();

					prevTime = nextTime;
				}

				if ( isWarmup ) continue;

				// Need to divide by alpha to convert from premultiplied

				if ( subFrameCount > 1 )
				{
					alphaDivide.Dispatch( subFrameTex.Width, subFrameTex.Height, 1 );
				}

				// Have to wait for prev frame to finish writing, since it'll be using framePixels

				if ( prevFrameTask is not null )
				{
					await prevFrameTask;
				}

				// Grab the frame from the GPU and add it to the video writer

				var frameSourceTex = subFrameCount > 1 ? accumulatedTex : subFrameTex;

				frameSourceTex.GetPixels( (0, 0, frameSourceTex.Width, frameSourceTex.Height), 0, 0,
					MemoryMarshal.Cast<byte, Color32>( framePixels.AsSpan() ),
					ImageFormat.RGBA8888, (frameSourceTex.Width, frameSourceTex.Height) );

				prevFrameTask = onFrame.Invoke( frameTime, framePixels, ct );
			}

			if ( prevFrameTask is not null )
			{
				await prevFrameTask;
			}
		}
		finally
		{
			foreach ( var particleEffect in _session.Player.Scene.GetAll<ParticleEffect>() )
			{
				particleEffect.Paused = false;
			}
		}
	}

	private void BeforeRenderFrame( SceneCamera captureCamera, VideoExportConfig config, MovieTime deltaTime )
	{
		var camera = _session.Player.Scene.Camera;

		// Update camera

		var aspect = (float)config.Resolution.x / config.Resolution.y;

		camera.UpdateSceneCamera( captureCamera );

		captureCamera.Size = config.Resolution.x;
		captureCamera.FieldOfView = camera.FovAxis == CameraComponent.Axis.Vertical
			? Screen.CreateVerticalFieldOfView( camera.FieldOfView, aspect )
			: camera.FieldOfView;

		// Make sure SkinnedModelRenderers have updated

		SignalMethod.Invoke( _session.Player.Scene, [GameObjectSystem.Stage.UpdateBones] );

		// Step particle effects

		foreach ( var particleEffect in _session.Player.Scene.GetAll<ParticleEffect>() )
		{
			try
			{
				particleEffect.Paused = false;
				particleEffect.Step( (float)deltaTime.TotalSeconds );
				particleEffect.Paused = true;
			}
			catch ( Exception ex )
			{
				Log.Warning( ex );
			}
		}

		// Make sure ParticleGameSystem moves particle sprites to their new positions

		SignalMethod.Invoke( _session.Player.Scene, [GameObjectSystem.Stage.FinishUpdate] );
	}

	private static MethodInfo RenderToTextureMethod { get; } = typeof( SceneCamera ).GetMethod( "RenderToTexture",
		BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )!;

	private static MethodInfo SignalMethod { get; } = typeof( Scene ).GetMethod( "Signal",
		BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )!;
}

partial class Session
{
	public SessionRenderer Renderer { get; }
}
