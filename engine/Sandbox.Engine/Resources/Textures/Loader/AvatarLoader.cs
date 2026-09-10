using Microsoft.Extensions.Caching.Memory;
using NativeEngine;
using Steamworks;
using Steamworks.Data;
using System.Collections.Concurrent;
using System.Threading;

namespace Sandbox.TextureLoader;

/// <summary>
/// Facilitates loading of Steam user avatars.
/// </summary>
internal static class Avatar
{
	/// <summary>
	/// Caches raw avatar pixel data from Steam API responses to avoid repeat network requests.
	/// </summary>
	static readonly MemoryCache _dataCache = new( new MemoryCacheOptions() );

	/// <summary>
	/// Caches generated (Bot/Streamer Mode) avatar textures so we don't re-render them on every request.
	/// </summary>
	static readonly ConcurrentDictionary<ulong, Texture> _generatedCache = new();

	/// <summary>
	/// Weak references to every avatar texture, keyed by their url.
	/// So we can re-resolve them (real vs generated) when Streamer Mode is toggled at runtime.
	/// </summary>
	static readonly ConcurrentDictionary<string, WeakReference<Texture>> _loaded = new();

	static Avatar()
	{
		ConVarSystem.ConVarChanged += OnConVarChanged;
	}

	record struct AvatarData( int Width, int Height, byte[] Pixels );

	static void OnConVarChanged( Command command, string oldValue )
	{
		if ( command.Name.Equals( "streamer_mode", System.StringComparison.OrdinalIgnoreCase ) )
			RefreshAll();
	}

	// Reload every live avatar so it swaps between its real and generated variant. Dead refs are pruned here.
	static void RefreshAll()
	{
		foreach ( var (url, weak) in _loaded )
		{
			if ( weak.TryGetTarget( out var texture ) )
				_ = LoadIntoTexture( url, texture );
			else
				_loaded.TryRemove( url, out _ );
		}
	}

	static void Track( string url, Texture texture )
	{
		if ( texture is not null )
			_loaded[url] = new WeakReference<Texture>( texture );
	}

	internal static bool IsAppropriate( string url )
	{
		return url.StartsWith( "avatar:" ) || url.StartsWith( "avatarbig:" ) || url.StartsWith( "avatarsmall:" );
	}

