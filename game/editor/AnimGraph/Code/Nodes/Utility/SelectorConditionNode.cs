using Editor.NodeEditor;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Provides the auto-managed condition inputs for a Selector's internal condition graph.
/// <br/><br/><b>Use when:</b>&#160;Wiring the prioritized boolean expressions for each selector option.
/// <br/><b>Important:</b>&#160;Inputs and labels are synchronized from the owning Selector, and this node cannot be cloned or removed.
/// </summary>
[Title( "Conditions" ), Group( "Conditions" ), Icon( "rule" )]
[AvailableIn]
public class SelectorConditionNode : Node
{
	[Hide]
	public List<string> Labels { get; set; } = new();

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
			while ( _plugs.Count > Labels.Count )
				_plugs.RemoveAt( _plugs.Count - 1 );

			while ( _plugs.Count < Labels.Count )
			{
				var i = _plugs.Count;
				var info = new AnimPlugInfo( $"Option{i}",
					() => i < Labels.Count && !string.IsNullOrWhiteSpace( Labels[i] ) ? Labels[i] : "Option",
					_ => i < Conditions.Count ? Conditions[i] : default,
					( _, value ) => { if ( i < Conditions.Count ) Conditions[i] = value; } );

				_plugs.Add( new AnimPlugIn( this, info, typeof( BoolLink ) ) );
			}

			return _plugs;
		}
	}

	public NodeInput ConditionFor( int index )
		=> index >= 0 && index < Conditions.Count ? Conditions[index] : default;

	public void SyncOptions( List<string> labels )
	{
		while ( Conditions.Count > labels.Count )
			Conditions.RemoveAt( Conditions.Count - 1 );

		while ( Conditions.Count < labels.Count )
			Conditions.Add( default );

		Labels.Clear();
		Labels.AddRange( labels );
	}
}
