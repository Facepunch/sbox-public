namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Defines a shared transition from every other state to one target state.
/// <br/><br/>Its condition and transition settings are reused wherever the target can be reached. From each active state it competes with that state's other transitions in priority order, and the first valid transition wins.
/// <br/><br/><b>Use when:</b>&#160;Many states need the same route, such as death, stun, interruption, or entering a universal mode.
/// <br/><b>Important:</b>&#160;A global transition never transitions a state to itself. An explicit conduit from a source state to the same target replaces the global route for that source, allowing source-specific conditions or blend settings.
/// <br/><br/>One global transition node per target state is synchronized automatically with the state list. These nodes cannot be cloned or removed.
/// </summary>
[Title( "Global Transition" ), Group( "State Machine" ), Icon( "route" )]
[AvailableIn]
public class GlobalTransitionNode : TransitionNode
{
	/// <summary>
	/// The target state this global transition leads to.
	/// </summary>
	[Hide]
	public string StateName { get; set; } = "";

	public override bool CanClone => false;
	public override bool CanRemove => false;

	protected override DisplayInfo DefaultDisplayInfo
	{
		get
		{
			var info = TypeDisplayInfo;
			info.Name = $"Global → {StateName}";
			return info;
		}
	}
}
