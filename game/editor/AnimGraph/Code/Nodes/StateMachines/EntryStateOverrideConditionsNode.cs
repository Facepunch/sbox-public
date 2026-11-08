using Editor.NodeEditor;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Chooses which state a state machine enters when it becomes active.
/// <br/><br/>Each input corresponds to a state. Conditions are checked in state-list order and the first true condition wins. If none are true, the state machine uses its configured default state.
/// <br/><br/><b>Use when:</b>&#160;The correct starting state is already known, such as entering locomotion directly into grounded, airborne, armed, or unarmed behavior.
/// <br/><b>Important:</b>&#160;Entry overrides are evaluated only when the state machine initializes. They are not continuously checked and do not transition an active state machine; use a normal or global transition for that.
/// <br/><br/>State inputs are synchronized automatically with the state list. This node cannot be cloned or removed.
/// </summary>
[Title( "Entry State Conditions" ), Group( "State Machine" ), Icon( "login" )]
[AvailableIn]
public class EntryStateOverrideConditionsNode : Node
{
	[Hide]
	public List<string> States { get; set; } = new();

	[Hide]
	public List<NodeInput> Conditions { get; set; } = new();

	public override bool CanClone => false;
	public override bool CanRemove => false;
	public override Color PrimaryColor => Theme.Red;

	private readonly List<IPlugIn> _plugs = new();

	protected override IEnumerable<IPlugIn> DynamicInputs
	{
		get
		{
			while ( _plugs.Count > States.Count )
				_plugs.RemoveAt( _plugs.Count - 1 );

			while ( _plugs.Count < States.Count )
			{
				var i = _plugs.Count;
				var info = new AnimPlugInfo( $"State{i}", () => i < States.Count ? States[i] : $"State {i + 1}",
					_ => i < Conditions.Count ? Conditions[i] : default,
					( _, value ) => { if ( i < Conditions.Count ) Conditions[i] = value; } );

				_plugs.Add( new AnimPlugIn( this, info, typeof( BoolLink ) ) );
			}

			return _plugs;
		}
	}

	public NodeInput ConditionFor( string state )
	{
		var index = States.IndexOf( state );
		return index >= 0 && index < Conditions.Count ? Conditions[index] : default;
	}

	public void SyncStates( List<string> names )
	{
		for ( var i = States.Count - 1; i >= 0; i-- )
		{
			if ( names.Contains( States[i] ) )
				continue;

			States.RemoveAt( i );
			if ( i < Conditions.Count )
				Conditions.RemoveAt( i );
		}

		foreach ( var name in names.Where( n => !States.Contains( n ) ) )
		{
			States.Add( name );
			Conditions.Add( default );
		}

		while ( Conditions.Count < States.Count )
			Conditions.Add( default );
	}

	public void RenameState( string oldName, string newName )
	{
		var index = States.IndexOf( oldName );
		if ( index < 0 )
			return;

		States[index] = newName;
		Update();
	}
}
