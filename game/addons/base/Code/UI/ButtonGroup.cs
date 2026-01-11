namespace Sandbox.UI;

/// <summary>
/// A group of side-by-side buttons one of which can be selected.
/// </summary>
[Library( "ButtonGroup" )]
public class ButtonGroup : Panel
{
	// TODO - allow multi select
	// TODO - allow toggle off

	private object _value;

	/// <summary>
	/// Called when the value has been changed.
	/// </summary>
	[Parameter] public System.Action<object> ValueChanged { get; set; }

	/// <summary>
	/// The selected option value.
	/// </summary>
	[Parameter]
	public object Value
	{
		get => _value;
		set
		{
			if ( _value == value )
				return;

			_value = value;

			ValueChanged?.Invoke( _value );
			CreateEvent( "onchange" );
			CreateValueEvent( "value", _value );
			SetSelectedButton();
		}
	}

	/// <summary>
	/// Options to show in this button group.
	/// </summary>
	[Parameter]
	public List<Option> Options { get; set; }

	/// <summary>
	/// CSS Class(es) to add to child buttons.
	/// </summary>
	[Parameter]
	public string ButtonClass { get; set; } = "";


	public ButtonGroup()
	{

	}


	/// <summary>
	/// Adds a button to this group.
	/// </summary>
	/// <param name="value">The button's label.</param>
	/// <param name="action">Called <c>onclick</c>.</param>
	public Button AddButton( string value, Action action )
	{
		var btn = AddChild( new Button( value, action ) );
		btn.AddClass( ButtonClass );
		return btn;
	}

	/// <summary>
	/// Adds a button to this button group with <c>startactive</c> and <c>stopactive</c> callbacks
	/// </summary>
	/// <param name="value">The button's label</param>
	/// <param name="action">Called on <c>startactive</c> with parameter of <see langword="true"/> and <c>stopactive</c> with parameter of <see langword="false"/>.</param>
	public Button AddButtonActive( string value, Action<bool> action )
	{
		var btn = AddChild( new Button( value ) );
		btn.AddClass( ButtonClass );

		btn.AddEventListener( "startactive", () => action( true ) );
		btn.AddEventListener( "stopactive", () => action( false ) );

		return btn;
	}

	protected override void OnChildAdded( Panel child )
	{
		base.OnChildAdded( child );

		child.AddEventListener( "onclick", () => SelectedButton = child );

		if ( child.HasClass( "active" ) )
			SelectedButton = child;
	}

	protected override void OnParametersSet()
	{
		base.OnParametersSet();

		if ( Options == null ) return;

		DeleteChildren();

		foreach ( var option in Options )
		{
			var btn = AddButton( option.Title, () => Value = option.Value );
			btn.Value = option.Value?.ToString();
		}

		SetSelectedButton();
	}

	public override void Tick()
	{
		base.Tick();

		if ( Options is null )
		{
			foreach ( var btn in ChildrenOfType<Button>() )
			{
				btn.Active = object.Equals( btn.Value, _value );
			}
		}
	}

	Panel _selected;

	/// <summary>
	/// The selected button panel.
	/// </summary>
	public Panel SelectedButton
	{
		get => _selected;
		set
		{
			if ( _selected == value )
				return;

			if ( _selected is Button oldButton )
				oldButton.Active = false;

			_selected?.RemoveClass( "active" );
			_selected?.CreateEvent( "stopactive" );

			_selected = value;

			if ( _selected is Button newbutton )
			{
				newbutton.Active = true;

				if ( newbutton.Value != null )
					Value = newbutton.Value;
			}

			_selected?.AddClass( "active" );
			_selected?.CreateEvent( "startactive" );
		}
	}

	void SetSelectedButton()
	{
		if ( Options is null )
			return;

		for ( int i = 0; i < Options.Count; i++ )
		{
			if ( string.Equals( Options[i].Value?.ToString(), _value?.ToString(), System.StringComparison.OrdinalIgnoreCase ) )
			{
				SelectedButton = Children.ElementAtOrDefault( i );
			}
		}
	}
}
