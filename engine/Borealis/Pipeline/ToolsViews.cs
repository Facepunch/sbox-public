namespace Borealis;

/// <summary>
/// The tools pass a colour draw takes (<see cref="GpuContext.ToolsPass"/>): the draw keeps its own vertex stage and draws
/// with the engine's <c>tools_view</c> pixel stage, whatever its shader is.
/// </summary>
internal enum ToolsPass : byte
{
	/// <summary>The material's own pixel stage.</summary>
	None,

	/// <summary>Lines in the wireframe colour, depth tested.</summary>
	Wireframe,

	/// <summary>A constant added per fragment, with no depth test: how many surfaces cover each pixel.</summary>
	Overdraw,

	/// <summary>Added per fragment, redder where the triangle covers fewer pixels of its 2x2 quad: the pixel stage's wasted lanes.</summary>
	QuadOverdraw,

	/// <summary>A flat colour per shader.</summary>
	ShaderIds,
}

/// <summary>
/// The editor's view modes (<c>SceneCameraDebugMode</c> in <see cref="RenderView.ToolsVisMode"/>, and
/// <see cref="RenderView.Wireframe"/>) as the frame draws them. One mechanism for every shader kind:
/// <list type="bullet">
/// <item>Modes of the material's outputs and its lighting (full bright, albedo, roughness, normals, AO, UVs, diffuse,
/// reflect, transmission, clustered light culling) are the <c>ToolsVisMode</c> specialization constant, which the engine's
/// shading (<c>ToolsVis.hlsl</c> through the shading model, and every Slang surface's pass through <c>SurfaceToolsView</c>)
/// turns into the channel asked for.</item>
/// <item>Geometric modes (wireframe, overdraw, quad overdraw, shader IDs) are a <see cref="ToolsPass"/>: the colour layers
/// draw every draw with its own vertex stage and the engine's <c>tools_view</c> pixel stage, for .shader materials,
/// surfaces, pass modules and GPU-driven buckets alike. Shaders draw lit meanwhile.</item>
/// </list>
/// Motion vectors and the reactive mask are the upscaler's, which Borealis has none of: they draw the lit frame.
/// </summary>
internal static class ToolsViews
{
	/// <summary><c>SceneCameraDebugMode.ShaderIDColor</c>.</summary>
	public const int ShaderIds = 16;

	/// <summary><c>SceneCameraDebugMode.QuadOverdraw</c>.</summary>
	public const int QuadOverdraw = 100;

	/// <summary><c>SceneCameraDebugMode.Overdraw</c>.</summary>
	public const int Overdraw = 101;

	/// <summary><c>SceneCameraDebugMode.MotionVectors</c>.</summary>
	public const int MotionVectors = 102;

	/// <summary><c>SceneCameraDebugMode.ReactiveMask</c>.</summary>
	public const int ReactiveMask = 103;

	/// <summary>The tools pass a view's colour layers draw with: wireframe over any mode, else its mode's.</summary>
	public static ToolsPass PassOf( RenderView view ) => view is null ? ToolsPass.None : view.Wireframe ? ToolsPass.Wireframe : view.ToolsVisMode switch
	{
		ShaderIds => ToolsPass.ShaderIds,
		QuadOverdraw => ToolsPass.QuadOverdraw,
		Overdraw => ToolsPass.Overdraw,
		_ => ToolsPass.None,
	};

	/// <summary>
	/// The <c>ToolsVisMode</c> shaders are given: the view's, unless the mode is the engine's tools pass or the upscaler's,
	/// which shaders draw lit under.
	/// </summary>
	public static int ShaderMode( RenderView view ) => view.ToolsVisMode is ShaderIds or QuadOverdraw or Overdraw or MotionVectors or ReactiveMask ? 0 : view.ToolsVisMode;

	/// <summary>Whether a view counts fragments: it starts black, draws no sky, and its draws take every fragment, unculled by depth.</summary>
	public static bool Counts( RenderView view ) => PassOf( view ) is ToolsPass.Overdraw or ToolsPass.QuadOverdraw;

	/// <summary>
	/// The colour a shader's draws take in <see cref="ToolsPass.ShaderIds"/>: bright and saturated, from a hash of its name, as
	/// 0xRRGGBB.
	/// </summary>
	public static uint ShaderColor( string name )
	{
		var h = 2166136261u;
		foreach ( var c in name ?? "" ) h = (h ^ char.ToLowerInvariant( c )) * 16777619u;
		h ^= h >> 15;
		h *= 0x2c1b3c6du;
		h ^= h >> 12;
		var hue = (h & 0xffff) / 65536f;
		float Channel( float offset ) => Math.Clamp( MathF.Abs( (hue + offset) % 1f * 6f - 3f ) - 1f, 0f, 1f );
		var value = 0.65f + 0.35f * ((h >> 16) & 0xff) / 255f;
		uint Byte( float c ) => (uint)Math.Clamp( (0.2f + 0.8f * c) * value * 255f, 0f, 255f );
		return Byte( Channel( 0f ) ) << 16 | Byte( Channel( 2f / 3f ) ) << 8 | Byte( Channel( 1f / 3f ) );
	}

	/// <summary>The line colour of <see cref="ToolsPass.Wireframe"/>, as 0xRRGGBB.</summary>
	public const uint WireframeColor = 0xffffff;
}
