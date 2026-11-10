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

		foreach ( var read in generator.CompiledReads ) Context.AddCompiledReference( read );
		if ( Context.Describing ) return true;

		var options = new ResourceGenerator.Options { ForDisk = true, Compiler = this };
		var texGen = generator as TextureGenerator;

		// The image the generator makes on the CPU, mipped as a texture made from it would be, so this needs no render
		// device. A generator that only makes a texture is read back from it, which does: without one (a build machine)
		// that fails here, rather than write a texture of nothing.
		using var image = texGen is null ? null : await texGen.CreateImage( options, default );
		if ( image is null && !Graphics.IsAvailable )
		{
			Context.Fail( $"{generator.GetType().Name} makes no image of its own, and reading its texture back takes a render device" );
			return false;
		}

		var texture = image is null ? await generator.CreateAsync( options, default ) : null;
		if ( image is null && texture is null ) return false;

		Context.ResourceVersion = 1;

		var desc = texture?.Desc ?? default;
		var flagsIn = desc.m_nFlags;

		int width = image?.Width ?? desc.m_nWidth;
		int height = image?.Height ?? desc.m_nHeight;
		int depth = image is null ? desc.m_nDepth : 1;
		int mipCount = image is null ? desc.m_nNumMipLevels : (int)System.Math.Log2( System.Math.Min( width, height ) ) + 1;

		var formatOverride = texGen?.FormatOverride;

		var writer = new VTexWriter();

		VTexWriter.VTEX_Format_t diskFormat;
		if ( formatOverride.HasValue )
			diskFormat = VTexWriter.RuntimeToVTEX_Format( formatOverride.Value ) ?? VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA8888;
		else
			diskFormat = default; // will be calculated after SetTexture

		// PNG format only supports a single mip level
		bool isPngFormat = diskFormat == VTexWriter.VTEX_Format_t.VTEX_FORMAT_PNG_RGBA8888
						|| diskFormat == VTexWriter.VTEX_Format_t.VTEX_FORMAT_PNG_DXT5;

		if ( isPngFormat )
			mipCount = 1;

		if ( width <= 0 || height <= 0 || mipCount <= 0 ) return false;

		// The writer keeps every level until it has encoded them: they're disposed after that, the image with its using
		var levels = new List<Bitmap>();
		try
		{
			var level = image;
			for ( var mip = 0; mip < mipCount; mip++ )
			{
				level = image is null ? texture.GetBitmap( mip ) : mip == 0 ? image : level.NextMip();
				if ( level != image ) levels.Add( level );
				writer.SetTexture( level, mip );
			}

			writer.Header.Width = (ushort)width;
			writer.Header.Height = (ushort)height;
			writer.Header.Depth = (ushort)depth;
			writer.Header.MipCount = (byte)mipCount;

			var flags = VTexWriter.VTEX_Flags_t.NONE;

			if ( flagsIn.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_CUBE_TEXTURE ) )
			{
				flags |= VTexWriter.VTEX_Flags_t.VTEX_FLAG_CUBE_TEXTURE;
			}

			if ( flagsIn.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE ) )
			{
				flags |= VTexWriter.VTEX_Flags_t.VTEX_FLAG_VOLUME_TEXTURE;
			}

			if ( flagsIn.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_ARRAY ) )
			{
				flags |= VTexWriter.VTEX_Flags_t.VTEX_FLAG_TEXTURE_ARRAY;
			}

			if ( flagsIn.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_NO_LOD ) || isPngFormat )
			{
				flags |= VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD;
			}

			writer.Header.Flags = flags;

			if ( formatOverride.HasValue )
				writer.Header.Format = diskFormat;
			else
				writer.CalculateFormat();

			Context.Data.Write( writer.GetData() );

			// Encoding owns only the captured bitmaps. Native compiler writes stay on the caller.
			var streamingData = await Task.Run( writer.GetStreamingData );

			Context.StreamingData.Write( streamingData );

			return true;
		}
		finally
		{
			foreach ( var level in levels ) level.Dispose();
		}
	}
}
