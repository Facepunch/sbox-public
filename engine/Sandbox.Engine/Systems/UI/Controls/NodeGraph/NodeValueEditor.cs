namespace Sandbox.UI.NodeGraph;

/// <summary>
/// An inline editor inside a plug, placed by the plug in node units.
/// </summary>
public abstract class NodeValueEditor : Panel
{
	public PlugPanel Plug { get; }

	/// <summary>
	/// Off while the plug is connected. A disabled editor draws nothing and takes no input.
	/// </summary>
	public bool Enabled
	{
		get => _enabled;
		set
		{
			_enabled = value;
			Style.PointerEvents = value ? PointerEvents.All : PointerEvents.None;
		}
	}

	bool _enabled = true;

	/// <summary>
	/// The plug doesn't draw its label while this editor is enabled.
	/// </summary>
	public virtual bool HideLabel => true;

	/// <summary>
	/// Width the editor needs, in node units. The node grows its plug to fit.
	/// </summary>
	public virtual float PreferredWidth => 48f;

	protected NodeValueEditor( PlugPanel plug )
	{
		Plug = plug;
		AddClass( "node-value-editor" );
		Style.Position = PositionMode.Absolute;
		Style.PointerEvents = PointerEvents.All;
	}

	/// <summary>
	/// Place the editor at <paramref name="rect"/>, in the plug's node units.
	/// </summary>
	public void Place( Rect rect )
	{
		Style.Left = rect.Left;
		Style.Top = rect.Top;
		Style.Width = rect.Width;
		Style.Height = rect.Height;
	}

	public override void OnDraw( Painter painter )
	{
		if ( !Enabled ) return;

		var paint = new NodePaint( this, painter ) { HasMouseOver = HasHovered };
		Paint( ref paint, new Rect( 0, Box.Rect.Size / ScaleToScreen ) );
	}

	/// <summary>
	/// Draw the editor. <paramref name="rect"/> is its local rect, in node units.
	/// </summary>
	protected abstract void Paint( ref NodePaint paint, Rect rect );
}
