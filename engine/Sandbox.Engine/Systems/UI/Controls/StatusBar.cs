using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A strip of normal status widgets on the left and permanent widgets on the right.
/// Temporary messages replace the left widgets without deleting them.
/// </summary>
[Library( "statusbar" )]
[StyleSheet.Inline( "statusbar", Styles )]
public class StatusBar : Panel
{
	const string Styles = """
		.statusbar { flex-direction: row; align-items: center; flex-shrink: 0; min-width: 0; overflow: hidden; pointer-events: all; }
		.statusbar > .status-left, .statusbar > .status-right { align-items: center; min-width: 0; overflow: hidden; }
		.statusbar > .status-left { flex-grow: 1; flex-shrink: 1; }
		.statusbar > .status-right { flex-shrink: 0; margin-left: auto; }
		.statusbar > .status-message { flex-grow: 1; flex-shrink: 1; min-width: 0; white-space: nowrap; text-overflow: ellipsis; overflow: hidden; }
		.status-left > *, .status-right > * { flex-shrink: 0; }
		.status-separator { flex-shrink: 0; }
		""";

	readonly Label _messageLabel;
	double? _expiresAt;

	/// <summary>
	/// Normal widgets, hidden while a message is displayed.
	/// </summary>
	public Panel Left { get; }

	/// <summary>
	/// Permanent widgets, visible even while a message is displayed.
	/// </summary>
	public Panel Right { get; }

	/// <summary>
	/// The current temporary message, or an empty string.
	/// </summary>
	public string Message => _messageLabel.Text;

	/// <summary>
	/// Called when the message changes or is cleared.
	/// </summary>
	public event Action<string> MessageChanged;

	public StatusBar()
	{
		AddClass( "statusbar" );
		Left = Add.Panel( "status-left" );
		_messageLabel = Add.Label( "", "status-message" );
		_messageLabel.Style.Display = DisplayMode.None;
		Right = Add.Panel( "status-right" );
	}

	/// <summary>
	/// Add and own a normal widget. Stretch distributes spare space within its slot.
	/// </summary>
	public T AddLeft<T>( T widget, int stretch = 0 ) where T : Panel => AddWidget( Left, widget, stretch );

	/// <summary>
	/// Add and own a permanent widget.
	/// </summary>
	public T AddRight<T>( T widget, int stretch = 0 ) where T : Panel => AddWidget( Right, widget, stretch );

	T AddWidget<T>( Panel slot, T widget, int stretch ) where T : Panel
	{
		ArgumentNullException.ThrowIfNull( widget );
		if ( stretch < 0 ) throw new ArgumentOutOfRangeException( nameof( stretch ) );
		if ( !widget.IsValid || widget.IsDeleting || widget is RootPanel || AncestorsAndSelf.Contains( widget ) || widget == Left || widget == Right )
			throw new ArgumentException( "The widget cannot be owned by this status bar.", nameof( widget ) );
		widget.Parent = slot;
		widget.Style.FlexGrow = stretch;
		widget.Style.FlexShrink = stretch > 0 ? 1 : 0;
		return widget;
	}

	/// <summary>
	/// Remove a direct slot widget without deleting it. The caller takes ownership.
	/// </summary>
	public bool RemoveWidget( Panel widget )
	{
		if ( widget is null || (widget.Parent != Left && widget.Parent != Right) ) return false;
		widget.Parent = null;
		return true;
	}

	/// <summary>
	/// Add a separator to the normal or permanent slot.
	/// </summary>
	public Panel AddSeparator( bool right = false ) => (right ? Right : Left).Add.Panel( "status-separator" );

	/// <summary>
	/// Replace the current message. Positive seconds expire automatically; zero or negative seconds
	/// keep the message until replaced or cleared. Empty text clears it.
	/// </summary>
	public void ShowMessage( string text, float seconds = 5 )
	{
		if ( !float.IsFinite( seconds ) ) throw new ArgumentOutOfRangeException( nameof( seconds ) );
		text ??= "";
		_expiresAt = text.Length > 0 && seconds > 0 ? TimeNow + seconds : null;
		if ( Message == text ) return;
		_messageLabel.Text = text;
		_messageLabel.Tooltip = text;
		var hasMessage = text.Length > 0;
		if ( hasMessage ) UISystem?.ReleaseFocusSubtree( Left );
		Left.Style.Display = hasMessage ? DisplayMode.None : DisplayMode.Flex;
		_messageLabel.Style.Display = hasMessage ? DisplayMode.Flex : DisplayMode.None;
		MessageChanged?.Invoke( text );
	}

	/// <summary>
	/// Clear the message and restore the normal widgets.
	/// </summary>
	public void ClearMessage() => ShowMessage( "" );

	internal void ExpireMessage( double now )
	{
		if ( _expiresAt is { } deadline && now >= deadline ) ClearMessage();
	}

	/// <inheritdoc/>
	public override void Tick()
	{
		base.Tick();
		ExpireMessage( TimeNow );
	}
}
