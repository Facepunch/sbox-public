using Sandbox.Html;

namespace Sandbox.UI.Navigation;

/// <summary>
/// A panel that acts like a website. A single page is always visible
/// but it will cache other views that you visit, and allow forward/backward navigation.
/// </summary>
[Library( "navigator" )]
public class NavigationHost : Panel
{
	/// <summary>
	/// The currently visible panel
	/// </summary>
	public Panel CurrentPanel => Current?.Panel;

	/// <summary>
	/// The Url we're currently viewing
	/// </summary>
	public string CurrentUrl => Current?.Url;

	/// <summary>
	/// The query part of the url
	/// </summary>
	public string CurrentQuery;

	/// <summary>
	/// The Url we should go to when one isn't set
	/// </summary>
	public string DefaultUrl { get; set; }

	/// <summary>
	/// The panel in which we should create our pages
	/// </summary>
	public Panel NavigatorCanvas { get; set; }

	protected class HistoryItem
	{
		public Panel Panel;
		public string Url;
	}

	/// <summary>
	/// Called after initialization
	/// </summary>
	protected override void OnParametersSet()
	{
		if ( DefaultUrl != null && CurrentUrl == null )
		{
			Navigate( DefaultUrl );
		}
	}

	/// <summary>
	/// This sucks this sucks this sucks
	/// </summary>
	public override void OnTemplateSlot( INode element, string slotName, Panel panel )
	{
		if ( slotName == "navigator-canvas" )
		{
			NavigatorCanvas = panel;

			foreach ( var p in Cache )
			{
				p.Panel.Parent = NavigatorCanvas;
			}

			return;
		}

		base.OnTemplateSlot( element, slotName, panel );
	}

	protected List<HistoryItem> Cache = new();

	HistoryItem Current;
	Stack<HistoryItem> Back = new();
	Stack<HistoryItem> Forward = new();

	Dictionary<string, TypeDescription> destinations = new Dictionary<string, TypeDescription>();

	/// <summary>
	/// Instead of finding pages by attributes, we can fill them in manually here
	/// </summary>
	public void AddDestination( string url, Type type )
	{
		var td = TypeLibrary.GetType( type );
		Assert.NotNull( td );
		destinations[url] = td;
	}

