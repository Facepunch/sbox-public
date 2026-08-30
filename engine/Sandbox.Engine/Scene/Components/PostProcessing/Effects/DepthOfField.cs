using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies a depth of field effect to the camera
/// </summary>
[Title( "Depth Of Field" )]
[Category( "Post Processing" )]
[Icon( "center_focus_strong" )]
public sealed class DepthOfField : BasePostProcess<DepthOfField>
{
	/// <summary>
	/// Quality scale factors: [Off, Low, Medium, High]
	/// </summary>
	private static readonly float[] StepScales = { 0f, 3f, 2f, 1f };

	[ConVar( "r_dof_quality", ConVarFlags.Saved, Min = 0, Max = 3, Help = "Depth of field quality (0: off, 1: low, 2: med, 3: high)" )]
	internal static int Quality { get; set; } = 3;

	/// <summary>
	/// How blurry to make stuff that isn't in focus, the maximum blur radius in pixels.
	/// </summary>
	[Range( 0, 100 )]
	[Property, Group( "Focus" ), Icon( "blur_circular" )]
	public float BlurSize { get; set; } = 30.0f;

	/// <summary>
	/// How far away from the camera to focus in world units.
	/// </summary>
	[Range( 1.0f, 1000 )]
	[Property, Group( "Focus" ), Icon( "horizontal_distribute" )]
	public float FocalDistance { get; set; } = 200.0f;

	/// <summary>
	/// How far from the <see cref="FocalDistance"/> something has to be, in world units, before it's fully out of focus.
	/// Larger values give a softer, more gradual falloff. Defaults to the camera far plane.
	/// </summary>
	[Range( 1.0f, 15000.0f )]
	[Property, Group( "Focus" ), Icon( "blur_linear" )]
	public float FocusRange { get; set; } = 15000f;

	/// <summary>
	/// Should we blur what's ahead of the focal point towards us?
	/// </summary>
	[Property, Group( "Properties" ), Icon( "flip_to_back" )]
	public bool FrontBlur { get; set; } = false;

	/// <summary>
	/// Should we blur what's behind the focal point?
	/// </summary>
	[Property, Group( "Properties" ), Icon( "flip_to_front" )]
	public bool BackBlur { get; set; } = true;

	/// <summary>
	/// Visualize the circle of confusion in black and white instead of the composite.
	/// </summary>
	[Property, Group( "Properties" ), Icon( "bug_report" )]
	public bool DebugView { get; set; } = false;

	CommandList command = new CommandList( "Depth Of Field" );

	private static ComputeShader ShaderCs = new ComputeShader( "postprocess/postprocess_standard_dof_cs" );

	private static Material Shader = Material.FromShader( "shaders/postprocess/postprocess_standard_dof.shader" );

	/// <summary>
	/// Max classified tiles per layer. Each tile covers 32x32 full-res pixels, this covers ~8K screens.
	/// </summary>
	private const int TileCapacity = 32 * 1024;

	private GpuBuffer<uint> TileListBuffer;
	private GpuBuffer<GpuBuffer.IndirectDispatchArguments> DispatchArgsBuffer;

	private void EnsureTileBuffers()
	{
		// Back layer tiles at [0, TileCapacity), front layer tiles at [TileCapacity, 2 * TileCapacity)
		TileListBuffer ??= new GpuBuffer<uint>( TileCapacity * 2, GpuBuffer.UsageFlags.Structured, "DoF_Tiles" );
		DispatchArgsBuffer ??= new GpuBuffer<GpuBuffer.IndirectDispatchArguments>( 2, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "DoF_DispatchArgs" );
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		TileListBuffer?.Dispose();
		TileListBuffer = null;

		DispatchArgsBuffer?.Dispose();
		DispatchArgsBuffer = null;
	}

