using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A simple checkbox <see cref="Panel"/>.
/// </summary>
[Library( "checkbox" )]
public class Checkbox : Panel
{
	/// <summary>
	/// Called when the checked state has been changed.
	/// </summary>
	[Parameter] public System.Action<bool> ValueChanged { get; set; }

	/// <summary>
	/// The checkmark icon. Although no guarantees it's an icon!
	/// </summary>
	public Panel CheckMark { get; protected set; }

	/// <summary>
	/// Use <see cref="Checked"/>.
	/// </summary>
	private bool isChecked = false;

	/// <summary>
	/// Returns true if this checkbox is checked.
	/// </summary>
	[Parameter]
	public bool Checked
	{
		get => isChecked;
		set
		{
			if ( isChecked == value )
				return;

			isChecked = value;
			OnValueChanged();
		}
	}

	/// <summary>
	/// Returns true if this checkbox is checked.
	/// </summary>
	[Parameter]
	public bool Value
	{
		get => Checked;
		set => Checked = value;
	}

	/// <summary>
	/// The <see cref="UI.Label"/> that displays <see cref="LabelText"/>.
	/// </summary>
	public Label Label { get; protected set; }

	/// <summary>
	/// Text for the checkbox label.
	/// </summary>
	[Parameter]
	public string LabelText
	{
		get => Label?.Text;
		set
		{
			if ( !Label.IsValid() )
			{
				Label = Add.Label();
			}

			Label.Text = value;
		}
	}

	public Checkbox()
	{
		AddClass( "checkbox" );
		CheckMark = Add.Icon( "check", "checkmark" );
	}

	public override void SetProperty( string name, string value )
	{
		base.SetProperty( name, value );

		if ( name == "checked" || name == "value" )
		{
			Checked = value.ToBool();
		}

		if ( name == "text" )
		{
			LabelText = value;
		}
	}

	public override void SetContent( string value )
	{
		LabelText = value?.Trim() ?? "";
	}

	/// <summary>
	/// Called when <see cref="Value"/> changes.
	/// </summary>
	public virtual void OnValueChanged()
	{
		UpdateState();
		CreateEvent( "onchange", Checked );

		if ( Checked )
		{
			CreateEvent( "onchecked" );
		}
		else
		{
			CreateEvent( "onunchecked" );
		}
	}

	/// <summary>
	/// Called to update visuals of the checkbox. By default this applies <c>checked</c> CSS class.
	/// </summary>
	protected virtual void UpdateState()
	{
		SetClass( "checked", Checked );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		Checked = !Checked;
		CreateValueEvent( "checked", Checked );
		CreateValueEvent( "value", Checked );
		e.StopPropagation();

		ValueChanged?.Invoke( Checked );
		e.StopPropagation();
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		e.StopPropagation();
	}
}
