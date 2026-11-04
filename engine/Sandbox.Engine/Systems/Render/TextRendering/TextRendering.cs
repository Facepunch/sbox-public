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
		if ( !Graphics.IsAvailable )
			return Texture.Invalid;

		var tb = GetOrCreateTextBlock( scope, flag, clip );
		tb.MakeReady();
		return tb.Texture ?? Texture.Invalid;
	}

	/// <summary>
	/// Resolves or creates a laid out <see cref="TextBlock"/> for the given scope, null when headless. Safe to call
	/// from any thread. MakeReady() must still be called on the render thread before drawing its texture.
	/// </summary>
	internal static TextBlock GetOrCreateTextBlock( in Scope scope, TextFlag flag, Vector2 clip = default )
	{
		if ( Application.IsHeadless ) return null;
		if ( clip == default ) clip = 8096;

		var hc = new HashCode();
		hc.Add( scope );
		hc.Add( new Vector2( clip ) );
		hc.Add( flag );

		var hash = hc.ToHashCode();

		if ( Dictionary.TryGetValue( hash, out var tb ) )
		{
			tb.EnsureLayout();
			return tb;
		}

		// Build a fully initialized candidate before publishing.
		// If another thread wins the race, their instance is returned and ours is discarded.
		var candidate = new TextBlock();
		candidate.Clip = clip;
		candidate.Flags = flag;
		candidate.Initialize( scope );
		candidate.EnsureLayout();

		// GetOrAdd is race-safe: only one instance wins the slot.
		// Set CacheKey on the winner so MakeReady can re-register if Tick() evicts it.
		var winner = Dictionary.GetOrAdd( hash, candidate );
		winner.CacheKey = hash;
		return winner;
	}

	static ConcurrentDictionary<int, TextBlock> Dictionary = new();

	const ulong UnusedFrameLimit = 2;
	static RealTimeSince _timeSinceCleanup;

	/// <summary>
	/// Evict unused text blocks. Callers can still retain their textures.
	/// </summary>
	internal static void Tick()
	{
		if ( Graphics.IsAvailable ) GpuFontText.PreloadShader();

		Assert.False( Application.IsHeadless );

		if ( _timeSinceCleanup < 0.5f ) return;
		_timeSinceCleanup = 0;

		foreach ( var item in Dictionary )
		{
			// Preparation also counts: measuring text need not submit its texture to the GPU.
			if ( Application.FrameCount - item.Value.LastPreparedFrame <= UnusedFrameLimit ) continue;
			// LastUsed is measured in resource frames, not seconds.
			if ( item.Value.Texture?.LastUsed <= (int)UnusedFrameLimit ) continue;

			Dictionary.TryRemove( item );
		}
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
