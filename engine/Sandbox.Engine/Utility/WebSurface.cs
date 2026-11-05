using Sandbox.Utility;
using Steamworks;
using Steamworks.Data;

namespace Sandbox;


/// <summary>
/// Enables rendering and interacting with a webpage
/// </summary>
public sealed class WebSurface : IDisposable
{
	static ISteamHTMLSurface steamHTMLSurface;
	static Dictionary<uint, WeakReference<WebSurface>> All;

	Task<HTML_BrowserReady_t> initTask;
	Action _initQueue;
	bool _disposed;

	uint BrowserId;

	public bool IsLimited { get; private set; }

	static WebSurface()
	{
		if ( Application.IsUnitTest || Application.IsHeadless )
			return;

		All = new Dictionary<uint, WeakReference<WebSurface>>();
		steamHTMLSurface = NativeEngine.Steam.SteamHTMLSurface();

		if ( !steamHTMLSurface.IsValid )
		{
			Log.Warning( "WebSurface unavailable - SteamHTMLSurface is null" );
			return;
		}

		steamHTMLSurface.Init();

		Dispatch.Install<HTML_NeedsPaint_t>( x => GetBrowser( x.UnBrowserHandle )?.OnNeedsRepaint( x ) );
		Dispatch.Install<HTML_StartRequest_t>( x => GetBrowser( x.UnBrowserHandle )?.OnStartRequest( x ) );
		Dispatch.Install<HTML_URLChanged_t>( x => GetBrowser( x.UnBrowserHandle )?.OnURLChanged( x ) );
		Dispatch.Install<HTML_ChangedTitle_t>( x => GetBrowser( x.UnBrowserHandle )?.OnChangedTitle( x ) );
		Dispatch.Install<HTML_FinishedRequest_t>( x => GetBrowser( x.UnBrowserHandle )?.OnFinishedRequest( x ) );
		Dispatch.Install<HTML_SetCursor_t>( x => GetBrowser( x.UnBrowserHandle )?.OnSetCursor( x ) );
	}

	static WebSurface GetBrowser( uint handle )
	{
		lock ( All )
		{
			if ( All.TryGetValue( handle, out var v ) && v.TryGetTarget( out var target ) )
			{
				return target;
			}

			return null;
		}
	}

	public delegate void TextureChangedDelegate( ReadOnlySpan<byte> span, Vector2 size );

	/// <summary>
	/// Called when the texture has changed and should be updated
	/// </summary>
	public TextureChangedDelegate OnTexture { get; set; }

	internal WebSurface( bool isLimited )
	{
		IsLimited = isLimited;
		_ = CreateAsync();
	}

	async Task<HTML_BrowserReady_t> CreateBrowser()
	{
		if ( !steamHTMLSurface.IsValid )
			return default;

		var callback = steamHTMLSurface.CreateBrowser( "Mozilla/5.0 (Linux; U; X11; en-US; Facepunch Sandbox; ) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/85.0.4183.121 Safari/537.36", null );
		var t = await new CallResult<HTML_BrowserReady_t>( callback, false );
		return t ?? default;
	}

	async Task CreateAsync()
	{
		initTask = CreateBrowser();
		var s = await initTask;

		if ( initTask == null )
			return;

		initTask = null;

		// Disposed while waiting for the browser, remove it before it loads anything
		if ( _disposed )
		{
			if ( s.UnBrowserHandle != 0 )
				steamHTMLSurface.RemoveBrowser( s.UnBrowserHandle );

			return;
		}

		BrowserId = s.UnBrowserHandle;

		lock ( All )
		{
			All[BrowserId] = new WeakReference<WebSurface>( this );
		}

		_initQueue?.Invoke();
		_initQueue = null;
	}

	~WebSurface()
	{
		Dispose();
	}

	public void Dispose()
	{
		_disposed = true;
		_initQueue = null;
		OnTexture = null;
		GC.SuppressFinalize( this );

		if ( BrowserId == 0 )
			return;

		lock ( All )
		{
			All.Remove( BrowserId );
			steamHTMLSurface.RemoveBrowser( BrowserId );
			BrowserId = 0;

			foreach ( var expired in All.Where( x => x.Value.TryGetTarget( out var target ) == false ).Select( x => x.Key ).ToArray() )
			{
				All.Remove( expired );
			}
		}
	}

