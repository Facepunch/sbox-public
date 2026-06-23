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
	/// Find an existing texture for this
	/// </summary>
	protected virtual ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
	{
		return default;
	}

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
	/// </summary>
	public sealed override async ValueTask<Texture> CreateAsync( Options options, CancellationToken token )
	{
		// Call it completely in a new thread
		var output = await Task.Run( async () => await CreateTexture( options, token ) );
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
