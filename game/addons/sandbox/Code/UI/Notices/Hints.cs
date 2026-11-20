namespace Sandbox.UI;

public class Hints : GameObjectSystem<Hints>
{
	[Title( "Show UI Hints" )]
	[ConVar( "sandbox.showhints", ConVarFlags.Saved, Help = "Whether to display popup hints." )]
	public static bool ShowHints { get; set; } = true;

	record class Hint( string Name, string Icon, RealTimeUntil Delay )
	{
		public bool Ready => Delay < 0;
	}

	List<Hint> _queue = new();

	public Hints( Scene scene ) : base( scene )
	{
		Queue( "openspawnmenu", "info", 10 );
		Queue( "openinspectmenu", "info", 40 );
		Queue( "openpausemenu", "info", 70 );

		Listen( Stage.StartUpdate, 0, Tick, "UpdateHints" );
	}

	public void Queue( string hintName, string hintIcon, float delay )
	{
		var hint = new Hint( hintName, hintIcon, delay );
		_queue.Add( hint );
	}

	RealTimeSince timeSinceLast = 0;

	void Tick()
	{
		if ( timeSinceLast < 3 )
			return;

		if ( !ShowHints )
			return;

		var next = _queue.Find( x => x.Ready );
		if ( next is null ) return;

		_queue.Remove( next );
		timeSinceLast = 0;

		var phrase = Game.Language.GetPhrase( $"hint.{next.Name}", InputTokens() );

		Notices.AddNotice( next.Icon, phrase, 5 );
	}

	// Hint phrases write the key as {input:ActionName}. The phrase system treats that as a variable called "input:ActionName", so
	// it is filled in with the key bound to the action. Every action the hint phrases use needs to be listed here.
	static readonly string[] InputActions = { "SpawnMenu", "InspectMenu" };

	static Dictionary<string, object> InputTokens()
	{
		var tokens = new Dictionary<string, object>();
		foreach ( var action in InputActions )
			tokens[$"input:{action}"] = Input.GetButtonOrigin( action ) ?? action;

		return tokens;
	}

	public void Cancel( string hintName )
	{
		_queue.RemoveAll( x => x.Name.Equals( hintName, StringComparison.OrdinalIgnoreCase ) );
	}
}
