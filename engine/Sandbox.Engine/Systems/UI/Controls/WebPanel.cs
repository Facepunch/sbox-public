using Microsoft.AspNetCore.Components;
using System.Web;

namespace Sandbox.UI;

/// <summary>
/// A panel that displays an interactive web page.
/// </summary>
public class WebPanel : Panel
{
	/// <summary>
	/// Access to the HTML surface to change URL, etc.
	/// </summary>
	public WebSurface Surface { get; private set; }

	[Parameter]
	public string Url
	{
		get => Surface.Url;
		set
		{
			Surface.Url = AddAdditionalQueryProperties( value );
		}
	}


	public WebPanel()
	{
		AcceptsFocus = true;

		Surface = Game.CreateWebSurface();
		Surface.Size = Box.Rect.Size;
		Surface.OnTexture = BrowserDataChanged;
	}

	Texture sufaceTexture;


	/// <summary>
	/// The texture has changed
	/// </summary>
	private void BrowserDataChanged( ReadOnlySpan<byte> span, Vector2 size )
	{
		//
		// Create or Recreate the texture if it changed
		//
		if ( sufaceTexture == null || sufaceTexture.Size != size )
		{
			sufaceTexture?.Dispose();
			sufaceTexture = null;

			sufaceTexture = Texture.Create( (int)size.x, (int)size.y, ImageFormat.BGRA8888 )
										.WithName( "WebPanel" )
										.Finish();

			sufaceTexture.Flags |= TextureFlags.PremultipliedAlpha;

			Style.SetBackgroundImage( sufaceTexture );
		}

		//
		// Update with thw new data
		//
		sufaceTexture.Update( span, 0, 0, (int)size.x, (int)size.y );
	}

	protected override void OnFocus( PanelEvent e ) => Surface.HasKeyFocus = true;
	protected override void OnBlur( PanelEvent e ) => Surface.HasKeyFocus = false;

	public override void OnMouseWheel( Vector2 value )
	{
		Surface.TellMouseWheel( (int)value.y * -40 );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		Surface.TellMouseButton( e.MouseButton, true );
		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		Surface.TellMouseButton( e.MouseButton, false );
		e.StopPropagation();
	}

	public override void OnKeyTyped( char k ) => Surface.TellChar( k, KeyboardModifiers.None );

	public override void OnButtonEvent( ButtonEvent e )
	{
		Surface.TellKey( (uint)e.VirtualKey, e.KeyboardModifiers, e.Pressed );
		//e.StopPropagation();
	}

	public override void OnLayout( ref Rect layoutRect )
	{
		Surface.Size = Box.Rect.Size;
		Surface.ScaleFactor = ScaleToScreen;
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		Surface.TellMouseMove( e.LocalPosition );
		Style.Cursor = Surface.Cursor;
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		Surface?.Dispose();
		Surface = null;
	}

	private bool WouldLikeAuthHeader( string url )
	{
		if ( url is null ) return false;

		if ( url.StartsWith( "https://sbox.game/" ) )
			return true;

		if ( url.StartsWith( "http://localhost:5000/" ) )
			return true;

		return false;
	}

	private string AddAdditionalQueryProperties( string url )
	{
		if ( !Game.IsMenu ) return url;
		if ( !WouldLikeAuthHeader( url ) ) return url;

		var sessionCookie = AccountInformation.Session;
		var steamid = AccountInformation.SteamId;

		var uri = new Uri( url );
		var query = HttpUtility.ParseQueryString( uri.Query );

		// Add or update the sessionCookie and steamid parameters
		query["sessioncookie"] = sessionCookie;
		query["steamid"] = steamid.ToString();

		// Rebuild the URL with the new query string
		var uriBuilder = new UriBuilder( uri )
		{
			Query = query.ToString()
		};

		return uriBuilder.ToString();
	}

}
