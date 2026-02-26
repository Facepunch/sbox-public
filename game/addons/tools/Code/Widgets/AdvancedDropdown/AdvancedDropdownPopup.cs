namespace Editor;

/// <summary>
/// A popup wrapper around <see cref="AdvancedDropdownWidget"/>.
/// </summary>
public class AdvancedDropdownPopup : PopupWidget
{
	public AdvancedDropdownWidget Dropdown { get; }

	public Action<object> OnSelect
	{
		get => Dropdown.OnSelect;
		set => Dropdown.OnSelect = value;
	}

	public AdvancedDropdownPopup( Widget parent ) : this( parent, null )
	{
	}

	public AdvancedDropdownPopup( Widget parent, AdvancedDropdownWidget dropdown ) : base( parent )
	{
		Dropdown = dropdown ?? new AdvancedDropdownWidget( this );
		Dropdown.OnFinished = Destroy;

		Layout = Layout.Column();
		Layout.Add( Dropdown );

		DeleteOnClose = true;
	}
}
