using System.Threading;

namespace Sandbox.Resources;

public abstract class TextureGenerator : ResourceGenerator<Texture>
{
	/// <summary>
	/// When set, the compiled texture will use this format instead of automatically determining one.
	/// Useful to avoid block compression (BC1/BC7) for textures that require pixel-perfect quality (sprites, UI, icons).
	/// </summary>
	[Hide]
	public virtual ImageFormat? FormatOverride => null;

	/// <summary>
	/// The image this makes on the CPU, for the caller to dispose, or null if it makes its texture another way. Compiling a
	/// generator that makes one needs no render device, so one that caches to disk should.
	/// </summary>
	protected virtual ValueTask<Bitmap> CreateBitmap( Options options, CancellationToken ct )
	{
		return default;
	}

	/// <summary>
	/// Find an existing texture for this. By default, the texture of <see cref="CreateBitmap"/>'s image.
	/// </summary>
	protected virtual async ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
	{
		using var bitmap = await CreateBitmap( options, ct );
		return bitmap?.ToTexture();
	}

	/// <summary>
	/// The image a compile writes, or null if this only makes a texture (which takes a render device to read back).
	/// </summary>
	internal ValueTask<Bitmap> CreateImage( Options options, CancellationToken ct ) => CreateBitmap( options, ct );

	/// <summary>
	/// Create a texture. Will replace a placeholder texture, which will turn into the generated texture later, if it's not immediately available.
	/// </summary>
	public sealed override Texture Create( Options options )
	{
		var tex = CreateTexture( options, default );

		Texture output = default;
		if ( !tex.IsCompletedSuccessfully )
		{
			// loading async
			output = Texture.Create( 1, 1 ).WithData( new byte[4] { 0, 0, 0, 0 } ).Finish();
			_ = output.ReplacementAsync( tex.AsTask() );
		}
		else
		{
			// finished immediately
			output = tex.Result;
		}

		if ( output is null ) return default;

		output.EmbeddedResource = CreateEmbeddedResource();
		return output;
	}

	/// <summary>
	/// Create a texture. Will wait until the texture is fully loaded and return when done.
	/// Runs in the caller's context; generators can explicitly offload work when needed.
	/// </summary>
	public sealed override async ValueTask<Texture> CreateAsync( Options options, CancellationToken token )
	{
		var output = await CreateTexture( options, token );
		if ( output is null ) return default;

		token.ThrowIfCancellationRequested();

		output.EmbeddedResource = CreateEmbeddedResource();
		return output;
	}

	public virtual EmbeddedResource? CreateEmbeddedResource()
	{
		var di = DisplayInfo.For( this );
		var data = Json.SerializeAsObject( this );

		var embed = new EmbeddedResource
		{
			ResourceCompiler = "texture",
			ResourceGenerator = di.ClassName ?? GetType().FullName,
			Data = data
		};

		// If the generator supports caching to disk, create a deterministic path for the compiled texture
		if ( CacheToDisk && EngineFileSystem.Mounted is BaseFileSystem mounted )
		{
			var generatorName = (di.ClassName ?? di.Name).ToLower().GetFilenameSafe();
			var crc = Sandbox.Utility.Crc64.FromString( data.ToJsonString() );
			var compiledPath = $"textures/generated/{generatorName}/{crc:x}.vtex";

			if ( mounted.FileExists( $"{compiledPath}_c" ) )
			{
				embed.CompiledPath = compiledPath;
			}
		}

		return embed;
	}

}
