namespace Sandbox.UI;

[CustomEditor( typeof( ClientInput ) )]
public partial class ClientInputControl : BaseControl
{
	InputHint _inputHint;
	IconPanel _fallbackIcon;
	Label _bindLabel;
	bool _capturing;

	/// <inheritdoc/>
	public override bool SupportsMultiEdit => true;

	/// <summary>
	/// Shows the bound action and the number of wired outputs.
	/// </summary>
	public ClientInputControl()
	{
		var preview = AddChild<Panel>( "preview" );
		_inputHint = preview.AddChild<InputHint>( "hint" );
		_fallbackIcon = preview.AddChild<IconPanel>( "fallback" );
		_fallbackIcon.Text = "keyboard";
		_bindLabel = AddChild<Label>( "bind-label" );
	}

	/// <inheritdoc/>
	public override void Rebuild()
	{
		if ( Property == null || _capturing ) return;

		var binding = Property.GetValue<ClientInput>();
		var outputCount = GetLinkedOutputs().Count( link => link.Connected );
		_inputHint.Action = binding.Action;
		_inputHint.Key = binding.HasKey ? binding.Keys[^1] : null;
		_inputHint.SetClass( "hidden", !binding.IsEnabled );
		_fallbackIcon.SetClass( "hidden", binding.IsEnabled );

		if ( !binding.IsEnabled )
		{
			_bindLabel.Text = outputCount > 0
				? Game.Language.GetPhrase( outputCount == 1 ? "ui.input.one_connected_output" : "ui.input.connected_outputs", new() { { "count", outputCount } } )
				: "#ui.input.no_binding";
			return;
		}

		var label = binding.Key?.Replace( "+", " + " ).ToUpperInvariant();
		if ( label is null )
		{
			var match = Input.GetActions().FirstOrDefault( a => a.Name == binding.Action );
			label = match != null ? LocalizedText.InputTitle( match ) : binding.Action;
		}
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

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		if ( _capturing || Property is null ) return;
		_capturing = true;
		SetClass( "capturing", true );
		_inputHint.SetClass( "hidden", true );
		_fallbackIcon.SetClass( "hidden", false );
		_bindLabel.Text = "#ui.input.press_key";
		Input.Keyboard.Capture( OnCaptured );
	}

	/// <inheritdoc/>
	public override void OnDeleted()
	{
		if ( _capturing ) Input.Keyboard.CancelCapture();
		base.OnDeleted();
	}

	void OnCaptured( IReadOnlyList<string> buttons )
	{
		_capturing = false;
		if ( !IsValid || Property is null ) return;

		SetClass( "capturing", false );
		if ( buttons.Count == 0 )
		{
			Rebuild();
			return;
		}

		var matches = Input.Keyboard.GetBoundActions( buttons );

		if ( matches.Count == 1 )
		{
			OnBindChanged( matches[0].Name );
			return;
		}

		if ( matches.Count == 0 )
		{
			SetBinding( new ClientInput { Key = string.Join( "+", buttons ).ToLowerInvariant() } );
			return;
		}

		Rebuild();
		var menu = new Menu();
		foreach ( var action in matches )
			menu.AddOption( ActionLabel( action ), "", () => OnBindChanged( action.Name ) );
		menu.Open( this, Popup.PositionMode.UnderMouse );
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );

		var menu = new Menu();
		menu.AddOption( "#ui.input.no_key_binding", "", () => OnBindChanged( "" ) );

		var outputs = GetLinkedOutputs().ToArray();
		if ( outputs.Length > 0 )
		{
			var sub = menu.AddMenu( "#ui.input.linked_outputs", "cable" );
			foreach ( var (output, connected) in outputs )
			{
				sub.AddOption(
					Game.Language.GetPhrase( "ui.input.linked_output", new()
					{
						{ "object", LocalizedText.Resolve( output.Component.GameObject.Name ) },
						{ "output", LocalizedText.Resolve( output.Title ) }
					} ),
					connected ? "check_box" : "check_box_outline_blank",
					() => SetConnected( output, !connected )
				);
			}
		}

		menu.AddSeparator();

		var grouped = Input.GetActions()
			.GroupBy( a => a.GroupName ?? "" )
			.OrderBy( g => LocalizedText.InputGroup( g.Key ), StringComparer.CurrentCultureIgnoreCase );

		foreach ( var group in grouped )
		{
			var actionMenu = string.IsNullOrWhiteSpace( group.Key ) ? menu : menu.AddMenu( LocalizedText.InputGroup( group.Key ) );
			foreach ( var action in group )
			{
				actionMenu.AddOption( ActionLabel( action ), "", () => OnBindChanged( action.Name ) );
			}
		}

		menu.Open( this, Popup.PositionMode.UnderMouse );
	}

	IEnumerable<(SignalOutputDescription Output, bool Connected)> GetLinkedOutputs()
	{
		var inputs = GetTargetInputs().ToArray();
		if ( inputs.Length == 0 ) yield break;

		foreach ( var root in GetTargetComponents().Select( target => target.GameObject.Root ).Distinct() )
		{
			foreach ( var output in SignalSystem.GetContraptionOutputs( root ) )
			{
				if ( inputs.Any( input => SignalSystem.AreCompatible( output, input ) ) )
					yield return (output, inputs.Any( input => SignalSystem.IsConnected( output, input ) ));
			}
		}
	}

	IEnumerable<SignalInputDescription> GetTargetInputs()
	{
		return GetTargetComponents().SelectMany( SignalSystem.GetInputs )
			.Where( input => string.Equals( input.Id, Property.Name, StringComparison.OrdinalIgnoreCase ) );
	}

	IEnumerable<Component> GetTargetComponents()
	{
		return Property.Parent?.Targets.OfType<Component>().Where( component => component.IsValid() ) ?? [];
	}

	void SetConnected( SignalOutputDescription output, bool connected )
	{
		foreach ( var input in GetTargetInputs() )
			SignalSystem.SetConnected( output, input, connected );

		Rebuild();
	}

	string ActionLabel( InputAction a )
	{
		var title = LocalizedText.InputTitle( a );
		var origin = Input.GetButtonOrigin( a.Name );
		return origin != null
			? Game.Language.GetPhrase( "ui.input.binding_with_key", new() { { "binding", title }, { "key", origin } } )
			: title;
	}

	void OnBindChanged( string value ) => SetBinding( new ClientInput { Action = value } );

	void SetBinding( ClientInput current )
	{
		Property.SetValue( current );

		foreach ( var component in GetTargetComponents() )
			GameManager.ChangeProperty( component, Property.Name, current );

		Rebuild();
	}
}
