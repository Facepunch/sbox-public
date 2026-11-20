namespace Sandbox.UI;

[CustomEditor( typeof( ClientInput ) )]
public partial class ClientInputControl : BaseControl
{
	Panel _preview;
	InputHint _inputHint;
	IconPanel _fallbackIcon;
	Label _bindLabel;

	public override bool SupportsMultiEdit => true;

	public ClientInputControl()
	{
		_preview = AddChild<Panel>( "preview" );
		_inputHint = _preview.AddChild<InputHint>( "hint" );
		_fallbackIcon = _preview.AddChild<IconPanel>( "fallback" );
		_fallbackIcon.Text = "keyboard";
		_bindLabel = AddChild<Label>( "bind-label" );
	}

	public override void Rebuild()
	{
		if ( Property == null ) return;
		if ( _capturing ) return;

		var input = Property.GetValue<ClientInput>();
		var action = input.Action;
		var keys = input.Keys;
		var outputCount = GetLinkedOutputs().Count( IsConnected );

		// A raw key, glyph for the last key of a combo (ctrl + k shows k)
		if ( keys.Length > 0 )
		{
			_inputHint.Action = null;
			_inputHint.Key = keys[^1];
			_inputHint.SetClass( "hidden", false );
			_fallbackIcon.SetClass( "hidden", true );
			SetClass( "no-binding", false );

			var keyLabel = KeyLabel( keys );
			_bindLabel.Text = outputCount > 0
				? Game.Language.GetPhrase( "ui.input.binding_with_outputs", new() { { "binding", keyLabel }, { "count", outputCount } } )
				: keyLabel;
			return;
		}

		_inputHint.Key = null;

		if ( string.IsNullOrWhiteSpace( action ) )
		{
			_inputHint.Action = null;
			_inputHint.SetClass( "hidden", true );
			_fallbackIcon.SetClass( "hidden", false );
			_bindLabel.Text = outputCount > 0
				? Game.Language.GetPhrase( outputCount == 1 ? "ui.input.one_connected_output" : "ui.input.connected_outputs", new() { { "count", outputCount } } )
				: "#ui.input.no_binding";
			SetClass( "no-binding", outputCount == 0 );
			return;
		}

		_inputHint.Action = action;
		_inputHint.SetClass( "hidden", false );
		_fallbackIcon.SetClass( "hidden", true );
		SetClass( "no-binding", false );

		var match = Input.GetActions().FirstOrDefault( a => a.Name == action );
		var label = match != null ? LocalizedText.InputTitle( match ) : action;
		_bindLabel.Text = outputCount > 0
			? Game.Language.GetPhrase( "ui.input.binding_with_outputs", new() { { "binding", label }, { "count", outputCount } } )
			: label;
	}

	/// <summary>
	/// Refreshes composed binding and connection labels when the language changes.
	/// </summary>
	public override void LanguageChanged()
	{
		base.LanguageChanged();
		Rebuild();
	}

	bool _capturing;

	/// <summary>
	/// Left click listens for a key, or combo, and binds it. Mouse buttons are captured too, so clicking
	/// again binds mouse1 - escape backs out.
	/// </summary>
	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		_capturing = true;
		SetClass( "capturing", true );
		_inputHint.SetClass( "hidden", true );
		_fallbackIcon.SetClass( "hidden", false );
		_bindLabel.Text = "#ui.input.press_key";

		Input.Keyboard.Capture( OnCaptured );
	}

	/// <summary>
	/// Right click opens the menu: clearing the key, and linked outputs.
	/// </summary>
	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );
		OpenMenu();
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		if ( _capturing ) Input.Keyboard.CancelCapture();
	}

	void OnCaptured( IReadOnlyList<string> buttons )
	{
		if ( !_capturing ) return;

		_capturing = false;
		SetClass( "capturing", false );

		if ( !IsValid || Property == null ) return;

		// Cancelled
		if ( buttons.Count == 0 )
		{
			Rebuild();
			return;
		}

		OnBindChanged( string.Join( "+", buttons ) );
	}

	static string KeyLabel( string[] keys ) => string.Join( " + ", keys ).ToUpperInvariant();

	void OpenMenu()
	{
		var menu = new Sandbox.UI.Menu();
		menu.AddOption( "#ui.input.no_key_binding", "", () => OnBindChanged( "" ) );

		var outputs = GetLinkedOutputs().ToArray();
		if ( outputs.Length > 0 )
		{
			var sub = menu.AddMenu( "#ui.input.linked_outputs", "cable" );
			foreach ( var output in outputs )
			{
				var connected = IsConnected( output );
				var source = output;
				sub.AddOption(
					Game.Language.GetPhrase( "ui.input.linked_output", new()
					{
						{ "object", LocalizedText.Resolve( output.Component.GameObject.Name ) },
						{ "output", LocalizedText.Resolve( output.Title ) }
					} ),
					connected ? "check_box" : "check_box_outline_blank",
					() => SetConnected( source, !connected )
				);
			}
		}

		menu.Open( this, Popup.PositionMode.UnderMouse );
	}

	IEnumerable<SignalOutputDescription> GetLinkedOutputs()
	{
		var visitedRoots = new HashSet<GameObject>();

		foreach ( var target in GetTargetComponents() )
		{
			if ( !visitedRoots.Add( target.GameObject.Root ) ) continue;

			foreach ( var output in SignalSystem.GetContraptionOutputs( target.GameObject ) )
			{
				if ( CanConnect( output ) ) yield return output;
			}
		}
	}

	bool CanConnect( SignalOutputDescription output )
	{
		return GetTargetInputs().Any( input => SignalSystem.AreCompatible( output, input ) );
	}

	IEnumerable<SignalInputDescription> GetTargetInputs()
	{
		foreach ( var target in GetTargetComponents() )
		{
			foreach ( var input in SignalSystem.GetInputs( target ) )
			{
				if ( string.Equals( input.Id, Property.Name, StringComparison.OrdinalIgnoreCase ) )
					yield return input;
			}
		}
	}

	IEnumerable<Component> GetTargetComponents()
	{
		foreach ( var target in Property.Parent?.Targets ?? Enumerable.Empty<object>() )
		{
			if ( target is Component component && component.IsValid() )
				yield return component;
		}
	}

	bool IsConnected( SignalOutputDescription output )
	{
		return GetTargetInputs().Any( input => SignalSystem.IsConnected( output, input ) );
	}

	void SetConnected( SignalOutputDescription output, bool connected )
	{
		foreach ( var input in GetTargetInputs() )
			SignalSystem.SetConnected( output, input, connected );

		Rebuild();
	}

	/// <summary>
	/// Bind a raw key combo, or nothing. Clears any action from before raw keys.
	/// </summary>
	void OnBindChanged( string keys )
	{
		var current = Property.GetValue<ClientInput>();
		current.Key = keys;
		current.Action = "";
		Property.SetValue( current );

		foreach ( var target in Property.Parent?.Targets ?? Enumerable.Empty<object>() )
		{
			if ( target is Component component )
				GameManager.ChangeProperty( component, Property.Name, current );
		}

		Rebuild();
	}
}
