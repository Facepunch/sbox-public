using Sandbox.Rendering;

namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// Divisor of the refraction capture's dimensions relative to the viewing camera, set by the
	/// water quality level. 1 is full resolution.
	/// </summary>
	[ConVar( "r.water.refraction.downsample", Min = 1, Max = 8, Help = "Water refraction resolution divisor (1: full, 2: half, 4: quarter)." )]
	internal static int RefractionDownsampleFactor { get; set; } = 1;

	/// <summary>
	/// Whether water draws the scene under it into its surface. Off, a body in view no longer renders the scene a second time.
	/// </summary>
	[ConVar( "r.water.refraction", Help = "Draw the scene under water into its surface (0: off, saves a second render of the scene)." )]
	internal static bool Refraction { get; set; } = true;

	/// <summary>
	/// Immutable to the drawing thread: the scene's preparation updates this after joining the previous draw.
	/// The command list keeps this instance, not the live component or its camera.
	/// </summary>
	sealed class RefractionCapture
	{
		readonly TagSet exclusions;
		readonly int viewHash;
		readonly Action draw;
		readonly Vector4[] waves = new Vector4[WaveBands];
		Vector3 origin;
		Rotation rotation;
		float height, steepness, waveTime;
		bool aboveVisible, belowVisible;
		int downsample;
		FogSetup? aboveFog;

		public RefractionCapture( string surfaceTag )
		{
			exclusions = new TagSet( [surfaceTag] );
			viewHash = HashCode.Combine( surfaceTag, nameof( RefractionCapture ) );
			draw = DrawView;
		}

		public Action Draw => draw;

		public void Update( WaterBody water )
		{
			var material = water.ActiveMaterial;
			aboveVisible = Refraction && MaterialOpacity( material, false ) < 1;
			belowVisible = Refraction && MaterialOpacity( material, true ) < 1;
			if ( !aboveVisible && !belowVisible ) return;

			origin = water.SurfaceOrigin;
			rotation = water.WorldRotation;
			height = water.CaptureDisplacement;
			if ( height > 0 )
			{
				water.UpdateWaves();
				water._waves.AsSpan().CopyTo( waves );
				steepness = water.WaveSteepness;
				waveTime = water.WaveTime;
			}
			downsample = CaptureSizeFactor( RefractionDownsampleFactor );
			aboveFog = aboveVisible ? water.RefractionFog : null;
		}

		void DrawView()
		{
			var attributes = Graphics.Attributes;
			attributes.Set( "HasRefractionTexture", false );
			if ( (!aboveVisible && !belowVisible)
				|| !SceneRenderer.Current.TryGetCurrentView( out var view ) || view.Capture ) return;

			var eye = rotation.Inverse * (view.Transform.Position - origin);
			var surface = height > 0 ? WaterWaveMath.Sample( eye, waves, height, steepness, waveTime, out _ ) : 0;
			bool below = surface > eye.z;
			if ( below ? !belowVisible : !aboveVisible ) return;

			FogSetup? fog = below ? default( FogSetup ) : aboveFog;
			var setup = new ViewSetup
			{
				Transform = view.Transform,
				FieldOfView = view.FieldOfView,
				ZNear = view.ZNear,
				ZFar = view.ZFar,
				ProjectionMatrix = view.Projection,
				EnablePostprocessing = false,
				EnableUI = false,
				EnableDebugOverlays = false,
				ViewHash = viewHash,
				ClearColor = !below ? fog?.Color : null,
				Fog = fog,
				ExcludeTags = exclusions
			};

			// Use the view's projection as the base. Don't add oblique clipping to orthographic or near-surface views.
			if ( !view.Orthographic && MathF.Abs( eye.z ) > MathF.Max( 0.5f, height ) + view.ZNear * 2
				&& view.Size.x > 0 && view.Size.y > 0 )
			{
				var normal = below ? -rotation.Up : rotation.Up;
				var plane = new Plane( origin + normal * (height + 0.125f), -normal );
				var projection = view.Projection ?? Matrix.CreateProjection( view.ZNear, view.ZFar,
					view.FieldOfView, view.Size.x / view.Size.y, new Vector4( -1, -1, 1, 1 ) );
				var tx = view.Transform;
				setup.ProjectionMatrix = Matrix.CreateObliqueProjection( in tx, in plane, in projection );
				setup.ClipPlane = plane;
			}

			var (width, heightPixels) = RenderTarget.ScaleDownSize( view.Size, downsample );
			var target = RenderTarget.GetCaptureTemporary( width, heightPixels, ImageFormat.RGBA16161616F, ImageFormat.None, "Water refraction" );
			if ( !view.Camera.RenderToTexture( target.ColorTarget, null, setup, out var capture ) ) return;

			attributes.Set( "RefractionTexture", target.ColorTarget );
			attributes.Set( "RefractionViewport", capture.Viewport );
			attributes.Set( "RefractionHasSceneFog", fog is null );
			attributes.Set( "HasRefractionTexture", true );
		}
	}
}
