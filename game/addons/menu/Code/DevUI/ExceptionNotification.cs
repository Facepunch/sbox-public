using Sandbox.UI.Construct;

namespace Sandbox.UI.Dev;

public class ExceptionNotification : Panel
{
	Label message;
	RealTimeSince TimeSinceLastError;

	public ExceptionNotification()
	{
		Add.Icon( "😥" );

		var column = new Panel( this, "column" );
		column.AddChild( new Label() { Text = "Code Error" } );

		message = column.Add.Label( "Something went wrong! This is an exception notice!", "message" );
		SetClass( "hidden", true );
		TimeSinceLastError = 100;
	}

	public override void Tick()
	{
		base.Tick();

		SetClass( "hidden", TimeSinceLastError > 8 );
		SetClass( "fresh", TimeSinceLastError < 0.2f );
	}

	internal void OnException( LogEvent entry )
	{
		message.Text = entry.Message?.Split( '\n', System.StringSplitOptions.RemoveEmptyEntries ).FirstOrDefault()?.Trim() ?? "null";
		TimeSinceLastError = 0;
	}
}
