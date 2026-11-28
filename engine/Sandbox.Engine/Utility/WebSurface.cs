using Sandbox.Utility;
using Sandbox.SteamApi;

namespace Sandbox;


/// <summary>
/// Enables rendering and interacting with a webpage
/// </summary>
public sealed class WebSurface : IDisposable
{
	static readonly Dictionary<uint, WeakReference<WebSurface>> All = new();
	static bool htmlInitialized;

	Action _initQueue;
	bool _creating = true;
	bool _disposed;

	readonly Sandbox.Engine.GlobalContext _context;
	readonly System.Threading.CancellationToken _session;
	readonly int _steamSession = SteamClient.Session;

	bool IsCurrentSteamSession => SteamClient.IsValid && _steamSession == SteamClient.Session;
	uint _browserId;
	uint BrowserId => IsCurrentSteamSession && !_session.IsCancellationRequested ? _browserId : 0;

	public bool IsLimited { get; private set; }

	internal static void Shutdown()
	{
		lock ( All )
		{
			if ( !htmlInitialized ) return;

			All.Clear();
			SteamNative.HtmlSurface.Shutdown();
			htmlInitialized = false;
		}
	}

	internal static WebSurface GetBrowser( uint handle )
	{
		if ( !SteamClient.IsValid ) return null;

		lock ( All )
		{
			if ( All.TryGetValue( handle, out var v ) && v.TryGetTarget( out var target ) && target.BrowserId == handle )
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

		if ( isLimited )
		{
			_context = Sandbox.Engine.GlobalContext.Current;
			_session = _context.CancellationTokenSource?.Token ?? default;
		}

		_ = CreateAsync();
	}

	ulong StartCreatingBrowser()
	{
		lock ( All )
		{
			if ( !IsCurrentSteamSession || Application.IsUnitTest || Application.IsHeadless ) return 0;

			if ( !htmlInitialized && !SteamNative.HtmlSurface.Initialize() )
			{
				Log.Warning( "WebSurface unavailable - Steam HTML initialization failed" );
				return 0;
			}

			htmlInitialized = true;
			return SteamNative.HtmlSurface.CreateBrowser( "Mozilla/5.0 (Linux; U; X11; en-US; Facepunch Sandbox; ) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/85.0.4183.121 Safari/537.36" );
		}
	}

	async Task CreateAsync()
	{
		var browser = await SteamCalls.GetResultAsync( StartCreatingBrowser(), SteamNative.HtmlSurface.GetBrowserReadyResult );
		Action onCreated;
		lock ( All )
		{
			_creating = false;
			onCreated = _initQueue;
			_initQueue = null;
			if ( browser == 0 || !IsCurrentSteamSession ) return;

			if ( _disposed || _session.IsCancellationRequested )
			{
				SteamNative.HtmlSurface.RemoveBrowser( browser );
				return;
			}

			_browserId = browser;
			All[browser] = new WeakReference<WebSurface>( this );
		}

		onCreated?.Invoke();
	}

	void WhenCreated( Action action )
	{
		lock ( All )
		{
			if ( _disposed || !IsCurrentSteamSession || _session.IsCancellationRequested ) return;
			if ( _creating )
			{
				_initQueue += action;
				return;
			}

			if ( _browserId == 0 ) return;
		}

		action();
	}

	~WebSurface()
	{
		Dispose();
	}

	/// <summary>
	/// Closes the browser and stops delivering its page and texture events.
	/// </summary>
	public void Dispose()
	{
		GC.SuppressFinalize( this );

		lock ( All )
		{
			_disposed = true;
			_initQueue = null;
			OnTexture = null;
			OnPageLoaded = null;

			if ( _browserId != 0 && IsCurrentSteamSession )
			{
				All.Remove( _browserId );
				SteamNative.HtmlSurface.RemoveBrowser( _browserId );
			}
			_browserId = 0;
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
				WhenCreated( () => Url = value );
				return;
			}

			SteamNative.HtmlSurface.LoadURL( BrowserId, value ?? "" );
		}
	}

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
				WhenCreated( () => Size = value );
				return;
			}

			_size = value;
			SteamNative.HtmlSurface.SetSize( BrowserId, (uint)value.x, (uint)value.y );
		}
	}

	/// <summary>
	/// Invoked when the browser needs to be repainted
	/// </summary>
	internal unsafe void OnNeedsRepaint( IntPtr pixels, uint width, uint height )
	{
		ReadOnlySpan<byte> s = new ReadOnlySpan<byte>( (void*)pixels, (int)(width * height * 4) );

		var size = new Vector2( width, height );
		_size = size;

		if ( _context is null )
		{
			OnTexture?.Invoke( s, size );
			return;
		}

		if ( _session.IsCancellationRequested ) return;

		using var scope = new Sandbox.Engine.GlobalContext.GlobalContextScope( _context );
		OnTexture?.Invoke( s, size );
	}

	internal void OnChangedTitle( string title )
	{
		PageTitle = title;
	}

	/// <summary>
	/// A navigation has happened, allow or deny it
	/// </summary>
	internal bool AllowNavigation( string url )
	{
		try
		{
			if ( IsLimited )
			{
				if ( !Uri.TryCreate( url, UriKind.Absolute, out var uri ) ) return false;
				CheckUrlIsAllowed( uri );
			}
		}
		catch ( InvalidOperationException e )
		{
			Log.Warning( $"{e.Message}" );
			return false;
		}

		currentUrl = url;
		return true;
	}

	/// <summary>
	/// Invoked when the browser is navigating to a new url
	/// </summary>
	internal void OnURLChanged( string url )
	{
		currentUrl = url;
	}


	internal void OnFinishedRequest( string url, string title )
	{
		currentUrl = url;
		PageTitle = title;

		OnPageLoaded?.Invoke( url );
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
			WhenCreated( () => ExecuteJavascript( script ) );
			return;
		}

		SteamNative.HtmlSurface.ExecuteJavascript( BrowserId, script );
	}

	/// <summary>
	/// CSS cursor requested by the browser for the current pointer position.
	/// </summary>
	public string Cursor { get; internal set; }

	/// <summary>
	/// Tell the browser the mouse has moved
	/// </summary>
	public void TellMouseMove( Vector2 position )
	{
		SteamNative.HtmlSurface.MouseMove( BrowserId, (int)position.x, (int)position.y );
	}

	/// <summary>
	/// Tell the browser the mouse wheel has moved
	/// </summary>
	/// <param name="delta"></param>
	public void TellMouseWheel( int delta )
	{
		SteamNative.HtmlSurface.MouseWheel( BrowserId, delta );
	}

	/// <summary>
	/// Tell the browser a mouse button has been pressed
	/// </summary>
	public void TellMouseButton( MouseButtons button, bool state )
	{
		SteamNative.HtmlSurface.SetMouseButtonState( BrowserId, button, state );
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

		SteamNative.HtmlSurface.KeyChar( BrowserId, unicodeKey, modifiers );
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

		SteamNative.HtmlSurface.SetKeyState( BrowserId, virtualKeyCode, modifiers, state );
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
				WhenCreated( () => HasKeyFocus = value );
				return;
			}

			_keyFocus = value;
			SteamNative.HtmlSurface.SetKeyFocus( BrowserId, value );
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
				WhenCreated( () => ScaleFactor = value );
				return;
			}

			_scaleFactor = value;
			SteamNative.HtmlSurface.SetDPIScalingFactor( BrowserId, value );
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
				WhenCreated( () => InBackgroundMode = value );
				return;
			}

			_backgroudMode = value;
			SteamNative.HtmlSurface.SetBackgroundMode( BrowserId, value );
		}
	}
}
