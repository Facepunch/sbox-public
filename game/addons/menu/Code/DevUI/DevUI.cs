namespace Sandbox.UI.Dev;

public class DevLayer : RootPanel
{
	public static DevLayer Instance;

	ExceptionNotification ExceptionNotification;

	public DevLayer()
	{
		Instance = this;

		AddChild<DeveloperMode>();
		AddChild<ConsoleOverlay>();

		ExceptionNotification = AddChild<ExceptionNotification>();

		MenuUtility.AddLogger( OnConsoleMessage );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		MenuUtility.RemoveLogger( OnConsoleMessage );
	}

	[MenuConVar( "devui_scale" )]
	public static float DevUI_Scale { get; set; } = 1.0f;


	protected override void UpdateScale( Rect screenSize )
	{
		Scale = Screen.DesktopScale * DevUI_Scale;
	}

	private void OnConsoleMessage( LogEvent entry )
	{
		if ( !ThreadSafe.IsMainThread )
			return;

		if ( entry.Level == LogLevel.Error )
		{
			ExceptionNotification.OnException( entry );
		}
	}
}
