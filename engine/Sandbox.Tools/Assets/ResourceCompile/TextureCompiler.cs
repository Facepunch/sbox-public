using Sandbox.Resources;

namespace Editor;


[Expose]
[ResourceIdentity( "texture" )]
[ResourceIdentity( "vtex" )]
public class TextureResourceCompiler : ResourceCompiler
{
	public TextureResourceCompiler()
	{
	}

	/// <summary>
	/// We found an embedded resource definition.
	/// 1. Find the TextureGenerator
	/// 2. Create a child texture resource with a deterministic name
	/// 3. Put the provided compile data in that and let it compile
	/// 4. Store a reference to the compiled version in the json
	/// </summary>
	protected override bool CompileEmbedded( ref EmbeddedResource embed )
	{
		return CompileEmbeddedResource<Texture>( ref embed, "textures", "vtex", FileSystem.Transient );
	}

	override protected async Task<bool> Compile()
	{
		if ( !TryParseEmbeddedResource( out var serialized ) || !serialized.HasValue )
			return false;

		var generator = ResourceGenerator.Create<Texture>( serialized.Value );
		if ( generator is null || !generator.CacheToDisk ) return false;

		var texture = await generator.CreateAsync( new ResourceGenerator.Options { ForDisk = true, Compiler = this }, default );
		if ( texture is null ) return false;

		Context.ResourceVersion = 1;

		//
		// Note: mipmaps don't work with PNG format ya doink
		//

		int width = texture.Width;
		int height = texture.Height;
		int depth = texture.Depth;
		int mipCount = texture.Mips;

		var writer = new VTexWriter();

		for ( var mip = 0; mip < mipCount; mip++ )
		{
			var bitmap = texture.GetBitmap( mip );
			writer.SetTexture( bitmap, mip );
		}

		writer.Header.Width = (ushort)width;
		writer.Header.Height = (ushort)height;
		writer.Header.Depth = (ushort)depth;
		writer.Header.MipCount = (byte)mipCount;

		var desc = texture.Desc;
		var flags = VTexWriter.VTEX_Flags_t.NONE;

		if ( desc.m_nFlags.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_CUBE_TEXTURE ) )
		{
			flags |= VTexWriter.VTEX_Flags_t.VTEX_FLAG_CUBE_TEXTURE;
		}

		if ( desc.m_nFlags.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE ) )
		{
			flags |= VTexWriter.VTEX_Flags_t.VTEX_FLAG_VOLUME_TEXTURE;
		}

		if ( desc.m_nFlags.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_ARRAY ) )
		{
			flags |= VTexWriter.VTEX_Flags_t.VTEX_FLAG_TEXTURE_ARRAY;
		}

		if ( desc.m_nFlags.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_NO_LOD ) )
		{
			flags |= VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD;
		}

		writer.Header.Flags = flags;

		writer.CalculateFormat();

		Context.Data.Write( writer.GetData() );
		Context.StreamingData.Write( writer.GetStreamingData() );

		return true;
	}
}
