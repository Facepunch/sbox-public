/// <summary>
/// A non-physics logical link between two GameObjects.
/// Used by the Wire tool to group unconnected objects so the Duplicator
/// treats them as part of the same contraption.
/// </summary>
public sealed class ManualLink : Component
{
	[Property, Sync]
	public GameObject Body { get; set; }

	// The signal wire this link carries, if it was created by wiring two ports together.
	// Destroying the link (undo, the wire tool's unlink) disconnects the wire too.
	[Property, Sync, Hide]
	public Component SignalSource { get; set; }

	[Property, Sync, Hide]
	public string SignalOutputId { get; set; }

	[Property, Sync, Hide]
	public Component SignalTarget { get; set; }

	[Property, Sync, Hide]
	public string SignalInputId { get; set; }

	internal bool HasWire => SignalSource is not null && SignalTarget is not null;

	/// <summary>
	/// Wires whose output is the source component itself — reference and presence links.
	/// </summary>
	internal bool IsComponentWire => string.Equals( SignalOutputId, SignalSystem.ComponentOutputId, StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// Stamp the wire on both ends of the pair, so either side can resolve it from
	/// its own link children.
	/// </summary>
	internal void SetWire( Component source, string outputId, Component target, string inputId )
	{
		SetWireEnd( source, outputId, target, inputId );
		Body?.GetComponent<ManualLink>()?.SetWireEnd( source, outputId, target, inputId );
	}

	private void SetWireEnd( Component source, string outputId, Component target, string inputId )
	{
		SignalSource = source;
		SignalOutputId = outputId;
		SignalTarget = target;
		SignalInputId = inputId;
	}

	internal static GameObject[] CreatePair( GameObject first, GameObject second )
	{
		var firstLink = new GameObject( first, false, "link" );
		var secondLink = new GameObject( second, false, "link" );

		firstLink.AddComponent<ManualLink>().Body = secondLink;
		secondLink.AddComponent<ManualLink>().Body = firstLink;

		secondLink.NetworkSpawn();
		firstLink.NetworkSpawn();
		return [firstLink, secondLink];
	}

	protected override void OnDestroy()
	{
		SignalSystem.DisconnectWire( this );

		if ( Body.IsValid() )
			Body.Destroy();

		base.OnDestroy();
	}

	protected override void OnUpdate()
	{
		if ( !Body.IsValid() )
			DestroyGameObject();
	}
}

