using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// The engine's tools view pixel stage (<c>shaders/tools_view.slang</c>, a standalone module), which colour draws in a tools
/// pass draw with (<see cref="ToolsPass"/>, <see cref="ToolsViews"/>): each keeps its own vertex stage, whatever made it.
/// </summary>
internal static class ToolsViewStage
{
	const string Path = "shaders/tools_view.shader";

	sealed class Loaded
	{
		public ShaderVariant Variant;
		public bool Missing;
	}

	static readonly ConditionalWeakTable<ShaderLibrary, Loaded> loaded = new();

	/// <summary>The stage's variant in <paramref name="shaders"/>; null while it compiles or if it's missing.</summary>
	public static ShaderVariant Of( ShaderLibrary shaders )
	{
		var state = loaded.GetValue( shaders, static _ => new Loaded() );
		if ( Volatile.Read( ref state.Variant ) is { } known ) return known;
		if ( state.Missing ) return null;
		lock ( state )
		{
			if ( state.Variant is not null ) return state.Variant;
			if ( shaders.Load( Path ) is not { } shader )
			{
				state.Missing = true;
				return null;
			}

			var variant = shaders.Variant( shader, "Default", [], null, null, false, out var pending );
			if ( pending || variant?.Pixel?.Bytecode is null ) return null;
			Volatile.Write( ref state.Variant, variant );
			return variant;
		}
	}

	/// <summary>The pass to draw and cache: ordinary material draws until the tools stage is ready.</summary>
	public static ToolsPass ReadyPass( ShaderLibrary shaders, ToolsPass pass )
		=> pass != ToolsPass.None && Of( shaders ) is not null ? pass : ToolsPass.None;

	/// <summary>
	/// Make a colour pipeline draw in a tools pass: its own vertex (or mesh) stage, rasterizer and culling, with the tools
	/// view's pixel stage writing the first target (the others written nothing), and the pass's state over the pipeline's:
	/// lines for wireframe, additive with no depth for the overdraw counts. <paramref name="shaderColor"/> is the shader's
	/// colour for <see cref="ToolsPass.ShaderIds"/> (<see cref="ToolsViews.ShaderColor"/>). Nothing changes while the stage
	/// isn't ready or for a pipeline with no colour: the draw is the material's own until it is.
	/// </summary>
	public static void Apply( ShaderLibrary shaders, ref GraphicsPipelineInfo info, ToolsPass pass, uint shaderColor )
	{
		if ( pass == ToolsPass.None || info.ColorFormats is not { Length: > 0 } || Of( shaders ) is not { Pixel: { } pixel } ) return;

		info.FragmentShader = pixel.Module;
		info.FragmentEntry = pixel.Bytecode.EntryPoint( ShaderProgramType.Pixel );
		info.FragmentConstants = [new SpecializationConstant( 0, (uint)pass ), new SpecializationConstant( 1, pass == ToolsPass.ShaderIds ? shaderColor : ToolsViews.WireframeColor )];
		info.AlphaToCoverage = false;

		var counts = pass is ToolsPass.Overdraw or ToolsPass.QuadOverdraw;
		var blend = new BlendState[info.ColorFormats.Length];
		blend[0] = counts ? BlendState.Additive : BlendState.Opaque;
		for ( int i = 1; i < blend.Length; i++ ) blend[i] = BlendState.NoWrites;
		info.Blend = blend;

		if ( pass == ToolsPass.Wireframe ) info.Wireframe = true;
		if ( counts )
		{
			// Every fragment counts: nothing tested or written but colour
			info.DepthTest = false;
			info.DepthWrite = false;
			info.StencilTest = false;
		}
	}
}
