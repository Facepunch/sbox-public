using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Sandbox;

/// <summary>
/// A YouTube video playing in a <see cref="UI.WebPanel"/>, in YouTube's embedded player with its own controls off - so
/// the controls can be ours. Commands go in as script; what the player's doing comes back in the page's title. From
/// <see cref="MenuUtility.PlayYouTubeVideo"/>.
/// </summary>
public sealed class YouTubePlayer
{
	/// <summary>
	/// A real page the player's put in - YouTube's embedded player only plays in a page embedding it, with an origin
	/// (error 153 otherwise, in any browser), and not one of YouTube's own (error 152). Ours, and tiny.
	/// </summary>
	const string HostPage = "https://sbox.game/robots.txt";

	/// <summary>
	/// What the page's title starts with once the player's ready - the state follows it, a few times a second
	/// </summary>
	const string StatePrefix = "yt|";

	static readonly Regex VideoIdPattern = new( "^[A-Za-z0-9_-]{6,20}$" );

	readonly UI.WebPanel panel;
	readonly WebSurface surface;

	/// <summary>
	/// The video's YouTube id
	/// </summary>
	public string VideoId { get; }

	/// <summary>
	/// Its panel's still there - the player goes when it does
	/// </summary>
	public bool IsValid => panel.IsValid();

	internal YouTubePlayer( UI.WebPanel panel, string videoId )
	{
		this.panel = panel;
		surface = panel.Surface;
		VideoId = videoId;

		// Once - writing the page can finish another load
		var written = false;
		surface.OnPageLoaded = url =>
		{
			if ( written || url is null || !url.StartsWith( HostPage ) ) return;

			written = true;
			surface.ExecuteJavascript( WritePageScript( videoId ) );
			_ = StartWhenReady();
		};

		// The surface's own url, not the panel's - the panel puts the user's session on sbox.game urls, and nothing
		// needs it here
		surface.Url = HostPage;
	}

	internal static bool IsVideoId( string videoId ) => videoId is not null && VideoIdPattern.IsMatch( videoId );

	/// <summary>
	/// The page the player's written into: YouTube's player api puts the player in, with its controls off, and the
	/// page's title says what it's doing
	/// </summary>
	static string WritePageScript( string videoId )
	{
		var page = $$"""
			<!doctype html>
			<html><head><style>
			html, body { margin: 0; height: 100%; background: #000; overflow: hidden; }
			#player { position: fixed; inset: 0; width: 100%; height: 100%; border: 0; }
			</style></head><body>
			<div id="player"></div>
			<script>
			function report() {
				var p = window.player;
				if ( !p || !p.getPlayerState ) return;
				document.title = '{{StatePrefix}}' + p.getPlayerState() + '|' + p.getCurrentTime().toFixed( 2 ) + '|' + p.getDuration().toFixed( 2 )
					+ '|' + p.getVideoLoadedFraction().toFixed( 3 ) + '|' + p.getVolume() + '|' + ( p.isMuted() ? 1 : 0 );
			}
			window.onYouTubeIframeAPIReady = function() {
				window.player = new YT.Player( 'player', {
					width: '100%', height: '100%', videoId: '{{videoId}}',
					playerVars: { controls: 0, disablekb: 1, fs: 0, iv_load_policy: 3, rel: 0, playsinline: 1 },
					events: { onReady: function() { report(); setInterval( report, 250 ); }, onStateChange: report }
				} );
			};
			</script>
			<script src="https://www.youtube.com/iframe_api"></script>
			</body></html>
			""";

		return $"document.open(); document.write({System.Text.Json.JsonSerializer.Serialize( page )}); document.close();";
	}

	/// <summary>
	/// Click the middle of the player once it's ready - a video only plays with sound after a click from inside the
	/// page. After that, <see cref="Play"/> can start it.
	/// </summary>
	async Task StartWhenReady()
	{
		for ( var waited = 0; waited < 15000; waited += 100 )
		{
			if ( !IsValid ) return;

			if ( IsReady )
			{
				var middle = surface.Size / 2;
				surface.TellMouseMove( middle );
				surface.TellMouseButton( MouseButtons.Left, true );
				surface.TellMouseButton( MouseButtons.Left, false );
				return;
			}

			await Task.Delay( 100 );
		}
	}

	//
	// What it's doing - read from the page's title, which the page keeps up to date
	//

	string parsedTitle;
	string[] state = Array.Empty<string>();

	string[] State
	{
		get
		{
			var title = surface.PageTitle;
			if ( title == parsedTitle ) return state;

			parsedTitle = title;
			state = title?.StartsWith( StatePrefix ) ?? false ? title[StatePrefix.Length..].Split( '|' ) : Array.Empty<string>();
			return state;
		}
	}

	int StateInt( int index ) => State.Length > index && int.TryParse( State[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v ) ? v : 0;
	float StateFloat( int index ) => State.Length > index && float.TryParse( State[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ? v : 0;

	/// <summary>
	/// The player's loaded and taking commands
	/// </summary>
	public bool IsReady => State.Length >= 6;

	/// <summary>
	/// YouTube's player state: -1 unstarted, 0 ended, 1 playing, 2 paused, 3 buffering, 5 cued
	/// </summary>
	int PlayerState => IsReady ? StateInt( 0 ) : -1;

	public bool IsPlaying => PlayerState == 1;
	public bool IsBuffering => PlayerState == 3;
	public bool HasEnded => PlayerState == 0;

	/// <summary>
	/// Where it's at, in seconds
	/// </summary>
	public float Time => StateFloat( 1 );

	/// <summary>
	/// How long it is, in seconds - 0 until it's known
	/// </summary>
	public float Duration => StateFloat( 2 );

	/// <summary>
	/// How much of it's loaded, 0 to 1
	/// </summary>
	public float Buffered => StateFloat( 3 );

	/// <summary>
	/// Its volume, 0 to 100
	/// </summary>
	public int Volume => StateInt( 4 );

	public bool IsMuted => StateInt( 5 ) == 1;

	//
	// Telling it what to do
	//

	void Run( string call )
	{
		if ( IsValid ) surface.ExecuteJavascript( $"window.player && window.player.{call};" );
	}

	public void Play() => Run( "playVideo()" );
	public void Pause() => Run( "pauseVideo()" );

	/// <summary>
	/// Play it if it isn't, pause it if it is - from the start again if it's ended
	/// </summary>
	public void TogglePlaying()
	{
		if ( IsPlaying || IsBuffering ) Pause();
		else Play();
	}

	/// <summary>
	/// Go to this many seconds in
	/// </summary>
	public void Seek( float seconds ) => Run( $"seekTo({Math.Max( seconds, 0 ).ToString( "0.##", CultureInfo.InvariantCulture )}, true)" );

	/// <summary>
	/// Set its volume, 0 to 100
	/// </summary>
	public void SetVolume( int volume ) => Run( $"setVolume({Math.Clamp( volume, 0, 100 )})" );

	public void Mute() => Run( "mute()" );
	public void Unmute() => Run( "unMute()" );

	public void ToggleMuted()
	{
		if ( IsMuted ) Unmute();
		else Mute();
	}
}
