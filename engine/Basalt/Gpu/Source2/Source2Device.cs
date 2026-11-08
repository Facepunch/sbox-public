using NativeEngine;

namespace Basalt.Gpu.Source2;

/// <summary>
/// Basalt's device over Source 2's render device, material system and resource system, through the engine.
/// </summary>
internal sealed class Source2Device : GpuDevice
{
	public static Source2Device Instance { get; } = new();

	public override GpuTexture CreateTexture( in TextureDesc desc, string name )
	{
		Texture texture;
		if ( desc.Depth > 1 )
		{
			var volume = Texture.CreateVolume( desc.Width, desc.Height, desc.Depth, desc.Format.ToEngine() ).WithName( name );
			if ( desc.Storage ) volume = volume.WithUAVBinding();
			if ( !desc.Data.IsEmpty ) volume = volume.WithData( desc.Data.ToArray() );
			texture = volume.Finish();
		}
		else
		{
			var builder = Texture.CreateRenderTarget().WithSize( desc.Width, desc.Height ).WithFormat( desc.Format.ToEngine() )
				.WithMSAA( Source2Formats.ToAmount( desc.Samples ) );
			if ( desc.Mips > 1 ) builder = builder.WithMips( desc.Mips );
			if ( desc.Storage ) builder = builder.WithUAVBinding();
			if ( desc.GenerateMips ) builder._config.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_GEN_MIP_MAPS;
			texture = builder.Create( name );
		}

		return Source2Texture.From( texture );
	}

	public override GpuTexture LoadTexture( string path ) => Source2Texture.From( Texture.Load( path ) );

	public override GpuTexture Black => Source2Texture.From( Texture.Black );

	public override void Clear( GpuTexture texture, Color color ) => texture.Engine()?.Clear( color );

	public override IDisposable CreateBuffer( int elementCount, int elementSize, GpuBuffer.UsageFlags usage, string name )
		=> new Sandbox.GpuBuffer( elementCount, elementSize, (Sandbox.GpuBuffer.UsageFlags)usage, name );

	public override void SetData( GpuBuffer buffer, ReadOnlySpan<byte> data ) => buffer.Engine().SetData( data );

	public override ShaderParameters CreateParameters() => Source2Parameters.From( new RenderAttributes() );

	public override GpuTexture SheetMetadata => Source2Texture.From( Texture.FromNative( g_pRenderDevice.GetSheetMetaDataTexture() ) );

	public override GpuMaterial LoadMaterial( string path ) => Source2Material.From( Material.Load( path ) );

	public override GpuMaterial MaterialFromShader( string shaderPath ) => Source2Material.From( Material.FromShader( shaderPath ) );

	public override GpuMaterial CreateMaterial( string name, string shaderPath ) => Source2Material.From( Material.Create( name, shaderPath ) );

	internal override GpuContext CreateContext( GpuContext frame = null ) => frame is null ? new Source2Context() : new Source2Context( frame );

	public override bool SupportsAsyncCompute => supportsAsyncCompute ??= RenderDeviceManager.SupportsAsyncCompute();
	bool? supportsAsyncCompute;

	public override bool SupportsDrawIndirectCount => supportsDrawIndirectCount ??= RenderTools.SupportsDrawIndirectCount();
	bool? supportsDrawIndirectCount;

	public override int AllocateVertexCache( int vertices ) => RenderTools.AllocateVertexCache( vertices );

	public override unsafe Vector4 MorphSubrect( int handle )
	{
		Vector4 subrect = default;
		RenderTools.GetMorphSubrect( handle, (IntPtr)(&subrect) );
		return subrect;
	}

	public override void BeginSubmitting( GpuSwapChain swapChain ) => g_pRenderDevice.BeginSubmittingDisplayLists( swapChain.Handle );

	public override void SetSubmitTag( ulong tag ) => g_pRenderDevice.SetSubmitTag( tag );

	public override float RenderTime => CSceneSystem.GetCurrentRenderTime();

	public override IntPtr CreateConstantBuffer( int size ) => RenderTools.CreateConstantBuffer( size );

	public override void DestroyConstantBuffer( IntPtr buffer ) => RenderTools.DestroyConstantBuffer( buffer );

	public override int SwapChainSamples( GpuSwapChain swapChain ) => Source2Formats.Samples( g_pRenderDevice.GetSwapChainInfo( swapChain.Handle ).m_nMultisampleType );
}