	/// <summary>
	/// Navigate to the passed url
	/// </summary>
	public Panel Navigate( string url, bool redirectToDefault = true )
	{
		string query = "";
		string originalUrl = url;
		bool foundPartial = false;

		if ( url?.Contains( '?' ) ?? false )
		{
			var qi = url.IndexOf( '?' );
			query = url[(qi + 1)..];
			url = url[..qi];
		}

		//
		// Find a NavigatorPanel that we're a child of
		//
		var parent = Ancestors.OfType<NavigationHost>().FirstOrDefault();

		//
		// Make url absolute by adding it to parent url
		//
		if ( url?.StartsWith( "~/" ) ?? false && parent.IsValid() )
		{
			url = $"{parent.CurrentUrl}/{url[2..]}";
		}

		if ( url == CurrentUrl )
		{
			ApplyQuery( query );
			return Current?.Panel;
		}

		NavigatorCanvas ??= this;

		var previousUrl = CurrentUrl;

		var target = FindTarget( url, parent?.CurrentUrl );

		if ( target.panelType == null )
		{
			if ( DefaultUrl != null && redirectToDefault )
			{
				Navigate( DefaultUrl, false );
				return Current?.Panel;
			}

			NotFound( url );
			return Current?.Panel;
		}

		var parts = target.url.Split( '/', StringSplitOptions.RemoveEmptyEntries );

		//
		// If the URl contains a *, it's a partial url. This expects the matching
		// page to have a NavigatorPanel in it - which will forfil the rest of the url
		//
		if ( target.url.Contains( '*' ) )
		{
			foundPartial = true;

			// the full url might be something like
			// game/blahblah/front
			// but the url of this navigator might be
			// game/{ident}/*
			// so change the url to 
			// game/blahblah
			// stripping off extra parts

			var partCount = parts.Length;
			var currentParts = url.Split( "/" ).Take( partCount );
			url = string.Join( "/", currentParts );
		}

		Forward.Clear();

		if ( Current != null )
		{
			Back.Push( Current );
			Current.Panel.AddClass( "hidden" );

			if ( Current.Panel is INavigatorPage _nav )
			{
				_nav.OnNavigationClose();
			}

			Current = null;
		}

		var cached = Cache.FirstOrDefault( x => x.Url == url );
		if ( cached != null )
		{
			cached.Panel.RemoveClass( "hidden" );
			Current = cached;
			Current.Panel.Parent = NavigatorCanvas;
			RunNavigatedEvent();
		}
		else
		{
			var panel = target.panelType.Create<Panel>();
			if ( !panel.IsValid() )
			{
				Log.Warning( $"Found a Route attribute - but we couldn't create the panel ({target.panelType})" );
				return Current?.Panel;
			}
			panel.AddClass( "navigator-body" );

			Current = new HistoryItem { Panel = panel, Url = url };
			Current.Panel.Parent = NavigatorCanvas;

			foreach ( var (key, value) in ExtractProperties( parts, url ) )
			{
				panel.SetProperty( key, value );
			}

			Cache.Add( Current );
			StateHasChanged();
			RunNavigatedEvent();
		}

		if ( Current == null ) return null;

		//
		// If we're a partial url, find the child NavigatorPanel and
		// send it the rest of the url
		//
		if ( foundPartial )
		{
			if ( Current.Panel is not NavigationHost childNavigator )
				childNavigator = Current.Panel.Descendants.OfType<NavigationHost>().FirstOrDefault();

			//Log.Info( $"Telling childNavigator [{Current.Panel}] => [{childNavigator}] to go to {originalUrl}" );

			if ( childNavigator.IsValid() )
			{
				childNavigator.Navigate( originalUrl );
			}
		}

		if ( Current.Panel is INavigatorPage nav )
		{
			nav.OnNavigationOpen();
		}

		ApplyQuery( query );
		return Current?.Panel;
	}

	/// <summary>
	/// Find a panel that can be created for this url
	/// </summary>
	private (string url, TypeDescription panelType) FindTarget( string url, string currentUrl )
	{
		foreach ( var destination in destinations )
		{
			if ( !DoesUrlMatch( url, destination.Key ) )
				continue;

			return (destination.Key, destination.Value);
		}

		var attr = RouteAttribute.FindValidTarget( url, currentUrl );
		if ( attr == null ) return default;

		return (attr.Value.Attribute.Url, attr.Value.Type);
	}

	static bool DoesUrlMatch( string url, string target )
	{
		if ( string.IsNullOrEmpty( url ) ) return false;

		if ( url.Contains( '?' ) )
		{
			url = url[..url.IndexOf( '?' )];
		}

		var a = url.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var parts = target.Split( '/', StringSplitOptions.RemoveEmptyEntries );

		for ( int i = 0; i < parts.Length || i < a.Length; i++ )
		{
			var left = i < a.Length ? a[i] : null;
			var right = i < parts.Length ? parts[i] : null;

			if ( right == "*" )
				return true;

			if ( !TestPart( left, right ) )
				return false;
		}

		return true;
	}

	static bool TestPart( string part, string ours )
	{
		// this is a variable
		if ( ours != null && ours.StartsWith( '{' ) && ours.EndsWith( '}' ) )
			return true;

		return part == ours;
	}

	public IEnumerable<(string key, string value)> ExtractProperties( string[] parts, string url )
	{
		var a = url.Split( '/', StringSplitOptions.RemoveEmptyEntries );

		for ( int i = 0; i < parts.Length; i++ )
		{
			if ( !parts[i].StartsWith( '{' ) ) continue;
			if ( !parts[i].EndsWith( '}' ) ) continue;

			var key = parts[i][1..^1].Trim( '?' );

			if ( i < a.Length )
			{
				yield return (key, a[i]);
			}
			else
			{
				yield return (key, null);
			}
		}
	}

