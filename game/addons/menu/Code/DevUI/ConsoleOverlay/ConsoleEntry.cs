using Sandbox.UI.Construct;

namespace Sandbox.UI.Dev;

public class ConsoleEntry : Panel
{
	public Label Time;
	public Label Repeats;
	public Label Message;
	public LogEvent Event;

	public bool AutoDelete;
	public RealTimeUntil TimeUntilDelete;

	public ConsoleEntry()
	{
		Time = Add.Label( null, "time" );
		Time.Selectable = false;

		Repeats = Add.Label( null, "repeats" );
		Repeats.Selectable = false;

		Message = Add.Label( null, "message" );
	}

	public override void Tick()
	{
		base.Tick();

		if ( AutoDelete && TimeUntilDelete <= 0 )
		{
			Delete();
		}
	}

	internal void SetLogEvent( LogEvent e )
	{
		Event = e;

		Time.Text = Event.Time.ToString( "hh:mm:ss" );
		Repeats.Text = $"[{Event.Repeats + 1}]";
		Repeats.Style.Display = Event.Repeats > 0 ? DisplayMode.Flex : DisplayMode.None;
		Message.Text = Event.Message;
		AddClass( e.Level.ToString() );

		if ( e.Logger != null )
		{
			AddClass( e.Logger );
		}
	}

	internal void DeleteIn( float seconds )
	{
		AutoDelete = true;
		TimeUntilDelete = seconds;
	}
}
