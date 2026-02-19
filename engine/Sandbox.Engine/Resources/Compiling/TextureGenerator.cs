using System.Threading;

namespace Sandbox.Resources;

public abstract class TextureGenerator : ResourceGenerator<Texture>
{
	/// <summary>
	/// Tracks all textures created by texture generators so they can be
	/// regenerated in-place when code is hotloaded.
	/// </summary>
	static readonly List<WeakReference<Texture>> GeneratedTextures = new();
	static bool IsRegenerating;

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

		if ( !IsRegenerating )
		{
			GeneratedTextures.Add( new WeakReference<Texture>( output ) );
		}

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
		return new EmbeddedResource
		{
			ResourceCompiler = "texture",
			ResourceGenerator = DisplayInfo.For( this ).ClassName ?? GetType().FullName,
			Data = Json.SerializeAsObject( this )
		};
	}

	/// <summary>
	/// Called on hotload to regenerate all tracked textures in-place.
	/// This ensures textures created by generators reflect the latest code
	/// without needing to replace object references held by the scene.
	/// </summary>
	internal static void OnHotload()
	{
		// Remove dead references
		GeneratedTextures.RemoveAll( wr => !wr.TryGetTarget( out _ ) );

		if ( GeneratedTextures.Count == 0 )
			return;

		IsRegenerating = true;

		try
		{
			foreach ( var weakRef in GeneratedTextures.ToArray() )
			{
				if ( !weakRef.TryGetTarget( out var texture ) )
					continue;

				if ( texture.EmbeddedResource is not { } embedded )
					continue;

				if ( string.IsNullOrEmpty( embedded.ResourceGenerator ) )
					continue;

				try
				{
					RegenerateTexture( texture, embedded );
				}
				catch ( System.Exception e )
				{
					Log.Warning( e, $"Failed to regenerate texture from {embedded.ResourceGenerator}" );
				}
			}
		}
		finally
		{
			IsRegenerating = false;
		}
	}

	/// <summary>
	/// Regenerate a single texture in-place from its embedded resource data.
	/// </summary>
	static void RegenerateTexture( Texture texture, EmbeddedResource embedded )
	{
		// Create a fresh generator instance from the (possibly updated) TypeLibrary
		var generator = ResourceGenerator.Create<Texture>( embedded );
		if ( generator is not TextureGenerator texGen )
			return;

		// Run CreateTexture with the new code
		var task = texGen.CreateTexture( Options.Default, default );

		if ( task.IsCompletedSuccessfully )
		{
			// Sync path: replace immediately
			var newTexture = task.Result;
			if ( newTexture is not null )
			{
				texture.CopyFrom( newTexture );
			}
		}
		else
		{
			// Async path: replace when ready
			_ = texture.ReplacementAsync( task.AsTask() );
		}
	}
}
