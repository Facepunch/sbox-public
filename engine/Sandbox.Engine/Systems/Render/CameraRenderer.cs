using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Camera setup for rendering a View.
/// </summary>
internal ref struct CameraRenderer
{
	public CCameraRenderer Native;
	int cameraId;

	public CameraRenderer( string name, int cameraId )
	{
		this.cameraId = cameraId;
		Native = CCameraRenderer.Create( name, cameraId );
	}

	public void Dispose()
	{
		Native.DeleteThis();
	}

	void AddTags( ITagSet tags, bool exclude )
	{
		// A scene camera's tags are already tokens. Those TryGetAll would give back (the ones with
		// a known string) are the same tokens FindOrCreate makes, without the iterator per frame.
		if ( tags is TokenBasedTagSet && tags.GetTokens() is HashSet<uint> tokens )
		{
			foreach ( var token in tokens )
			{
				if ( !StringToken.TryLookup( token, out _ ) ) continue;

				if ( exclude ) Native.AddExcludeTag( token );
				else Native.AddRenderTag( token );
			}

			return;
		}

		foreach ( var tag in tags.TryGetAll() )
		{
			var token = StringToken.FindOrCreate( tag );

			if ( exclude ) Native.AddExcludeTag( token );
			else Native.AddRenderTag( token );
		}
	}

	internal void Configure( SceneCamera camera, ViewSetup config )
	{
		var _world = camera.World;
		var Attributes = new RenderAttributes();

		camera.Attributes.MergeTo( Attributes );

		if ( _world.IsValid() && !Graphics.IsActive )
		{
			_world.UpdateObjectsForRendering( camera.Position, camera.ZFar );
		}

		Color ambientLight = camera.AmbientLightColor;

		// update attributes from primary world
		if ( camera.World is not null )
		{
			camera.World.GradientFog.Apply( Attributes );
			ambientLight += camera.World.AmbientLightColor;
		}

		if ( config.AmbientLightTint is Color ambientTint )
		{
			ambientLight *= ambientTint;
		}

		if ( config.AmbientLightAdd is Color ambientAdd )
		{
			ambientLight += ambientAdd;
		}

		if ( config.GradientFog is GradientFogSetup gradientFog )
		{
			gradientFog.Apply( Attributes );
		}

		// The values added to this make IBL lerp value go beyond 0-1, clamp it
		ambientLight.a = ambientLight.a.Clamp( 0.0f, 1.0f );

		Attributes.Set( "ambientColor", ambientLight );
		Attributes.Set( "clearColor", config.ClearColor ?? camera.BackgroundColor );

		if ( DebugOverlay.ToolsVisualization.mat_toolsvis != SceneCameraDebugMode.Normal )
		{
			Attributes.Set( "ToolsVisMode", (int)DebugOverlay.ToolsVisualization.mat_toolsvis );
		}

		camera.GatherVolumetricFog( Attributes );
		camera.GatherTonemapper( Attributes );
		camera.CubemapFog?.Write( Attributes );

		// Set clear flags
		Attributes.Set( "clearFlags", (int)camera.ClearFlags );

		if ( config.Time is float renderTime )
		{
			Attributes.Set( "RenderTime", renderTime );
		}

		Native.ClearSceneWorlds();
		Native.SetRenderAttributes( Attributes.Get() );

		Native.ClearRenderTags();
		Native.ClearExcludeTags();

		AddTags( config.RenderTags ?? camera.RenderTags, exclude: false );
		AddTags( camera.ExcludeTags, exclude: true );

		if ( config.ExcludeTags is not null )
		{
			AddTags( config.ExcludeTags, exclude: true );
		}

		Native.ViewUniqueId = HashCode.Combine( cameraId, config.ViewHash );
		Native.CameraPosition = config.Transform?.Position ?? camera.Position;
		Native.CameraRotation = config.Transform?.Rotation.Angles() ?? camera.Rotation.Angles();
		Native.ZNear = config.ZNear ?? camera.ZNear;
		Native.ZFar = config.ZFar ?? camera.ZFar;
		Native.FieldOfView = config.FieldOfView ?? camera.FieldOfView;
		Native.Rect = new Rect( 0, 0, camera.Size.x, camera.Size.y );
		Native.Viewport = new Vector4( camera.Rect.Left, camera.Rect.Top, Math.Min( camera.Rect.Width, 1 - camera.Rect.Left ), Math.Min( camera.Rect.Height, 1 - camera.Rect.Top ) );
		Native.Ortho = camera.Ortho;
		Native.ClipSpaceBounds = config.ClipSpaceBounds ?? new Vector4( -1, -1, 1, 1 );
		Native.EnablePostprocessing = config.EnablePostprocessing ?? camera.EnablePostProcessing;
		Native.EnableEngineOverlays = config.EnableDebugOverlays ?? camera.EnableEngineOverlays;
		Native.EnableUI = config.EnableUI ?? camera.RenderUI;
		Native.UIOnly = camera.UIOnly;
		Native.FlipX = config.FlipX ?? false;
		Native.FlipY = config.FlipY ?? false;

		var customproj = config.ProjectionMatrix ?? camera.CustomProjectionMatrix;

		if ( customproj.HasValue )
		{
			Native.HasOverrideProjection = true;
			Native.OverrideProjection = customproj.Value;
		}
		else
		{
			Native.HasOverrideProjection = false;
		}

		if ( Native.Ortho )
		{
			Native.OrthoSize = camera.OrthoHeight / camera.Size.y;
		}

		if ( camera.ExcludeFromTextureStreaming )
			Native.SceneViewFlags |= NativeEngine.SceneViewFlags.SVF_NO_TEXTURE_STREAMING;

		// Native's bit (1 << 1): the managed enum's members aren't bit values, so SVF_TOOL_VIEW there is native's SVF_DEBUG_LAYERS
		if ( camera.ToolsView )
			Native.SceneViewFlags |= (NativeEngine.SceneViewFlags)(1 << 1);

		//
		// add worlds
		//

		if ( _world is not null )
		{
			Native.AddSceneWorld( _world );
		}

		foreach ( var world in camera.Worlds )
		{
			if ( world is null ) continue;
			if ( world == _world ) continue;

			Native.AddSceneWorld( world );
		}
	}
}
