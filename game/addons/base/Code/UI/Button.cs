using Microsoft.AspNetCore.Components.Rendering;
using Sandbox.UI.Navigation;

namespace Sandbox.UI;

/// <summary>
/// A simple button <see cref="Panel"/>.
/// </summary>
[Library( "Button" )]
public class Button : Panel, INavigationEvent
{
	/// <summary>
	/// The <see cref="Label"/> that displays <see cref="Text"/>.
	/// </summary>
	protected Label TextLabel;

	/// <summary>
	/// The <see cref="IconPanel"/> that displays <see cref="Icon"/>.
	/// </summary>
	protected IconPanel IconPanel;

	/// <summary>
	/// The <see cref="Label"/> that displays <see cref="Help"/>.
	/// </summary>
	protected Label HelpLabel;

	/// <summary>
	/// The column on the right, holding the label and help
	/// </summary>
	protected Panel RightColumn;

	/// <summary>
	/// The target link, if set
	/// </summary>
	[Parameter] public string Href { get; set; }

	/// <summary>
	/// Used for selection status in things like ButtonGroup
	/// </summary>
	public virtual object Value { get; set; }

	/// <summary>
	/// More button options underneath the main button content.
	/// </summary>
	[Parameter] public RenderFragment HoverMenu { get; set; }

	public Button()
	{
		AddClass( "button" );

		IconPanel = AddChild( new IconPanel( "people", "icon" ) );
		IconPanel.Style.Display = DisplayMode.None;

		RightColumn = AddChild( new Panel( this, "button-right-column" ) );
		RightColumn.Style.Display = DisplayMode.None;

		TextLabel = RightColumn.AddChild( new Label( "Empty Label", "button-label button-text" ) );
		TextLabel.Style.Display = DisplayMode.None;

		HelpLabel ??= RightColumn.AddChild( new Label( "", "button-help" ) );
		HelpLabel.Style.Display = DisplayMode.None;
	}

	public Button( string text, Action action = default ) : this()
	{
		if ( text != null )
			Text = text;

		if ( action != null )
			AddEventListener( "onclick", action );
	}

	public Button( string text, string icon ) : this()
	{
		if ( icon != null )
			Icon = icon;

		if ( text != null )
			Text = text;
	}

	/// <summary>
	/// The button is disabled for some reason
	/// </summary>
	[Parameter]
	public bool Disabled
	{
		get => HasClass( "disabled" );
		set => SetClass( "disabled", value );
	}

	/// <summary>
	/// Allow external factors to force the active state
	/// </summary>
	[Parameter] public bool Active { get; set; }

	public Button( string text, string icon, Action onClick ) : this( text, icon )
	{
		AddEventListener( "onclick", onClick );
	}

	public Button( string text, string icon, string className, Action onClick ) : this( text, icon, onClick )
	{
		AddClass( className );
	}

	/// <summary>
	/// Text for the button.
	/// </summary>
	[Parameter]
	public string Text
	{
		get => TextLabel?.Text;
		set
		{
			if ( string.IsNullOrEmpty( value ) )
			{
				TextLabel.Style.Display = DisplayMode.None;
				TextLabel.Text = "";
				return;
			}

			TextLabel.Style.Display = DisplayMode.Flex;
			RightColumn.Style.Display = DisplayMode.Flex;
			TextLabel.Text = value;
		}
	}

	/// <summary>
	/// Help for the button.
	/// </summary>
	[Parameter]
	public string Help
	{
		get => HelpLabel?.Text;
		set
		{
			if ( string.IsNullOrEmpty( value ) )
			{
				HelpLabel.Style.Display = DisplayMode.None;
				HelpLabel.Text = "";
				return;
			}

			HelpLabel.Style.Display = DisplayMode.Flex;
			RightColumn.Style.Display = DisplayMode.Flex;
			HelpLabel.Text = value;
		}
	}

	/// <summary>
	/// Deletes the <see cref="Text"/>.
	/// </summary>
	public void DeleteText()
	{
		if ( !TextLabel.IsValid() ) return;

		TextLabel?.Delete();
		TextLabel = null;
	}

	/// <summary>
	/// Icon for the button.
	/// </summary>
	[Parameter]
	public string Icon
	{
		get => IconPanel?.Text;
		set
		{
			if ( string.IsNullOrEmpty( value ) )
			{
				IconPanel.Style.Display = DisplayMode.None;
				return;
			}

			IconPanel.Style.Display = DisplayMode.Flex;
			IconPanel.Text = value;
			SetClass( "has-icon", IconPanel.IsValid() );
		}
	}

	/// <summary>
	/// Deletes the <see cref="Icon"/>.
	/// </summary>
	public void DeleteIcon()
	{
		IconPanel.Style.Display = DisplayMode.None;
	}

	/// <summary>
	/// Set the text for the button. Calls <c>Text = value</c>
	/// </summary>
	public virtual void SetText( string text )
	{
		Text = text;
	}

	/// <summary>
	/// Imitate the button being clicked.
	/// </summary>
	public void Click()
	{
		CreateEvent( new MousePanelEvent( "onclick", this, "mouseleft" ) );
	}

	public override void SetProperty( string name, string value )
	{
		switch ( name )
		{
			case "text":
				{
					SetText( value );
					return;
				}

			case "html":
				{
					SetText( value );
					return;
				}

			case "icon":
				{
					Icon = value;
					return;
				}

			case "href":
				{
					Href = value;
					return;
				}

			case "active":
				{
					SetClass( "active", value.ToBool() );
					return;
				}
		}

		base.SetProperty( name, value );
	}

	public override void SetContent( string value )
	{
		SetText( value?.Trim() ?? "" );
	}

	NavigationHost _navigatorCache;


	public override void Tick()
	{
		base.Tick();
		UpdateActiveState();
	}

	protected void UpdateActiveState()
	{
		if ( Active )
		{
			SetClass( "active", true );
			return;
		}

		if ( string.IsNullOrWhiteSpace( Href ) )
		{
			SetClass( "active", false );
			return;
		}

		_navigatorCache ??= Ancestors.OfType<NavigationHost>().FirstOrDefault();

		var active = (_navigatorCache?.CurrentUrlMatches( Href ) ?? false);
		SetClass( "active", active );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( !string.IsNullOrWhiteSpace( Href ) )
		{
			this.Navigate( Href );
			e.StopPropagation();
		}
	}

	protected override void BuildRenderTree( RenderTreeBuilder tree )
	{
		if ( HoverMenu != null )
		{
			var classes = "button-hover-menu";
			if ( !HasHovered ) classes += " hidden";

			tree.OpenElement<Panel>( 0 );
			tree.AddAttribute( 1, "class", classes );

			HoverMenu?.Invoke( tree );
			tree.CloseElement();
		}
	}

	protected override int BuildHash() => HashCode.Combine( HoverMenu, HasHovered );
	protected override string GetRenderTreeChecksum() => $"{BuildHash()}";

	void INavigationEvent.OnNavigated( string url )
	{
		UpdateActiveState();
	}
}
