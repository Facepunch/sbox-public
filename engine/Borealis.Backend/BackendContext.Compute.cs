using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Runtime.CompilerServices;

namespace Borealis.Backend;

/// <summary>A texture's single mip, as <see cref="ShaderParameters.Set(StringToken, GpuTexture, int)"/> names one.</summary>
internal sealed record TextureMip( GpuTexture Texture, int Mip );

/// <summary>
/// Compute dispatches with parameters of their own (<see cref="GpuContext.Dispatch(GpuMaterial, ShaderParameters, int, int, int)"/>):
/// what Borealis's shared passes run, the depth chain among them. Each binds by reflection through a <see cref="ShaderDrawer"/>
/// of the device's: the parameters over this context's attributes, single mips as views of one mip, and the textures it
/// writes moved to <see cref="ImageLayout.General"/> (a mip it reads of a texture it writes moved to
/// <see cref="ImageLayout.ShaderRead"/> for the dispatch alone), the textures it only reads to
/// <see cref="ImageLayout.ShaderRead"/>.
/// </summary>
internal sealed partial class BackendContext
{
	/// <summary>The drawer parameterised dispatches bind with, one a device, binding by reflection; locked while used.</summary>
	static readonly ConditionalWeakTable<BackendDevice, ShaderDrawer> drawers = new();

	/// <summary>Single-mip views made for parameterised dispatches, one a texture and mip.</summary>
	static readonly ConditionalWeakTable<BackendTexture, Dictionary<int, TextureViewHandle>> mipViews = new();

	// One dispatch's attributes and the textures it writes and reads, reused from dispatch to dispatch
	ShaderAttributes dispatchAttributes;
	readonly HashSet<BackendTexture> dispatchWritten = new(), dispatchSampled = new();
	readonly List<(BackendTexture Texture, int Mip)> dispatchSampledMips = new();

	public override void Dispatch( GpuMaterial computeShader, ShaderParameters parameters, int x, int y, int z )
	{
		if ( recording is null || computeShader is not BackendMaterial { Shader: { } shader } ) return;
		if ( shader[ShaderProgramType.Compute] is not { } program || shader.Modes.Count == 0 ) return;
		var given = parameters as BackendParameters;

		object Value( string name )
		{
			var token = Token( name );
			return given?.Get( token ) ?? Lookup( token );
		}

		var drawer = drawers.GetValue( device, static d => new ShaderDrawer( d ) { MaterialObjects = false } );
		lock ( drawer )
		{
			// Kept on the context and emptied for each dispatch: dispatches allocate nothing in steady state
			var attributes = dispatchAttributes ??= new ShaderAttributes();
			attributes.Clear();
			foreach ( var combo in program.DynamicCombos )
			{
				var token = Token( combo.Name );
				if ( (given?.GetCombo( token ) ?? LookupCombo( token )) is int value ) attributes.SetCombo( combo.Name, value );
			}

			var variant = drawer.Variant( shader, attributes );
			if ( variant?.Compute?.Bytecode is null ) return;

			foreach ( var (variable, _) in variant.Compute.MaterialConstants )
			{
				if ( variable.Source != VariableSource.Attribute || string.IsNullOrEmpty( variable.Attribute ) ) continue;
				if ( Floats( Value( variable.Attribute ) ) is { } floats ) attributes.Set( variable.Attribute, floats );
			}

			CloseScope();
			var written = dispatchWritten;
			var sampled = dispatchSampled;
			var sampledMips = dispatchSampledMips;
			written.Clear();
			sampled.Clear();
			sampledMips.Clear();
			foreach ( var resource in variant.Resources )
			{
				if ( resource.Name is not { } name ) continue;
				var storage = resource.Kind == ShaderResourceKind.StorageTexture;
				switch ( Value( name ) )
				{
					case TextureMip { Texture: BackendTexture texture, Mip: var mip }:
						attributes.Set( name, MipView( texture, mip ) );
						if ( storage ) written.Add( texture );
						else sampledMips.Add( (texture, mip) );
						break;

					case BackendTexture texture when resource.Kind is ShaderResourceKind.Texture or ShaderResourceKind.StorageTexture:
						attributes.Set( name, texture.Handle );
						if ( storage ) written.Add( texture );
						else sampled.Add( texture );
						break;

					case GpuBuffer { Resource: BackendBuffer buffer }:
						attributes.Set( name, buffer.Handle );
						break;

					case byte[] data when resource.Kind == ShaderResourceKind.ConstantBuffer:
						attributes.SetData( name, data );
						break;
				}
			}

			// Writes in General; reads in ShaderRead, a read mip of a written texture only for this dispatch
			foreach ( var texture in written ) Transition( texture, ImageLayout.General );
			foreach ( var texture in sampled ) if ( !written.Contains( texture ) ) Transition( texture, ImageLayout.ShaderRead );
			foreach ( var (texture, mip) in sampledMips )
			{
				if ( written.Contains( texture ) ) List.Barrier( MipBarrier( texture.Handle, mip, ImageLayout.General, ImageLayout.ShaderRead ) );
				else Transition( texture, ImageLayout.ShaderRead );
			}

			drawer.Dispatch( List, shader, attributes, x, y, z );
			recording.Pipeline = default;
			recording.Binder = null;

			foreach ( var (texture, mip) in sampledMips )
			{
				if ( written.Contains( texture ) ) List.Barrier( MipBarrier( texture.Handle, mip, ImageLayout.ShaderRead, ImageLayout.General ) );
			}
		}
	}

