using Sandbox.UI;
using Sandbox.UI.Construct;

namespace Sandbox;

/// <summary>
/// A small window with a title bar and a <see cref="UISheet"/> of whatever object you give it.
/// </summary>
public class UIModal : Panel
{
	bool _wired;
	bool _focused;
	int _framesLaidOut;

	/// <summary>
	/// What goes under the title bar.
	/// </summary>
	public Panel Body { get; }

	/// <summary>
	/// Called when Enter is pressed in one of its text boxes.
	/// </summary>
	public Action OnSubmit { get; set; }

	/// <summary>
	/// Called after it closed, by any of the ways.
	/// </summary>
	public Action OnClosed { get; set; }

	public UIModal( string title, object target ) : this( title )
	{
		var sheet = Body.AddChild<UISheet>();
		sheet.PropertyFilter = UISheet.PropertiesAndButtons;
		sheet.Target = target;
	}

	UIModal( string title )
	{
		AddClass( "modal" );

		var window = Add.Panel( "window" );
		window.Add.Panel( "title-bar" ).Add.Label( title );

		Body = window.Add.Panel( "body" );
	}

	/// <summary>
	/// Opens a window over the menu the panel is in. Only one stays open, a new one replaces it.
	/// </summary>
	public static UIModal Show( Panel source, string title, object target ) => Open( source, new UIModal( title, target ) );

	static UIModal Open( Panel source, UIModal modal )
	{
		// over the menu while it's open, so it closes with it, otherwise over the HUD
		var layer = SpawnMenuHost.IsOpen ? SpawnMenuHost.ModalLayer : UIModalLayer.Find( source );
		if ( layer is null )
		{
			modal.Delete( true );
			return null;
		}

		foreach ( var other in layer.Children.OfType<UIModal>().ToList() )
			other.Close();

		modal.Parent = layer;

		return modal;
	}

	/// <summary>
	/// A window with a text box and two buttons, for asking a name
	/// </summary>
	/// <param name="title">The title bar, can be a localization token.</param>
	/// <param name="value">What the text box starts with.</param>
	/// <param name="onConfirm">Called with the name when it is confirmed.</param>
	/// <param name="placeholder">What the empty text box says.</param>
	/// <param name="confirmText">What the confirm button says.</param>
	/// <param name="confirmIcon">The icon of the confirm button.</param>
	/// <param name="message">A line above the text box, rich text like <see cref="Confirm"/>'s.</param>
	public static UIModal Prompt( Panel source, string title, string value, Action<string> onConfirm, string placeholder = null, string confirmText = "#ui.common.confirm", string confirmIcon = "check", string message = null )
	{
		value ??= "";
		UIModal modal = null;

		bool HasValue() => !string.IsNullOrWhiteSpace( value );

		void Confirm()
		{
			if ( !HasValue() ) return;

			onConfirm?.Invoke( value.Trim() );
			modal?.Close();
		}

		var attributes = new List<Attribute> { new PropertyAttribute(), new HideLabelAttribute() };
		if ( placeholder is not null )
			attributes.Add( new PlaceholderAttribute( placeholder ) );

		var properties = new List<SerializedProperty>
		{
			Game.TypeLibrary.CreateProperty( "Value", () => value, v => value = v, attributes.ToArray() ),
			new UIActionProperty( confirmText, confirmIcon, Confirm, 1, separator: true ) { Enabled = HasValue },
			new UIActionProperty( "#spawnmenu.common.cancel", null, () => modal?.Close(), 2, sameLine: true )
		};

		modal = Show( source, title, properties );
		if ( modal is null ) return null;

		modal.AddClass( "prompt" );
		modal.OnSubmit = Confirm;

		if ( message is not null )
		{
			var text = modal.Body.Add.Label( "", "message" );
			text.IsRich = true;
			text.Text = message;
			modal.Body.SetChildIndex( text, 0 );
		}

		return modal;
	}

	/// <summary>
	/// A confirmation window with buttons underneath
	/// </summary>
	/// <param name="title">The title bar, can be a localization token.</param>
	/// <param name="message">The question, rich text, so &lt;b&gt; works. Escape names in it with <see cref="Escape"/>.</param>
	/// <param name="onConfirm">Called when it is confirmed.</param>
	/// <param name="confirmText">What the confirm button says.</param>
	/// <param name="cancelText">What the cancel button says, no cancel button when null.</param>
	public static UIModal Confirm( Panel source, string title, string message, Action onConfirm, string confirmText = "#ui.common.confirm", string cancelText = "#spawnmenu.common.cancel" )
	{
		var modal = new UIModal( title );
		modal.AddClass( "confirm" );

		var text = modal.Body.Add.Label( "", "message" );
		text.IsRich = true;
		text.Text = message;

		modal.Body.AddChild<UISeparator>();

		modal.OnSubmit = () =>
		{
			onConfirm?.Invoke();
			modal.Close();
		};

		var buttons = modal.Body.Add.Panel( "buttons" );
		AddButton( buttons, confirmText, modal.Submit );

		if ( cancelText is not null )
			AddButton( buttons, cancelText, modal.Close );

		return Open( source, modal );
	}

	static void AddButton( Panel row, string text, Action action )
	{
		var button = row.AddChild<UIButton>();
		button.Text = text;
		button.AddEventListener( "onclick", action );
	}

	/// <summary>
	/// Makes text safe to put in the rich message of <see cref="Confirm"/>
	/// </summary>
	public static string Escape( string text ) => text?.Replace( "&", "&amp;" ).Replace( "<", "&lt;" ).Replace( ">", "&gt;" );

	public void Submit() => OnSubmit?.Invoke();

	public void Close()
	{
		if ( !this.IsValid() ) return;

		Delete();
		OnClosed?.Invoke();
	}

	public override void Tick()
	{
		base.Tick();

		if ( _focused ) return;

		var entry = Descendants.OfType<TextEntry>().FirstOrDefault();
		if ( entry is not null && !_wired )
		{
			_wired = true;
			entry.AddEventListener( "onsubmit", Submit );
			entry.AddEventListener( "oncancel", Close );
		}

		if ( (entry ?? (Panel)this).Box.Rect.Width <= 0f || ++_framesLaidOut < 3 ) return;

		_focused = true;

		// nothing to type in, take focus so Enter and Escape still reach us
		if ( entry is null )
		{
			AcceptsFocus = true;
			Focus();
			return;
		}

		entry.Focus();
		entry.CaretPosition = entry.TextLength;
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Button == "escape" )
		{
			Close();
			e.StopPropagation = true;
			return;
		}

		if ( e.Button is "enter" or "pad_enter" && InputFocus.Current == this )
		{
			Submit();
			e.StopPropagation = true;
			return;
		}

		base.OnButtonTyped( e );
	}
}

/// <summary>
/// Centers a <see cref="UIModal"/> over the HUD when it isn't opened from the menu. Goes away with its last modal.
/// </summary>
public class UIModalLayer : Panel
{
	public static Panel Find( Panel source )
	{
		var root = source.FindPopupPanel();
		if ( root is null ) return null;

		return root.ChildrenOfType<UIModalLayer>().FirstOrDefault( x => !x.IsDeleting ) ?? root.AddChild<UIModalLayer>();
	}

	public override void Tick()
	{
		base.Tick();

		if ( !Children.Any( x => !x.IsDeleting ) )
			Delete( true );
	}
}