	string currentUrl;


	public string PageTitle { get; private set; }

	/// <summary>
	/// The current Url
	/// </summary>
	public string Url
	{
		get => currentUrl;
		set
		{
			if ( BrowserId == 0 )
			{
				_initQueue += () => Url = value;
				return;
			}

			steamHTMLSurface.LoadURL( BrowserId, value ?? "", null );
		}
	}

	/*
	 *	None of this works, in my experience
	 *	
	internal void SetCookie( string hostname, string key, string value, string path = "/", bool insecure = false )
	{
		var expires = (uint)DateTime.UtcNow.AddDays( 7 ).GetEpoch();

		if ( insecure )
		{
			steamHTMLSurface.SetCookie( hostname, key, value, path, expires, false, true );
		}
		else
		{
			steamHTMLSurface.SetCookie( hostname, key, value, path, expires, true, true );
		}
	}
	*/

	static string[] blacklistHostname = new[]
	{
		"*steampowered.com",
		"*steamchina.com",
		"*steamcommunity.com",
		"*steamgames.com",
		"*steamusercontent.com",
		"*steamcontent.com",
		"*steamstatic.com",
	};

	/// <summary>
	/// Is this URL allowed
	/// </summary>
	internal static void CheckUrlIsAllowed( Uri uri )
	{
		if ( uri.Scheme != "http" && uri.Scheme != "https" )
			throw new InvalidOperationException( $"The url scheme '{uri.Scheme}' is not allowed" );

		if ( uri.IsUnc )
			throw new InvalidOperationException( $"UNC urls are not allowed" );

		if ( uri.IsFile )
			throw new InvalidOperationException( $"File urls are not allowed" );

		if ( uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6 )
			throw new InvalidOperationException( $"IP urls are not allowed" );

		var host = uri.Host;
		if ( blacklistHostname.Any( x => host.WildcardMatch( x ) ) )
			throw new InvalidOperationException( $"You cannot access this hostname" );

		// Users can opt for local http in editor, don't allow in non-editor please.
		// Opt in is so they know they're doing something that won't work normally
		if ( Application.IsEditor && CommandLine.HasSwitch( "-allowlocalhttp" ) )
			return;

		if ( uri.IsLoopback )
			throw new InvalidOperationException( $"Loopback urls are not allowed" );

		try
		{
			if ( uri.IsPrivate() )
				throw new InvalidOperationException( $"Urls that resolve to private IPs are not allowed" );
		}
		catch ( System.Net.Sockets.SocketException e )
		{
			throw new InvalidOperationException( $"Socket error: {e.Message}" );
		}




	}

	Vector2 _size;

	/// <summary>
	/// The size of the browser
	/// </summary>
	public Vector2 Size
	{
		get => _size;
		set
		{
			if ( BrowserId == 0 )
			{
				_initQueue += () => Size = value;
				return;
			}

			_size = value;
			steamHTMLSurface.SetSize( BrowserId, (uint)value.x, (uint)value.y );
		}
	}

	/// <summary>
	/// Invoked when the browser needs to be repainted
	/// </summary>
	private unsafe void OnNeedsRepaint( HTML_NeedsPaint_t x )
	{
		ReadOnlySpan<byte> s = new ReadOnlySpan<byte>( (void*)x.PBGRA, (int)(x.UnWide * x.UnTall * 4) );

		var size = new Vector2( x.UnWide, x.UnTall );
		_size = size;
		OnTexture?.Invoke( s, size );
	}

	private void OnChangedTitle( HTML_ChangedTitle_t x )
	{
		PageTitle = x.PchTitle;
	}

	/// <summary>
	/// A navigation has happened, allow or deny it
	/// </summary>
	/// <param name="r"></param>
	private unsafe void OnStartRequest( HTML_StartRequest_t r )
	{
		var url = r.PchURL;

		try
		{
			if ( IsLimited )
				CheckUrlIsAllowed( new Uri( url ) );
		}
		catch ( InvalidOperationException e )
		{
			Log.Warning( $"{e.Message}" );
			steamHTMLSurface.AllowStartRequest( BrowserId, false );
			return;
		}

		steamHTMLSurface.AllowStartRequest( BrowserId, true );
		currentUrl = url;
	}