	static readonly StringToken MipLevel0 = new( "MipLevel0" ), MipLevel1 = new( "MipLevel1" ), TextureSize = new( "TextureSize" ), InvTextureSize = new( "InvTextureSize" ), DownsampleMethod = new( "D_DOWNSAMPLE_METHOD" );
	static readonly StringToken MaskOutput = new( "Output" ), MaskMip = new( "Mip" );

	/// <summary>The frame buffer copy's mask and downsample parameters, kept between frames.</summary>
	readonly BackendParameters maskParameters = new(), mipParameters = new();

	/// <summary>
	/// The frame buffer copy's mips, as native's <c>CSceneSystem::DownsampleTexture</c> makes them after the copy: first
	/// <c>depthaware_mask_cs</c> over the copy (where something is nearer than the glass in <c>RefractionDepthTexture</c>,
	/// the last frame's blurred copy reprojected, so nearer objects stay out of the refraction), then a gaussian
	/// <c>downsample_cs</c> dispatch a mip, each from the one above, over the viewport's part.
	/// </summary>
	void GenerateFrameBufferCopyMips( BackendTexture copy, Rect viewport )
	{
		if ( copy.Mips <= 1 ) return;

		if ( device.MaterialFromShader( "shaders/depthaware_mask_cs.shader" ) is BackendMaterial { Shader: not null } mask )
		{
			maskParameters.Set( MaskOutput, copy, 0 );
			maskParameters.Set( MaskMip, copy, 1 );
			Dispatch( mask, maskParameters, copy.Width, copy.Height, 1 );
			List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		}

		if ( device.MaterialFromShader( "shaders/downsample_cs.shader" ) is not BackendMaterial { Shader: not null } downsample ) return;

		var parameters = mipParameters;
		parameters.SetCombo( DownsampleMethod, 1 );
		for ( int i = 0; i < copy.Mips - 1; i++ )
		{
			var width = Math.Max( 1, (int)viewport.Width >> (i + 1) );
			var height = Math.Max( 1, (int)viewport.Height >> (i + 1) );
			parameters.Set( MipLevel0, copy, i );
			parameters.Set( MipLevel1, copy, i + 1 );
			parameters.Set( TextureSize, new Vector2( width, height ) );
			parameters.Set( InvTextureSize, new Vector2( 1.0f / width, 1.0f / height ) );
			Dispatch( downsample, parameters, width, height, 1 );
			List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		}
	}

	/// <summary>
	/// Destroy the mip views compute made of a texture's image: when the texture goes or takes another image. They were kept
	/// for good before, one set per texture ever dispatched over.
	/// </summary>
	internal static void ForgetMipViews( BackendTexture texture, Device gpu )
	{
		if ( !mipViews.TryGetValue( texture, out var views ) ) return;
		lock ( views )
		{
			foreach ( var view in views.Values ) gpu.Destroy( view );
			views.Clear();
		}
	}

	TextureViewHandle MipView( BackendTexture texture, int mip )
	{
		var views = mipViews.GetValue( texture, static _ => new Dictionary<int, TextureViewHandle>() );
		lock ( views )
		{
			if ( views.TryGetValue( mip, out var view ) ) return view;
			view = Gpu.CreateView( texture.Handle, new TextureViewInfo { Dimension = TextureDimension.Texture2D, BaseMip = mip, MipCount = 1, LayerCount = 1 } );
			views[mip] = view;
			return view;
		}
	}
}