	public override void Render()
	{
		if ( Quality == 0 || (!BackBlur && !FrontBlur) )
			return;

		float blurSize = GetWeighted( x => x.BlurSize, 0.0f ).Clamp( 0.0f, 100.0f );
		if ( blurSize < 0.5f ) return;

		float focalDistance = GetWeighted( x => x.FocalDistance, 200.0f );
		float focusRange = GetWeighted( x => x.FocusRange, 10000.0f );
		float stepScale = StepScales[Quality.Clamp( 0, 3 )];
		int radius = Math.Max( 1, (int)(blurSize / stepScale) );

		EnsureTileBuffers();

		command.Reset();

		// Reset tile counts, classify fills these in on the GPU
		command.SetBufferData( DispatchArgsBuffer, new GpuBuffer.IndirectDispatchArguments[]
		{
			new() { ThreadGroupCountX = 0, ThreadGroupCountY = 1, ThreadGroupCountZ = 1 }, // Back
			new() { ThreadGroupCountX = 0, ThreadGroupCountY = 1, ThreadGroupCountZ = 1 }, // Front
		} );

		const int downsample = 2;
		const int tileSize = 16;

		command.Attributes.SetValue( "Color", RenderValue.ColorTarget );
		command.Attributes.SetValue( "Depth", RenderValue.DepthTarget );
		command.Attributes.SetValue( "D_MSAA", RenderValue.MsaaCombo );
		var FinalBack = BackBlur ? command.GetRenderTarget( "DofBack", downsample, ImageFormat.RGBA16161616F, ImageFormat.None ) : default;
		var FinalFront = FrontBlur ? command.GetRenderTarget( "DofFront", downsample, ImageFormat.RGBA16161616F, ImageFormat.None ) : default;
		var Vertical = command.GetRenderTarget( "DofVertical", downsample, ImageFormat.RGBA16161616F, ImageFormat.None );
		var Diagonal = command.GetRenderTarget( "DofDiagonal", downsample, ImageFormat.RGBA16161616F, ImageFormat.None );

		if ( BackBlur ) command.Clear( FinalBack, Color.Transparent );
		if ( FrontBlur ) command.Clear( FinalFront, Color.Transparent );


		// One texel per 16x16 half-res tile, x = back CoC, y = front CoC.
		// Allocated a bit bigger so partial edge tiles still get a texel.
		var TileMax = command.GetRenderTarget( "DofTileMax", (downsample * tileSize) - 1, ImageFormat.RG1616F, ImageFormat.None );
		var TileDilated = command.GetRenderTarget( "DofTileDilated", (downsample * tileSize) - 1, ImageFormat.RG1616F, ImageFormat.None );

		command.Attributes.Set( "InvDimensions", Vertical.Size, true );
		command.Attributes.Set( "Dimensions", Vertical.Size );
		command.Attributes.Set( "Radius", radius );
		command.Attributes.Set( "StepScale", stepScale );
		command.Attributes.Set( "FocusPlane", focalDistance.Clamp( 0, 5000 ) );
		command.Attributes.Set( "FocusRange", focusRange );
		command.Attributes.Set( "EnableBack", BackBlur );
		command.Attributes.Set( "EnableFront", FrontBlur );
		command.Attributes.Set( "TileCapacity", TileCapacity );

		//
		// Downsample + circle of confusion + per-tile max CoC
		//
		// Pooled targets can be left in a read state from their previous use, writing to them
		// as UAVs without transitioning back is undefined and corrupts on AMD
		if ( BackBlur ) command.ResourceBarrierTransition( FinalBack, ResourceState.UnorderedAccess );
		if ( FrontBlur ) command.ResourceBarrierTransition( FinalFront, ResourceState.UnorderedAccess );
		command.ResourceBarrierTransition( TileMax, ResourceState.UnorderedAccess );

		command.Attributes.Set( "OutColor0", (BackBlur ? FinalBack : FinalFront).ColorTexture );
		command.Attributes.Set( "OutColor1", (FrontBlur ? FinalFront : FinalBack).ColorTexture );
		command.Attributes.Set( "OutTile", TileMax.ColorTexture );
		command.Attributes.SetCombo( "D_PASS", BlurPasses.CircleOfConfusion );
		command.DispatchCompute( ShaderCs, Vertical.Size );

		command.ResourceBarrierTransition( TileMax, ResourceState.NonPixelShaderResource );
		if ( BackBlur ) command.ResourceBarrierTransition( FinalBack, ResourceState.NonPixelShaderResource );
		if ( FrontBlur ) command.ResourceBarrierTransition( FinalFront, ResourceState.NonPixelShaderResource );

		//
		// Classify tiles - build the tile lists and indirect dispatch arguments so
		// the blur only runs on tiles it can actually reach, with a correct budget
		//
		command.ResourceBarrierTransition( TileListBuffer, ResourceState.UnorderedAccess );

		// From CopyDestination so the SetBufferData reset above is flushed before the classify pass atomics
		command.ResourceBarrierTransition( DispatchArgsBuffer, ResourceState.CopyDestination, ResourceState.UnorderedAccess );
		command.ResourceBarrierTransition( TileDilated, ResourceState.UnorderedAccess );
		command.Attributes.Set( "TileMaxSRV", TileMax.ColorTexture );
		command.Attributes.Set( "OutTile", TileDilated.ColorTexture );
		command.Attributes.Set( "TileListRW", TileListBuffer );
		command.Attributes.Set( "DispatchArgsRW", DispatchArgsBuffer );
		command.Attributes.SetCombo( "D_PASS", BlurPasses.Classify );
		command.DispatchCompute( ShaderCs, TileMax.Size );

		command.ResourceBarrierTransition( TileListBuffer, ResourceState.UnorderedAccess, ResourceState.NonPixelShaderResource );
		command.ResourceBarrierTransition( DispatchArgsBuffer, ResourceState.UnorderedAccess, ResourceState.IndirectArgument );
		command.ResourceBarrierTransition( TileDilated, ResourceState.NonPixelShaderResource );

		command.Attributes.SetCombo( "D_DEBUG_COC", DebugView );
		command.Attributes.Set( "TileList", TileListBuffer );
		command.Attributes.Set( "TileDilatedSRV", TileDilated.ColorTexture );

		//
		// Hexagonal blur + composite, back layer first so the front layer blends over it
		//
		foreach ( DoFTypes type in Enum.GetValues( typeof( DoFTypes ) ) )
		{
			if ( !BackBlur && type == DoFTypes.BackBlur )
				continue;

			if ( !FrontBlur && type == DoFTypes.FrontBlur )
				continue;

			var target = type == DoFTypes.BackBlur ? FinalBack : FinalFront;

			command.Clear( Vertical, Color.Transparent );
			command.Clear( Diagonal, Color.Transparent );

			command.ResourceBarrierTransition( Vertical, ResourceState.UnorderedAccess );
			command.ResourceBarrierTransition( Diagonal, ResourceState.UnorderedAccess );

			command.Attributes.Set( "IsFront", type == DoFTypes.FrontBlur );
			command.Attributes.Set( "ListOffset", type == DoFTypes.FrontBlur ? TileCapacity : 0 );
			command.Attributes.Set( "FinalSRV", target.ColorTexture );
			command.Attributes.Set( "OutColor0", Vertical.ColorTexture );
			command.Attributes.Set( "OutColor1", Diagonal.ColorTexture );

			command.Attributes.SetCombo( "D_PASS", BlurPasses.DiagonalBlur );
			command.DispatchComputeIndirect( ShaderCs, DispatchArgsBuffer, (uint)type );

			command.ResourceBarrierTransition( Vertical, ResourceState.NonPixelShaderResource );
			command.ResourceBarrierTransition( Diagonal, ResourceState.NonPixelShaderResource );

			// The layer target was left in a read state for the diagonal pass, transition it
			// back to writable before the hexagonal pass writes the final blur into it
			command.ResourceBarrierTransition( target, ResourceState.UnorderedAccess );

			command.Attributes.Set( "VerticalSRV", Vertical.ColorTexture );
			command.Attributes.Set( "DiagonalSRV", Diagonal.ColorTexture );
			command.Attributes.Set( "OutColor0", target.ColorTexture );

			command.Attributes.SetCombo( "D_PASS", BlurPasses.HexagonalBlur );
			command.DispatchComputeIndirect( ShaderCs, DispatchArgsBuffer, (uint)type );

			command.ResourceBarrierTransition( target, ResourceState.PixelShaderResource );

			command.Attributes.Set( "Final", target.ColorTexture );
			command.Attributes.SetCombo( "D_DOF_TYPE", type );
			command.Blit( Shader );
		}

		InsertCommandList( command, Stage.AfterTransparent, 100, "Dof" );
	}

	private enum BlurPasses
	{
		CircleOfConfusion,
		Classify,
		DiagonalBlur,
		HexagonalBlur,
	};

	private enum DoFTypes
	{
		BackBlur,
		FrontBlur,
	};
}
