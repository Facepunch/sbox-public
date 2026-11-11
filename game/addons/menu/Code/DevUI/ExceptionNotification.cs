using Sandbox.UI.Construct;

namespace Sandbox.UI.Dev;

/// <summary>
/// The latest error in the top right - where it came from over what it said - washed amber each time it happens again,
/// gone 10 seconds after the last
/// </summary>
public class ExceptionNotification : Panel
{
	Label source;
	Label message;
	RealTimeSince TimeSinceLastError;

	public ExceptionNotification()
	{
		Build();
		SetClass( "hidden", true );
		TimeSinceLastError = 100;
	}

	/// <summary>
	/// Its insides - made again on a hotload, which doesn't run the constructor again
	/// </summary>
	void Build()
	{
		var from = source?.Text ?? "Engine";
		var text = message?.Text ?? "Something went wrong! This is an exception notice!";

		DeleteChildren( true );
		Add.Icon( "error" );

		var column = Add.Panel( "column" );
		source = column.Add.Label( from, "source" );
		message = column.Add.Label( text, "message" );
	}

	public override void OnHotloaded()
	{
		base.OnHotloaded();
		Build();
	}

	public override void Tick()
	{
		base.Tick();

		SetClass( "hidden", TimeSinceLastError > 10 );
		SetClass( "fresh", TimeSinceLastError < 0.05f );
		SetClass( "rumble", TimeSinceLastError < 0.3f );
	}

	internal void OnException( LogEvent entry )
	{
		source.Text = SourceName( entry.SourceAssembly );
		message.Text = entry.Message?.Split( '\n', System.StringSplitOptions.RemoveEmptyEntries ).FirstOrDefault()?.Trim() ?? "null";
		TimeSinceLastError = 0;
	}

	/// <summary>
	/// What a package assembly ("package.org.ident", or its ".editor") belongs to - the package's name if it's cached,
	/// its ident if not. The engine if it didn't come from one.
	/// </summary>
	static string SourceName( string assembly )
	{
		if ( string.IsNullOrWhiteSpace( assembly ) || !assembly.StartsWith( "package." ) )
			return "Engine";

		var ident = assembly["package.".Length..];
		if ( ident.EndsWith( ".editor" ) )
			ident = ident[..^".editor".Length];

		// This menu - never a package that's cached
		if ( ident == "local.menu" )
			return "Menu";

		// A local one's "found" even when it isn't, so the package can be null
		return Package.TryGetCached( ident, out var package ) && !string.IsNullOrWhiteSpace( package?.Title ) ? package.Title : ident;
	}
}
