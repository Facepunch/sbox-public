namespace Sandbox.UI;

/// <summary>
/// Base <see cref="Panel"/> event.<br/>
/// See <see cref="Panel.CreateEvent(PanelEvent)"/>.
/// </summary>
[Expose]
public class PanelEvent
{
	public string Name { get; init; }
	public object Value { get; set; }
	public float Time { get; set; }
	public string Button { get; set; }

	/// <summary>
	/// The panel on which the event is being called. For example, if you have a button with a label.. when the
	/// button gets clicked the actual click event might come from the label. When the event is called on the
	/// label, This will be the label. When the event propagates up to the button This will be the button - but
	/// Target will be the label. This is mainly of use with Razor callbacks, where you want to get the actual
	/// panel that created the event.
	/// </summary>
	public Panel This { get; set; }
	public Panel Target { get; set; }

	internal bool Propagate = true;

	public PanelEvent( string eventName, Panel active = null )
	{
		Name = eventName;
		Target = active;
	}

	public bool Is( string name )
	{
		return string.Equals( name, Name, System.StringComparison.OrdinalIgnoreCase );
	}

	public void StopPropagation()
	{
		Propagate = false;
	}
}
