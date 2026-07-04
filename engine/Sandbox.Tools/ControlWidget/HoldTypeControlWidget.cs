namespace Editor;

/// <summary>
/// Editor for hold type strings (e.g. <see cref="Sandbox.BaseWeapon.HoldType"/>) - a combo pre-filled
/// with the hold types the citizen animgraph understands, editable so graphs with their own options
/// can use anything.
/// </summary>
[CustomEditor( typeof( string ), NamedEditor = "holdtype" )]
public class HoldTypeControlWidget : ControlWidget
{
	// The citizen animgraph's holdtype options.
	static readonly string[] BuiltIn =
	[
		"none", "pistol", "rifle", "shotgun", "holditem", "melee_punch", "melee_weapons", "rpg", "physgun"
	];

	readonly ComboBox _combo;

	public override bool IsControlActive => _combo.IsValid() && _combo.LineEdit.IsValid() && _combo.LineEdit.IsFocused;

	public HoldTypeControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();

		_combo = new ComboBox( this );
		_combo.Editable = true;

		foreach ( var name in BuiltIn )
		{
			_combo.AddItem( name );
		}

		_combo.CurrentText = property.As.String ?? "";
		_combo.TextChanged += () => SerializedProperty.SetValue( _combo.CurrentText );

		Layout.Add( _combo );
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		if ( !_combo.IsValid() )
			return;

		if ( _combo.LineEdit.IsValid() && _combo.LineEdit.IsFocused )
			return;

		_combo.CurrentText = SerializedProperty.As.String ?? "";
	}

	protected override void OnPaint()
	{
		// the combo paints itself
	}
}
