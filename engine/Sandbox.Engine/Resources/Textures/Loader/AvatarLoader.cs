using Microsoft.Extensions.Caching.Memory;
using NativeEngine;
using Steamworks;
using Steamworks.Data;

namespace Sandbox.TextureLoader;

/// <summary>
/// Facilitates loading of Steam user avatars.
/// </summary>
internal static class Avatar
{
	/// <summary>
	/// Entries are cached on a sliding window, they will be released if not used for 10 minutes
	/// </summary>
	static readonly MemoryCache _cache = new( new MemoryCacheOptions() );

	internal static bool IsAppropriate( string url )
	{
		return url.StartsWith( "avatar:" ) || url.StartsWith( "avatarbig:" ) || url.StartsWith( "avatarsmall:" );
	}

	internal static Texture Load( string filename )
	{
		try
		{
			return _cache.GetOrCreate( filename, entry =>
			{
				entry.SetSlidingExpiration( TimeSpan.FromMinutes( 10 ) );

				var placeholder = Texture.Create( 1, 1 ).WithName( "avatar" ).WithData( new byte[4] { 0, 0, 0, 0 } ).Finish();
				placeholder.IsLoaded = false;

				_ = LoadIntoTexture( filename, placeholder );

				return placeholder;
			} );
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"Couldn't Load Avatar {filename} ({e.Message})" );
			return null;
		}
	}

	internal static async Task LoadIntoTexture( string url, Texture placeholder )
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
			// Bots, lets find steam profiles with Simpsons avatars and use those
			// Edit: I could only find like 6 so lets use a bunch of random ones
			//
			if ( steamid >= 90071996842377216 )
			{
				steamid = SandboxSystem.Random.FromArray( new ulong[]
				{
				76561198076731362,
				76561198115447501,
				76561198081295106,
				76561198165412225,
				76561198023414915,
				76561198176366622,
				76561198092430664,
				76561198066084037,
				76561198368894435,
				76561198389241377,
				76561198158965172,
				76561198306626714,
				76561198208716648,
				76561198835780877,
				76561197970331648,
				76561198051740093,
				76561198111069943,
				76561198075423731,
				76561197965588718,
				76561197960316241,
				76561198361294115,
				76561197960555384,
				76561198021354850,
				76561198207495888,
				76561198040673812,
				76561198241363850,
				76561198151921867,
				76561198095212046,
				76561198169445087
				} );
			}

			var result = size == 0 ? await SteamFriends.GetMediumAvatarAsync( steamid ) :  // 0
						(size == 1 ? await SteamFriends.GetLargeAvatarAsync( steamid ) :  // 1
									 await SteamFriends.GetSmallAvatarAsync( steamid )); // 2
			if ( !result.HasValue )
			{
				Log.Warning( $"AvatarLoader - Couldn't get avatar for {steamid} ({url})" );
				return;
			}

			//Log.Info( $"Got Avatar For {steamid} ({result.Value.Width} x {result.Value.Height})" );

			var texture = Texture.Create( (int)result.Value.Width, (int)result.Value.Height, ImageFormat.RGBA8888 )
						.WithName( "avatar" )
						.WithData( result.Value.Data )
						.Finish();

			//
			// Replace the placeholder texture with this loaded one
			//
			placeholder.CopyFrom( texture );
			placeholder.IsLoaded = true;

			if ( size != 0 )
				return;

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

			//
			// Download animated image into placeholder
			//
			await placeholder.ReplacementAsync( ImageUrl.LoadFromUrl( item ) );
		}
		finally
		{
			placeholder.IsLoaded = true;
		}
	}
}
