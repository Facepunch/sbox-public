using Sandbox.UI;
using System.Collections.Concurrent;

namespace Sandbox;

[Expose]
public static partial class TextRendering
{
	// this might seem like a weird way to expose this, but let me explain my logic
	//
	// Scope will contain a bunch of shit to let us add letter spacing and shadows and shit
	// But then we'll have GetOrCreateTexture that lets you create a texture with multiple scopes
	// so you can basically render rich text, with different styles in different sections.
	//
	// This will stop using GetOrCreateTexture eventually, and will replace all of its functionality.
	//
	// I think we can switch the built in UI label to use this stuff too, if we make a version that 
	// instead of looking in a cache, just returns a self managed TextBlock or something.

	/// <summary>
	/// Create a texture from the scope. The texture will either be a cached version or will be rendered immediately
	/// </summary>
	public static Texture GetOrCreateTexture( in Scope scope, Vector2 clip = default, TextFlag flag = TextFlag.LeftTop )
	{
		if ( Application.IsHeadless )
			return Texture.Invalid;

		if ( clip == default ) clip = 8096;

		var tb = GetOrCreateTextBlock( scope, flag, clip );
		tb.MakeReady();
		return tb.Texture;
	}

	/// <summary>
	/// Resolves or creates a fully initialized <see cref="TextBlock"/> for the given scope.
	/// Safe to call from any thread. MakeReady() must still be called on the render thread before drawing.
	/// </summary>
	private static TextBlock GetOrCreateTextBlock( in Scope scope, TextFlag flag, Vector2 clip )
	{
		if ( Application.IsHeadless ) return null;

		var hc = new HashCode();
		hc.Add( scope );
		hc.Add( new Vector2( clip ) );
		hc.Add( flag );

		var hash = hc.ToHashCode();

		if ( Dictionary.TryGetValue( hash, out var tb ) )
			return tb;

		// Build a fully initialized candidate before publishing.
		// If another thread wins the race, their instance is returned and ours is discarded.
		var candidate = new TextBlock();
		candidate.Clip = clip;
		candidate.Flags = flag;
		candidate.Initialize( scope );

		return Dictionary.GetOrAdd( hash, candidate );
	}

	static ConcurrentDictionary<int, TextBlock> Dictionary = new();

	static RealTimeSince _timeSinceCleanup;

	/// <summary>
	/// Free old, unused textblocks (and their textures)
	/// </summary>
	internal static void Tick()
	{
		Assert.False( Application.IsHeadless );

		if ( _timeSinceCleanup < 0.5f ) return;
		_timeSinceCleanup = 0;

		int total = Dictionary.Count;
		int deleted = 0;

		foreach ( var item in Dictionary )
		{
			if ( item.Value.TimeSinceUsed < 1.5f ) continue;

			item.Value.Dispose();
			Dictionary.TryRemove( item );
			deleted++;
		}

		//Log.Info( $"TextManager: {total} ({deleted} deleted)" );
	}

	internal static void Shutdown()
	{
		foreach ( var item in Dictionary )
		{
			item.Value.Dispose();
		}
		Dictionary.Clear();
	}
}