	void ApplyQuery( string query )
	{
		if ( string.IsNullOrWhiteSpace( query ) )
			return;

		var parts = System.Web.HttpUtility.ParseQueryString( query );
		foreach ( var key in parts.AllKeys )
		{
			Current.Panel.SetProperty( key, parts.Get( key ) );
		}
	}

	protected virtual void NotFound( string url )
	{
		if ( url == null ) return;
		Log.Warning( $"Url Not Found: {url}" );
	}

	public bool CurrentUrlMatches( string url )
	{
		if ( url != null && url.StartsWith( "~" ) )
			return CurrentUrl?.EndsWith( url[1..] ) ?? false;

		if ( CurrentUrl == null )
			return url == null;

		return CurrentUrl.WildcardMatch( url );
	}

	public override void SetProperty( string name, string value )
	{
		base.SetProperty( name, value );

		if ( name == "default" )
			DefaultUrl = value;
	}

	protected override void OnBack( PanelEvent e )
	{
		if ( GoBack() )
		{
			e.StopPropagation();
		}
	}

	protected override void OnForward( PanelEvent e )
	{
		if ( GoForward() )
		{
			e.StopPropagation();
		}
	}

	/// <summary>
	/// Keep pressing the back button until our url doesn't match the passed wildcard string
	/// </summary>
	public virtual bool GoBackUntilNot( string wildcard )
	{
		if ( GoBack() )
		{
			if ( !Current.Url.WildcardMatch( wildcard ) )
				return true;

			return GoBackUntilNot( wildcard );
		}

		return false;
	}

	/// <summary>
	/// To back to the previous page. Return true on success.
	/// </summary>
	public virtual bool GoBack()
	{
		if ( !Back.TryPop( out var result ) )
		{
			PlaySound( "ui.navigate.deny" );
			return false;
		}

		if ( !Cache.Contains( result ) || !result.Panel.IsValid() )
		{
			return GoBack();
		}

		PlaySound( "ui.navigate.back" );

		if ( Current != null )
			Forward.Push( Current );

		Switch( result );
		return true;
	}

	/// <summary>
	/// Go forward, return true on success
	/// </summary>
	public virtual bool GoForward()
	{
		if ( !Forward.TryPop( out var result ) )
		{
			PlaySound( "ui.navigate.deny" );
			return false;
		}

		if ( !Cache.Contains( result ) || !result.Panel.IsValid() )
		{
			return GoForward();
		}

		PlaySound( "ui.navigate.forward" );

		if ( Current != null )
			Back.Push( Current );

		Switch( result );
		return true;
	}

	void Switch( HistoryItem item )
	{
		if ( Current == item ) return;

		if ( Current?.Panel is INavigatorPage fromNav )
		{
			fromNav.OnNavigationClose();
		}

		Current?.Panel.AddClass( "hidden" );
		Current = null;

		Current = item;
		Current?.Panel.RemoveClass( "hidden" );

		if ( Current?.Panel is INavigatorPage toNav )
		{
			toNav.OnNavigationClose();
		}

		RunNavigatedEvent();
	}

	void RunNavigatedEvent()
	{
		foreach ( var t in Descendants.OfType<INavigationEvent>() )
		{
			t.OnNavigated( CurrentUrl );
		}
	}
}

/// <summary>
/// Broadcast to all ancestors when the url changes
/// </summary>
public interface INavigationEvent
{
	void OnNavigated( string url );
}

/// <summary>
/// When applied to a page of a navigator, this will receive
/// callbacks when the page is made visible and closed
/// </summary>
public interface INavigatorPage
{
	public virtual void OnNavigationOpen() { }
	public virtual void OnNavigationClose() { }
}

public static class NavigationExtensions
{
	/// <summary>
	/// Find the closest navigatorPanel ancestor and navigate to the given url
	/// </summary>
	public static Panel Navigate( this Panel panel, string url )
	{
		return panel.AncestorsAndSelf.OfType<NavigationHost>().FirstOrDefault()?.Navigate( url ) ?? null;
	}
}
