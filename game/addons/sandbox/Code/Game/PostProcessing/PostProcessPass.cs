using Sandbox.Rendering;
using System.Runtime.CompilerServices;

/// <summary>
/// Fullscreen passes and volume blending for scripted Sandbox effects.
/// </summary>
public abstract class PostProcessPass : BasePostProcess<PostProcessPass>
{
	readonly ConditionalWeakTable<CameraComponent, RenderAttributes> _views = new();

	/// <summary>
	/// Shader settings for the current camera. Each camera can see a different mix of volumes.
	/// </summary>
	protected RenderAttributes ViewAttributes => _views.GetOrCreateValue( Camera );

	/// <summary>
	/// Blends a numeric setting across active volumes. Use zero as the starting value to fade an effect in.
	/// </summary>
	protected float Weighted( Func<PostProcessPass, float> setting, bool onlyLerpBetweenVolumes = false )
	{
		return GetWeighted( setting, 0f, onlyLerpBetweenVolumes );
	}

	/// <summary>
	/// Draws the material over the camera image using this view's settings.
	/// </summary>
	protected void DrawPass( Material material, Stage stage, int order, string name )
	{
		var blit = BlitMode.WithBackbuffer( material, stage, order );
		blit.Attributes = ViewAttributes;
		Blit( blit, name );
	}

	/// <summary>
	/// Creates a reusable pass that samples the scene as ColorBuffer using the supplied live shader attributes.
	/// </summary>
	public static CommandList FromShader( string shaderPath, RenderAttributes attributes )
	{
		var commands = new CommandList( shaderPath );
		commands.Attributes.GrabFrameTexture( "ColorBuffer" );
		commands.Blit( Material.FromShader( shaderPath ), attributes );
		return commands;
	}
}
