using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// The standard effects the uber post pass draws (<c>shaders/postprocess/pp_uber.slang</c>): its <c>UberEffects</c> bits.
/// </summary>
[Flags]
internal enum UberPostEffect
{
	None = 0,
	Sharpen = 1,
	FilmGrain = 2,
	ChromaticAberration = 4,
	ColorAdjustments = 8,
	ColorGrading = 16,
	Pixelate = 32,
	Bloom = 64,
	Vignette = 128,
	Tonemap = 256,

	/// <summary>
	/// Effects that read the frame as the layer before them left it: those reading pixels other than their own, and bloom's
	/// composite, whose own command list copies the frame first. Each starts a run.
	/// </summary>
	StartsRun = Sharpen | ChromaticAberration | Pixelate | Bloom,
}

internal partial class PostProcessLayers
{
	static Material _uberMaterial;
	static Material UberMaterial => _uberMaterial ??= Material.FromShader( "shaders/postprocess/pp_uber.shader" );

	static readonly StringToken UberEffectsName = new( "UberEffects" );

	// A run's command list name by its effects, made once for each set seen
	static readonly Dictionary<UberPostEffect, string> _uberNames = new();

	/// <summary>
	/// Fold each run of standard effects' layers next to each other in a stage's order into one pass: one copy of the frame
	/// and one draw of the uber post shader with every effect of the run on, in place of a copy and a draw each. A layer with
	/// a command list of its own between two of them (a game's own effect) ends the run, so nothing is reordered. An effect
	/// that reads its neighbours (sharpen, chromatic aberration, pixelate) reads the frame as the layer before left it, so it
	/// starts a run, as bloom does: its own commands (the copy and the downsample chain) come first, then the run's draw. The
	/// run's first layer draws it; the others are left without a command list. A run of one effect that has a pass of its
	/// own (the vignette, a blend over the frame with no copy) keeps it. On the main thread, after the camera's effects are
	/// built.
	/// </summary>
	public void FoldUberLayers()
	{
		foreach ( var list in Layers.Values )
		{
			if ( list.Count == 0 ) continue;
			list.Sort();

			for ( int i = 0; i < list.Count; )
			{
				if ( list[i].Uber == UberPostEffect.None )
				{
					i++;
					continue;
				}

				var end = i + 1;
				while ( end < list.Count && list[end].Uber != UberPostEffect.None && (list[end].Uber & UberPostEffect.StartsRun) == 0 ) end++;

				// Alone, an effect with a pass of its own keeps it
				if ( end - i > 1 || list[i].CommandList is null )
					FoldRun( list, i, end );
				i = end;
			}
		}
	}

	static void FoldRun( List<PostProcessLayer> list, int start, int end )
	{
		var head = list[start];
		var attributes = head.UberAttributes ??= RenderAttributes.ForRecording();
		attributes.Clear();

		var effects = UberPostEffect.None;
		for ( int i = start; i < end; i++ )
		{
			var layer = list[i];
			effects |= layer.Uber;
			layer.UberSource?.MergeTo( attributes );
			layer.CommandList = null;
		}

		attributes.Set( UberEffectsName, (int)effects );

		// After the first effect's own commands (bloom's copy and chain), or a copy of the frame
		var cl = head.UberPrefix;
		if ( cl is null )
		{
			cl = head.BlitCommandList ??= new CommandList();
			cl.Reset();

			// Pixelate reads the block's mips
			cl.Attributes.GrabFrameTexture( "ColorBuffer", (effects & UberPostEffect.Pixelate) != 0 );
		}

		cl.DebugName = end - start == 1 ? head.Name : RunName( effects );
		cl.Blit( UberMaterial, attributes );
		head.CommandList = cl;
	}

	static string RunName( UberPostEffect effects )
	{
		if ( !_uberNames.TryGetValue( effects, out var name ) )
		{
			name = $"UberPost ({effects})";
			_uberNames[effects] = name;
		}

		return name;
	}
}