	/// <summary>
	/// Invoked when the browser is navigating to a new url
	/// </summary>
	/// <param name="x"></param>
	private unsafe void OnURLChanged( HTML_URLChanged_t x )
	{
		currentUrl = x.PchURL;
	}


	private void OnFinishedRequest( HTML_FinishedRequest_t x )
	{
		currentUrl = x.PchURL;
		PageTitle = x.PchPageTitle;

		OnPageLoaded?.Invoke( x.PchURL );
	}

	/// <summary>
	/// A page has finished loading - its url. Internal, like <see cref="ExecuteJavascript"/>: a page can be one with the
	/// user's session in it.
	/// </summary>
	internal Action<string> OnPageLoaded { get; set; }

	/// <summary>
	/// Run script in the page that's loaded. Internal on purpose - game code mustn't run script in pages, the menu's
	/// sbox.game pages have the user's session.
	/// </summary>
	internal void ExecuteJavascript( string script )
	{
		if ( BrowserId == 0 )
		{
			_initQueue += () => ExecuteJavascript( script );
			return;
		}

		steamHTMLSurface.ExecuteJavascript( BrowserId, script );
	}

	public string Cursor { get; private set; }

	enum EMouseCursor
	{
		dc_user = 0,
		dc_none,
		dc_arrow,
		dc_ibeam,
		dc_hourglass,
		dc_waitarrow,
		dc_crosshair,
		dc_up,
		dc_sizenw,
		dc_sizese,
		dc_sizene,
		dc_sizesw,
		dc_sizew,
		dc_sizee,
		dc_sizen,
		dc_sizes,
		dc_sizewe,
		dc_sizens,
		dc_sizeall,
		dc_no,
		dc_hand,
		dc_blank, // don't show any custom cursor, just use your default
		dc_middle_pan,
		dc_north_pan,
		dc_north_east_pan,
		dc_east_pan,
		dc_south_east_pan,
		dc_south_pan,
		dc_south_west_pan,
		dc_west_pan,
		dc_north_west_pan,
		dc_alias,
		dc_cell,
		dc_colresize,
		dc_copycur,
		dc_verticaltext,
		dc_rowresize,
		dc_zoomin,
		dc_zoomout,
		dc_help,
		dc_custom,

		dc_last, // custom cursors start from this value and up
	}

	private void OnSetCursor( HTML_SetCursor_t x )
	{
		Cursor = "default";

		if ( x.EMouseCursor == (int)EMouseCursor.dc_hand )
			Cursor = "pointer";

		if ( x.EMouseCursor == (int)EMouseCursor.dc_ibeam )
			Cursor = "text";

	}

	/// <summary>
	/// Tell the browser the mouse has moved
	/// </summary>
	public void TellMouseMove( Vector2 position )
	{
		steamHTMLSurface.MouseMove( BrowserId, (int)position.x, (int)position.y );
	}

	/// <summary>
	/// Tell the browser the mouse wheel has moved
	/// </summary>
	/// <param name="delta"></param>
	public void TellMouseWheel( int delta )
	{
		steamHTMLSurface.MouseWheel( BrowserId, delta );
	}

	/// <summary>
	/// Tell the browser a mouse button has been pressed
	/// </summary>
	public void TellMouseButton( MouseButtons button, bool state )
	{
		if ( (button & MouseButtons.Left) != 0 )
		{
			if ( state ) steamHTMLSurface.MouseDown( BrowserId, 0 );
			else steamHTMLSurface.MouseUp( BrowserId, 0 );
		}

		if ( (button & MouseButtons.Right) != 0 )
		{
			if ( state ) steamHTMLSurface.MouseDown( BrowserId, 1 );
			else steamHTMLSurface.MouseUp( BrowserId, 1 );
		}

		if ( (button & MouseButtons.Middle) != 0 )
		{
			if ( state ) steamHTMLSurface.MouseDown( BrowserId, 2 );
			else steamHTMLSurface.MouseUp( BrowserId, 2 );
		}
	}

