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

		var hc = new HashCode();
		hc.Add( scope );
		hc.Add( clip );
		hc.Add( flag );

		// TextManager is caching this right now
		var tb = GetOrCreateTextBlock( hc.ToHashCode(), out bool created );

		if ( created )
		{
			tb.Clip = clip;
			tb.Flags = flag;
			tb.Initialize( scope );
		}

		tb.MakeReady();
		return tb.Texture;
	}

	static ConcurrentDictionary<int, TextBlock> Dictionary = new();

	/// <summary>
	/// We don't expose this because we don't want them to do something stupid like free
	/// a textblock that they're still using
	/// </summary>
	internal static TextBlock GetOrCreateTextBlock( int hash, out bool created )
	{
		Assert.False( Application.IsHeadless );

		created = false;

		if ( Dictionary.TryGetValue( hash, out var textBlock ) )
			return textBlock;

		created = true;
		textBlock = new TextBlock();
		Dictionary[hash] = textBlock;
		return textBlock;
	}

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

	internal static void ClearCache()
	{
		foreach ( var item in Dictionary )
		{
			item.Value.Dispose();
		}
		Dictionary.Clear();
	}
}
