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

		Native.ClearSceneWorlds();
		Native.SetRenderAttributes( Attributes.Get() );

		Native.ClearRenderTags();
		Native.ClearExcludeTags();

		foreach ( var tag in camera.RenderTags.TryGetAll() )
		{
			Native.AddRenderTag( StringToken.FindOrCreate( tag ) );
		}

		foreach ( var tag in camera.ExcludeTags.TryGetAll() )
		{
			Native.AddExcludeTag( StringToken.FindOrCreate( tag ) );
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
		Native.EnableEngineOverlays = camera.EnableEngineOverlays;
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
