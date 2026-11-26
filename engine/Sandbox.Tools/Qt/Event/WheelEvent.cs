namespace Editor;

/// <summary>
/// Information about a mouse wheel scroll event of a <see cref="Widget"/>.
/// </summary>
public ref struct WheelEvent
{
	QWheelEvent ptr;

	internal WheelEvent( QWheelEvent ptr )
	{
		this.ptr = ptr;
		KeyboardModifiers = QtHelpers.Translate( ptr.modifiers() );
	}

	public readonly float Delta
	{
		get
		{
			// Qt swaps X / Y when holding alt, let's reverse that
			// so scroll delta is always Y axis.

			return HasAlt ? ptr.angleDelta().x : ptr.angleDelta().y;
		}
	}

	/// <summary>
	/// Both axes in eighths of a degree (120 a notch), as Qt reports them: with Alt held it swaps them.
	/// </summary>
	internal readonly Vector2 AngleDelta => new( ptr.angleDelta().x, ptr.angleDelta().y );

	public readonly Vector2 Position => (Vector2)ptr.position();
	public readonly Vector2 GlobalPosition => (Vector2)ptr.globalPosition();

	/// <inheritdoc cref="MouseEvent.Accepted"/>
	public bool Accepted
	{
		readonly get => ptr.isAccepted();
		set => ptr.setAccepted( value );
	}

	public readonly void Accept() => ptr.accept();
	public readonly void Ignore() => ptr.ignore();

	/// <inheritdoc cref="MouseEvent.KeyboardModifiers"/>
	public KeyboardModifiers KeyboardModifiers { get; set; }

	/// <inheritdoc cref="MouseEvent.HasShift"/>
	public readonly bool HasShift => KeyboardModifiers.Contains( KeyboardModifiers.Shift );

	/// <inheritdoc cref="MouseEvent.HasCtrl"/>
	public readonly bool HasCtrl => KeyboardModifiers.Contains( KeyboardModifiers.Ctrl );

	/// <inheritdoc cref="MouseEvent.HasAlt"/>
	public readonly bool HasAlt => KeyboardModifiers.Contains( KeyboardModifiers.Alt );
}