	/// <summary>
	/// Tell the browser a unicode key has been pressed
	/// </summary>
	public void TellChar( uint unicodeKey, KeyboardModifiers modifiers )
	{
		// don't allow paste
		if ( IsLimited && modifiers.Contains( KeyboardModifiers.Ctrl ) )
		{
			if ( unicodeKey == 'x' ) return;
			if ( unicodeKey == 'c' ) return;
			if ( unicodeKey == 'v' ) return;
		}

		steamHTMLSurface.KeyChar( BrowserId, unicodeKey, (int)modifiers );
	}

	/// <summary>
	/// Tell the browser a key has been pressed or released
	/// </summary>
	public void TellKey( uint virtualKeyCode, KeyboardModifiers modifiers, bool state )
	{
		//
		// Don't allow paste. Since the potential is there to open a webpage and simulate
		// key presses to past the contents of the clipboard into a hidden textarea and 
		// record that shit on a server somewhere. They could do this without the game having
		// focus - so anything happening in the background could get captured.
		//
		// There is a strategy to allow this, by having Copy, Paste, Cut functions that take some
		// special token class that only we can make, that come as part of the UI panel input systems.
		// but we'll leave that implementation until someone notices and cares.
		//


		if ( IsLimited && IsClipboardShortcut( virtualKeyCode, modifiers ) )
			return;

		if ( state ) steamHTMLSurface.KeyDown( BrowserId, virtualKeyCode, (int)modifiers, false );
		else steamHTMLSurface.KeyUp( BrowserId, virtualKeyCode, (int)modifiers );
	}

	/// <summary>
	/// Is this key combination a clipboard shortcut (paste, copy or cut)
	/// </summary>
	internal static bool IsClipboardShortcut( uint virtualKeyCode, KeyboardModifiers modifiers )
	{
		if ( modifiers.Contains( KeyboardModifiers.Ctrl ) )
		{
			if ( virtualKeyCode == 86 ) return true; // V
			if ( virtualKeyCode == 67 ) return true; // C
			if ( virtualKeyCode == 88 ) return true; // X
			if ( virtualKeyCode == 45 ) return true; // Insert (copy)
		}

		if ( modifiers.Contains( KeyboardModifiers.Shift ) )
		{
			if ( virtualKeyCode == 45 ) return true; // Insert (paste)
			if ( virtualKeyCode == 46 ) return true; // Delete (cut)
		}

		return false;
	}

	bool _keyFocus;

	/// <summary>
	/// Tell the html control if it has key focus currently, controls showing the I-beam cursor in text controls amongst other things
	/// </summary>
	public bool HasKeyFocus
	{
		get => _keyFocus;

		set
		{
			if ( BrowserId == 0 )
			{
				_initQueue += () => HasKeyFocus = value;
				return;
			}

			_keyFocus = value;
			steamHTMLSurface.SetKeyFocus( BrowserId, value );
		}
	}

	float _scaleFactor = 1.0f;

	/// <summary>
	/// DPI Scaling factor
	/// </summary>
	public float ScaleFactor
	{
		get => _scaleFactor;

		set
		{
			if ( BrowserId == 0 )
			{
				_initQueue += () => ScaleFactor = value;
				return;
			}

			_scaleFactor = value;
			steamHTMLSurface.SetDPIScalingFactor( BrowserId, value );
		}
	}

	bool _backgroudMode;

	/// <summary>
	/// Enable/disable low-resource background mode, where javascript and repaint timers are throttled, resources are
	/// more aggressively purged from memory, and audio/video elements are paused. When background mode is enabled,
	/// all HTML5 video and audio objects will execute ".pause()" and gain the property "._steam_background_paused = 1".
	/// When background mode is disabled, any video or audio objects with that property will resume with ".play()".
	/// </summary>
	public bool InBackgroundMode
	{
		get => _backgroudMode;
		set
		{
			if ( BrowserId == 0 )
			{
				_initQueue += () => InBackgroundMode = value;
				return;
			}

			_backgroudMode = value;
			steamHTMLSurface.SetBackgroundMode( BrowserId, value );
		}
	}
}
