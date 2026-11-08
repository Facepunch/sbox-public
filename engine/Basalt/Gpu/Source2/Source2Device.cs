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
}
