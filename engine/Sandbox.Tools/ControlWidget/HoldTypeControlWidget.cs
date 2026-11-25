namespace Editor;

/// <summary>
/// Editor for hold type strings (e.g. <see cref="Sandbox.BaseCombatWeapon.HoldType"/>) - a combo pre-filled
/// with the <see cref="Sandbox.WeaponHolds"/> names, editable so games can use their own.
/// </summary>
[CustomEditor( typeof( string ), NamedEditor = "holdtype" )]
public class HoldTypeControlWidget : ControlWidget
{
	readonly ComboBox _combo;

	public override bool IsControlActive => _combo.IsValid() && _combo.LineEdit.IsValid() && _combo.LineEdit.IsFocused;

	public HoldTypeControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();

		_combo = new ComboBox( this );
		_combo.Editable = true;

		foreach ( var name in Sandbox.WeaponHolds.All )
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