	internal static Texture Load( string filename )
	{
		try
		{
			if ( Game.Resources.Get<Texture>( filename ) is { } cached )
			{
				Track( filename, cached );
				return cached;
			}

			var placeholder = Texture.Create( 1, 1 ).WithName( "avatar" ).WithData( new byte[4] { 0, 0, 0, 0 } ).Finish();
			placeholder.IsLoaded = false;
			placeholder.RegisterWeakResourceId( filename );

			Track( filename, placeholder );

			_ = LoadIntoTexture( filename, placeholder );

			return placeholder;
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"Couldn't Load Avatar {filename} ({e.Message})" );
			return null;
		}
	}

	internal static async Task LoadIntoTexture( string url, Texture placeholder, CancellationToken ct = default )
	{
		try
		{
			int size = 0;
			var filename = url;

			if ( filename.StartsWith( "avatar:" ) )
			{
				filename = filename.Substring( "avatar:".Length );
			}

			if ( filename.StartsWith( "avatarbig:" ) )
			{
				filename = filename.Substring( "avatarbig:".Length );
				size = 1;
			}

			if ( filename.StartsWith( "avatarsmall:" ) )
			{
				filename = filename.Substring( "avatarsmall:".Length );
				size = 2;
			}

			filename = filename.Trim( '/', ' ' );

			if ( !ulong.TryParse( filename, out var steamid ) )
			{
				Log.Warning( $"AvatarLoader - Couldn't parse steamid {filename}" );
				return;
			}

			//
			// Bots and Streamer Mode users get a deterministic, generated avatar
			//
			if ( Preferences.StreamerMode || Utility.Steam.IsFakeSteamId( steamid ) )
			{
				var generated = _generatedCache.GetOrAdd( steamid, GenerateAvatar );
				placeholder.CopyFrom( generated );
				placeholder.IsLoaded = true;
				return;
			}

			if ( ct.IsCancellationRequested ) return;

			// Cache key based on resolved steamid + size
			var cacheKey = $"{steamid}:{size}";

			if ( !_dataCache.TryGetValue( cacheKey, out AvatarData cached ) )
			{
				var result = size == 0 ? await SteamFriends.GetMediumAvatarAsync( steamid ) :  // 0
							(size == 1 ? await SteamFriends.GetLargeAvatarAsync( steamid ) :  // 1
										 await SteamFriends.GetSmallAvatarAsync( steamid )); // 2
				if ( !result.HasValue )
				{
					Log.Warning( $"AvatarLoader - Couldn't get avatar for {steamid} ({url})" );
					return;
				}

				cached = new AvatarData( (int)result.Value.Width, (int)result.Value.Height, result.Value.Data );
				_dataCache.Set( cacheKey, cached, new MemoryCacheEntryOptions()
					.SetSlidingExpiration( TimeSpan.FromMinutes( 10 ) ) );
			}

			//Log.Info( $"Got Avatar For {steamid} ({cached.Width} x {cached.Height})" );

			if ( ct.IsCancellationRequested ) return;

			// Streamer Mode could have changed during the fetch above, don't copy a real avatar if true
			if ( Preferences.StreamerMode )
			{
				placeholder.CopyFrom( _generatedCache.GetOrAdd( steamid, GenerateAvatar ) );
				placeholder.IsLoaded = true;
				return;
			}

			using var texture = Texture.Create( cached.Width, cached.Height, ImageFormat.RGBA8888 )
					.WithName( "avatar" )
					.WithData( cached.Pixels )
					.Finish();

			//
			// Replace the placeholder texture with this loaded one
			//
			placeholder.CopyFrom( texture );
			placeholder.IsLoaded = true;

			if ( size != 0 )
				return;

			if ( ct.IsCancellationRequested ) return;

			//
			// If we want the animated texture, request if they have it and load it from the url.
			//
			var callback = Steam.SteamFriends().RequestEquippedProfileItems( steamid );
			var r = await new CallResult<EquippedProfileItems_t>( callback, false );
			if ( !r.HasValue || !r.Value.HasAnimatedAvatar )
				return;

			var item = Steam.SteamFriends().GetProfileItemPropertyString( steamid, 0, 0 );
			if ( string.IsNullOrWhiteSpace( item ) )
				return;

			// Streamer Mode could have been enabled during the awaits above, don't apply a real animated avatar if true
			if ( ct.IsCancellationRequested || Preferences.StreamerMode )
				return;

			//
			// Download animated image into placeholder
			//
			await placeholder.ReplacementAsync( ImageUrl.LoadFromUrl( item, ct ) );
		}
		finally
		{
			placeholder.IsLoaded = true;
		}
	}

	static readonly string[] AvatarIcons =
	{
		"pets", "rocket_launch", "favorite", "star", "bolt", "cruelty_free", "sports_esports", "mood",
		"emoji_emotions", "ac_unit", "local_fire_department", "spa", "diamond", "park", "sailing", "flight",
		"anchor", "cake", "icecream", "sports_basketball", "sports_soccer", "music_note", "headphones",
		"photo_camera", "palette", "brush", "extension", "casino", "savings", "psychology", "coffee",
		"forest", "water_drop", "wb_sunny", "dark_mode", "auto_awesome", "celebration", "bug_report"
	};

	static Texture GenerateAvatar( ulong id )
	{
		const int size = 256;

		var rng = new Random( unchecked((int)id) );
		var bgHue = rng.Float( 0f, 360f );
		var background = (Color)new ColorHsv( bgHue, rng.Float( 0.45f, 0.7f ), rng.Float( 0.45f, 0.7f ) );

		using var bitmap = new Bitmap( size, size );

		// Some avatars get a gradient background, some get a solid fill.
		if ( rng.Float( 0f, 1f ) < 0.5f )
		{
			var second = (Color)new ColorHsv( (bgHue + rng.Float( -35f, 35f ) + 360f) % 360f, rng.Float( 0.5f, 0.75f ), rng.Float( 0.25f, 0.45f ) );
			bitmap.SetLinearGradient( new Vector2( 0, 0 ), new Vector2( 0, size ), Gradient.FromColors( background, second ) );
		}
		else
		{
			bitmap.SetFill( background );
		}

		bitmap.DrawRect( bitmap.Rect );

		// Light, low-saturation icon colour (so it's still visible against the background)
		var icon = AvatarIcons[rng.Next( AvatarIcons.Length )];
		var iconColor = (Color)new ColorHsv( rng.Float( 0f, 360f ), rng.Float( 0f, 0.25f ), rng.Float( 0.9f, 1f ) );

		bitmap.DrawText( new TextRendering.Scope( icon, iconColor, size * 0.5f, "Material Icons" ), bitmap.Rect, TextFlag.Center | TextFlag.DontClip );

		return bitmap.ToTexture();
	}
}
