namespace Sandbox;

/// <summary>
/// A button for a <see cref="UISheet"/> that is made while the game runs
/// </summary>
public sealed class UIActionProperty : SerializedProperty.Proxy
{
	readonly SerializedProperty _target;
	readonly Action _action;
	readonly int _order;

	protected override SerializedProperty ProxyTarget => _target;

	/// <summary>
	/// The button can be pressed while this says true. Always, when not set.
	/// </summary>
	public Func<bool> Enabled { get; init; }

	public override bool IsMethod => true;
	public override int Order => _order;

	/// <param name="title">What the button says, can be a localization token.</param>
	/// <param name="order">Where it goes among the other properties of the sheet.</param>
	public UIActionProperty( string title, string icon, Action action, int order, bool separator = false, bool sameLine = false )
	{
		var attributes = new List<Attribute> { new ButtonAttribute( title, icon ) };
		if ( separator ) attributes.Add( new SeparatorAttribute() );
		if ( sameLine ) attributes.Add( new SameLineAttribute() );

		_target = Game.TypeLibrary.CreateProperty<bool>( title, () => false, _ => { }, attributes.ToArray() );
		_action = action;
		_order = order;
	}

	public override void Invoke() => _action?.Invoke();
}
