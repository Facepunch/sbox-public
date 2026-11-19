/// <summary>
/// The raw value of a signal delivery. You rarely need this — declare inputs as plain
/// <c>bool</c>, <c>float</c>, or parameterless methods and the system converts for you.
/// </summary>
public readonly record struct SignalEvent( float Analog, bool Down, bool Pressed, bool Released, Player Instigator );

/// <summary>
/// Makes a method or property wireable as an input. Works on: a parameterless method (fires
/// once when the signal turns on), a method taking a <c>bool</c> or <c>float</c>, or a writable
/// <c>bool</c>/<c>float</c> property.
/// On a writable property of a Component type it becomes a component input instead: linking
/// sets the property to the source component directly, and only components of that type
/// (marked <see cref="SignalOutputAttribute"/> on their class) can be linked to it.
/// The member's name is the wire's name — renaming it breaks saved contraptions.
/// </summary>
[AttributeUsage( AttributeTargets.Method | AttributeTargets.Property )]
public sealed class SignalInputAttribute : Attribute
{
	/// <summary>
	/// The stable port id. Defaults to the member name.
	/// </summary>
	public string Id { get; set; }
	public string Name { get; set; }
	public bool Default { get; set; }
}

/// <summary>
/// Makes a <see cref="SignalOutput"/> property wireable as an output.
/// The member's name is the wire's name — renaming it breaks saved contraptions.
/// On a class it exposes the component itself as a linkable reference instead: players can
/// wire it into any component input (<see cref="SignalInputAttribute"/> on a property of a
/// matching type) and the input receives the component directly. Wired into a boolean
/// input instead, it holds the input on for as long as the link exists.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Class )]
public sealed class SignalOutputAttribute : Attribute
{
	public string Name { get; set; }
	public bool Default { get; set; }
}

/// <summary>
/// A component with wireable ports can give itself a more specific name than its type's title, so several of the same
/// thing on one contraption can be told apart in the wire tool (e.g. "Speaker: horn" and "Speaker: siren").
/// Return null to use the type's title.
/// </summary>
public interface ISignalDisplayName
{
	string SignalDisplayName { get; }
}

/// <summary>
/// One wire: which component and which input it feeds into.
/// </summary>
public sealed class SignalConnection
{
	[Property]
	public Component Target { get; set; }

	[Property]
	public string Input { get; set; }
}

/// <summary>
/// An output port that players can wire to inputs on other entities. Call whichever
/// <c>Emit</c> overload matches what you have — the receiving end converts as needed.
/// State providers should emit every tick while active; this keeps continuous inputs push-driven.
/// Only does anything on the host.
///
/// Credit follows automatically: whoever last pressed or signaled this component is
/// passed along with the emit. Pass an instigator explicitly only to override that.
/// </summary>
public sealed class SignalOutput
{
	[Property]
	public List<SignalConnection> Connections { get; set; } = new();

	private bool _lastDown;
	private float _lastAnalog;

	/// <summary>
	/// Fire a one-shot pulse — "something happened once".
	/// </summary>
	public void Emit( Component source, Player instigator = null )
	{
		instigator ??= SignalSystem.InstigatorFor( source );
		SignalSystem.Emit( this, new SignalEvent( 1f, true, true, false, instigator ) );
		SignalSystem.Emit( this, new SignalEvent( 0f, false, false, true, instigator ) );
		_lastDown = false;
		_lastAnalog = 0f;
	}

	/// <summary>
	/// Emit an on/off state.
	/// </summary>
	public void Emit( Component source, bool value, Player instigator = null )
	{
		// Active state is continuous; idle only needs to travel once on release.
		if ( !value && !_lastDown ) return;

		var pressed = value && !_lastDown;
		var released = !value && _lastDown;
		_lastDown = value;
		_lastAnalog = value ? 1f : 0f;

		instigator ??= SignalSystem.InstigatorFor( source );
		SignalSystem.Emit( this, new SignalEvent( _lastAnalog, value, pressed, released, instigator ) );
	}

	/// <summary>
	/// Emit a number. Values above 0.5 count as "on".
	/// </summary>
	public void Emit( Component source, float value, Player instigator = null )
	{
		// Keep non-zero analog signals continuous without ticking dormant graphs.
		if ( value.AlmostEqual( 0f ) && _lastAnalog.AlmostEqual( 0f ) ) return;

		var down = value > 0.5f;
		var pressed = down && !_lastDown;
		var released = !down && _lastDown;
		_lastDown = down;
		_lastAnalog = value;

		instigator ??= SignalSystem.InstigatorFor( source );
		SignalSystem.Emit( this, new SignalEvent( value, down, pressed, released, instigator ) );
	}
}

internal readonly record struct SignalInputContext( PropertyDescription Property, SignalInputAttribute Attribute );

/// <summary>
/// What actually runs when a signal arrives at an input.
/// </summary>
internal readonly record struct SignalInputBinding(
	MethodDescription Method,
	PropertyDescription Property,
	SignalInputKind Kind,
	Action<Component, SignalEvent> Custom = null )
{
	public void Invoke( Component component, SignalEvent value )
	{
		switch ( Kind )
		{
			case SignalInputKind.TriggerMethod:
				if ( value.Pressed ) Method.Invoke( component, null );
				break;
			case SignalInputKind.EventMethod:
				Method.Invoke( component, [value] );
				break;
			case SignalInputKind.BoolMethod:
				Method.Invoke( component, [value.Down] );
				break;
			case SignalInputKind.FloatMethod:
				Method.Invoke( component, [value.Analog] );
				break;
			case SignalInputKind.BoolProperty:
				Property.SetValue( component, value.Down );
				break;
			case SignalInputKind.FloatProperty:
				Property.SetValue( component, value.Analog );
				break;
			case SignalInputKind.ComponentProperty:
				// Direct component link — nothing travels through it, the wire set the property.
				break;
			case SignalInputKind.Custom:
				Custom?.Invoke( component, value );
				break;
		}
	}
}

internal enum SignalInputKind
{
	TriggerMethod,
	EventMethod,
	BoolMethod,
	FloatMethod,
	BoolProperty,
	FloatProperty,
	ComponentProperty,
	Custom
}

/// <summary>
/// Lets a custom property type act as a signal input. Just implement this anywhere —
/// it's found automatically.
/// </summary>
internal interface ISignalAdapter
{
	Type TargetType { get; }
	bool TryBind( SignalInputContext context, out SignalInputBinding binding );
}

/// <summary>
/// The shared metadata exposed by both ends of a signal connection.
/// </summary>
internal abstract class SignalPortDescription
{
	public Component Component { get; }
	public string Id { get; }
	public string Title { get; }
	public string ComponentTitle { get; }
	public string Description { get; }
	public string Icon => string.IsNullOrWhiteSpace( _icon ) ? Kind : _icon;
	public bool IsDefault { get; }
	public int Order { get; }

	/// <summary>
	/// Component ports: the component type this port provides or accepts. Null for signal ports.
	/// </summary>
	public Type ComponentType { get; }
	public string Kind => this is SignalOutputDescription ? "output" : "input";

	private readonly string _icon;

	protected SignalPortDescription( Component component, SignalPort port )
	{
		Component = component;
		Id = port.Id;
		Title = port.Title;
		ComponentTitle = SignalSystem.GetComponentTitle( component );
		Description = port.Description;
		_icon = port.Icon;
		IsDefault = port.IsDefault;
		Order = port.Order;
		ComponentType = port.ComponentType;
	}
}

/// <summary>
/// An output port found on a component: its name and how to reach it.
/// </summary>
internal sealed class SignalOutputDescription : SignalPortDescription
{
	public SignalOutput Port { get; }

	internal SignalOutputDescription( Component component, SignalPort port, SignalOutput output ) : base( component, port )
	{
		Port = output;
	}
}

/// <summary>
/// An input port found on a component: its name and how to reach it.
/// </summary>
internal sealed class SignalInputDescription : SignalPortDescription
{
	internal SignalInputDescription( Component component, SignalPort port ) : base( component, port )
	{
		Binding = port.Binding;
	}

	internal SignalInputBinding Binding { get; }
}

/// <summary>
/// One input or output declared on a component type.
/// </summary>
internal readonly record struct SignalPort( string Id, string Title, string Description, string Icon, bool IsDefault, int Order )
{
	/// <summary>
	/// Outputs: the property holding the SignalOutput.
	/// </summary>
	public PropertyDescription Property { get; init; }

	/// <summary>
	/// Inputs: what to run when a signal arrives.
	/// </summary>
	public SignalInputBinding Binding { get; init; }

	/// <summary>
	/// Component ports: the component type this port provides or accepts.
	/// </summary>
	public Type ComponentType { get; init; }
}

/// <summary>
/// Delivers signals from outputs to the inputs they're wired to. Everything runs on the host;
/// clients just see the results through normal networking. TypeLibrary owns member caching and
/// hotload invalidation; this system resolves ports from its current descriptions when needed.
/// </summary>
internal sealed class SignalSystem : GameObjectSystem<SignalSystem>, IContextMenuEvent
{
	/// <summary>
	/// Port id of the synthetic output a class-level <see cref="SignalOutputAttribute"/> creates.
	/// Can't collide with member names — C# identifiers can't contain '$'.
	/// </summary>
	internal const string ComponentOutputId = "$component";

	private SignalPortDescription _contextSource;

	// Who last pressed or signaled each component, so gates that re-emit later still
	// pass the credit along without hand-carrying it. Pruned as components die.
	private static readonly Dictionary<Component, Player> _instigators = new();
	private readonly List<Component> _deadInstigators = new();
	private RealTimeSince _sinceInstigatorPrune;

	public SignalSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 0, PruneInstigators, "PruneSignalInstigators" );
	}

	void IContextMenuEvent.PopulateContextMenu( IContextMenuEvent.Event e )
	{
		if ( !e.Target.IsValid() ) return;
		if ( _contextSource?.Component.IsValid() != true ) _contextSource = null;

		if ( _contextSource is null )
		{
			AddContextPorts( e, e.Target );
			AddContextUnlinks( e, e.Target );
			return;
		}

		var ports = GetCompatiblePorts( e.Target, _contextSource ).ToArray();

		if ( ports.Length > 0 && e.Target.Root != _contextSource.Component.GameObject.Root )
		{
			e.AddSubmenu( "link", "#signal.link", submenu =>
			{
				foreach ( var port in ports )
					submenu.AddOption( port.Title, port.Icon, () => CompleteContextLink( port ) );
			} );
		}

		e.AddOption( "link_off", "#signal.cancel_link", () => _contextSource = null );
	}

	private void AddContextPorts( IContextMenuEvent.Event e, GameObject target )
	{
		var ports = GetPorts( target ).ToArray();
		if ( ports.Length == 0 ) return;

		var defaults = ports.Where( port => port.IsDefault ).ToArray();
		if ( ports.Length == 1 || defaults.Length == 1 )
		{
			var port = ports.Length == 1 ? ports[0] : defaults[0];
			e.AddOption( "link", "#signal.link", () => _contextSource = port );
			return;
		}

		e.AddSubmenu( "link", "#signal.link", submenu =>
		{
			foreach ( var port in ports )
				submenu.AddOption( port.Title, port.Icon, () => _contextSource = port );
		} );
	}

	private void CompleteContextLink( SignalPortDescription target )
	{
		CreateLink( _contextSource, target );
		_contextSource = null;
	}

	/// <summary>
	/// One existing wire touching an object, as listed in the Unlink menu.
	/// </summary>
	internal readonly record struct WireInfo( Component Source, string OutputId, Component Target, string InputId );

	private static void AddContextUnlinks( IContextMenuEvent.Event e, GameObject target )
	{
		var wires = GetWires( target ).ToArray();
		if ( wires.Length == 0 ) return;

		var root = target.Root;

		e.AddSubmenu( "link_off", "#signal.unlink", submenu =>
		{
			foreach ( var wire in wires )
			{
				var outgoing = wire.Source.GameObject.Root == root;
				var captured = wire;
				submenu.AddOption( DescribeWire( wire ), outgoing ? "arrow_forward" : "arrow_back", () => Unlink( captured ) );
			}

			if ( wires.Length > 1 )
			{
				submenu.AddSeparator();
				submenu.AddOption( "#signal.unlink_all", "link_off", () =>
				{
					foreach ( var wire in wires ) Unlink( wire );
				} );
			}
		} );
	}

	/// <summary>
	/// "Button: Pressed → Thruster: Activate"
	/// </summary>
	private static string DescribeWire( WireInfo wire )
	{
		var output = TryFindOutput( wire.Source, wire.OutputId, out var outPort ) ? LocalizedText.Resolve( outPort.Title ) : wire.OutputId;
		var input = TryFindInput( wire.Target, wire.InputId, out var inPort ) ? LocalizedText.Resolve( inPort.Title ) : wire.InputId;

		return Game.Language.GetPhrase( "signal.wire_label", new()
		{
			{ "source", GetComponentTitle( wire.Source ) },
			{ "output", output },
			{ "target", GetComponentTitle( wire.Target ) },
			{ "input", input }
		} );
	}

	/// <summary>
	/// Every wire going into or out of this object's root: stamped link pairs, signal
	/// connections (including ones between welded objects that have no pair) and component references.
	/// </summary>
	internal static IEnumerable<WireInfo> GetWires( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return [];

		var root = gameObject.Root;
		var seen = new HashSet<(Component, string, Component, string)>();
		var result = new List<WireInfo>();

		void Add( Component source, string outputId, Component target, string inputId )
		{
			if ( !source.IsValid() || !target.IsValid() ) return;
			if ( string.IsNullOrEmpty( outputId ) || string.IsNullOrEmpty( inputId ) ) return;
			if ( seen.Add( (source, outputId.ToLowerInvariant(), target, inputId.ToLowerInvariant()) ) )
				result.Add( new WireInfo( source, outputId, target, inputId ) );
		}

		// Wires carried by link pairs on this object
		foreach ( var link in root.GetComponentsInChildren<ManualLink>( true ) )
		{
			if ( link.HasWire ) Add( link.SignalSource, link.SignalOutputId, link.SignalTarget, link.SignalInputId );
		}

		// Signal connections out of this object, and into it from the rest of the contraption
		foreach ( var output in GetContraptionOutputs( root ) )
		{
			if ( output.Port?.Connections is not { } connections ) continue;
			var fromHere = output.Component.GameObject.Root == root;

			foreach ( var connection in connections )
			{
				if ( connection?.Target.IsValid() != true ) continue;
				if ( !fromHere && connection.Target.GameObject.Root != root ) continue;

				Add( output.Component, output.Id, connection.Target, connection.Input );
			}
		}

		// Component references held by inputs on this object
		foreach ( var component in root.GetComponentsInChildren<Component>( true ) )
		{
			foreach ( var input in GetInputs( component ) )
			{
				if ( input.ComponentType is null ) continue;
				if ( input.Binding.Property?.GetValue( component ) is Component source && source.IsValid() )
					Add( source, ComponentOutputId, component, input.Id );
			}
		}

		return result;
	}

	/// <summary>
	/// Disconnect one wire, removing the link pair that carries it (if any).
	/// </summary>
	internal static void Unlink( WireInfo wire )
	{
		UnlinkRpc( wire.Source, wire.OutputId, wire.Target, wire.InputId );
	}

	[Rpc.Host]
	private static void UnlinkRpc( Component source, string outputId, Component target, string inputId )
	{
		if ( !source.IsValid() || !target.IsValid() ) return;
		if ( !TryFindOutput( source, outputId, out var output ) || !TryFindInput( target, inputId, out var input ) ) return;

		// Destroying the carrying pair disconnects the wire through ManualLink.OnDestroy.
		// Both ends are stamped, so finding one end is enough — it takes its partner with it.
		var carrier = source.GameObject.Root.GetComponentsInChildren<ManualLink>( true )
			.FirstOrDefault( link => link.IsValid() && link.HasWire
				&& link.SignalSource == source && link.SignalTarget == target
				&& string.Equals( link.SignalOutputId, outputId, StringComparison.OrdinalIgnoreCase )
				&& string.Equals( link.SignalInputId, inputId, StringComparison.OrdinalIgnoreCase ) );

		if ( carrier.IsValid() )
		{
			carrier.GameObject.Destroy();
		}
		else if ( !SetConnectedInternal( source, output, target, input, false ) )
		{
			// Unstamped wires (e.g. between welded objects) have no pair to destroy
			return;
		}

		SendConnectionNotice( Rpc.Caller, source, output, target, input, false );
	}

	/// <summary>
	/// The player who last pressed or signaled this component, or null.
	/// </summary>
	internal static Player InstigatorFor( Component component )
	{
		if ( component.IsValid() && _instigators.TryGetValue( component, out var player ) && player.IsValid() )
			return player;

		return null;
	}

	private static void RememberInstigator( Component component, Player player )
	{
		if ( !component.IsValid() || !player.IsValid() ) return;

		_instigators[component] = player;
	}

	private void PruneInstigators()
	{
		if ( Scene != Game.ActiveScene || !Networking.IsHost ) return;
		if ( _sinceInstigatorPrune < 1f ) return;
		_sinceInstigatorPrune = 0;

		foreach ( var entry in _instigators )
		{
			if ( !entry.Key.IsValid() || !entry.Value.IsValid() )
				_deadInstigators.Add( entry.Key );
		}

		foreach ( var component in _deadInstigators )
			_instigators.Remove( component );

		_deadInstigators.Clear();
	}

	public static IEnumerable<SignalOutputDescription> GetOutputs( Component component )
	{
		if ( !component.IsValid() ) yield break;

		foreach ( var port in GetOutputPorts( component ) )
		{
			if ( port.ComponentType is not null )
				yield return new SignalOutputDescription( component, port, null );
			else if ( port.Property.GetValue( component ) is SignalOutput output )
				yield return new SignalOutputDescription( component, port, output );
		}
	}

	public static IEnumerable<SignalInputDescription> GetInputs( Component component )
	{
		if ( !component.IsValid() ) yield break;

		foreach ( var port in GetInputPorts( component ) )
			yield return new SignalInputDescription( component, port );
	}

	/// <summary>
	/// Every output under this object's root. Unsorted — UI that lists ports uses <see cref="GetPorts"/>.
	/// </summary>
	public static IEnumerable<SignalOutputDescription> GetOutputs( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return [];

		return gameObject.Root.GetComponentsInChildren<Component>( true )
			.Where( component => component.Enabled )
			.SelectMany( GetOutputs );
	}

	public static IEnumerable<SignalPortDescription> GetPorts( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return [];

		return gameObject.Root.GetComponentsInChildren<Component>( true )
			.Where( component => component.Enabled )
			.SelectMany( component => GetOutputs( component ).Cast<SignalPortDescription>().Concat( GetInputs( component ) ) )
			.OrderBy( port => port.Order )
			.ThenByDescending( port => port.IsDefault )
			.ThenBy( port => LocalizedText.Resolve( port.ComponentTitle ), StringComparer.CurrentCultureIgnoreCase )
			.ThenBy( port => LocalizedText.Resolve( port.Title ), StringComparer.CurrentCultureIgnoreCase );
	}

	public static IEnumerable<SignalPortDescription> GetCompatiblePorts( GameObject gameObject, SignalPortDescription source )
	{
		return GetPorts( gameObject ).Where( port => AreCompatible( source, port ) );
	}

	internal static string GetComponentTitle( Component component )
	{
		// lets two of the same thing be told apart, e.g. "Speaker: horn"
		if ( component is ISignalDisplayName named && !string.IsNullOrWhiteSpace( named.SignalDisplayName ) )
			return named.SignalDisplayName;

		var title = Game.TypeLibrary.GetType( component.GetType() )?.Title ?? component.GetType().Name;
		return Game.Language.GetPhrase( title.TrimStart( '#' ) );
	}

	/// <summary>
	/// Any signal output can wire to any signal input. Component outputs wire to component
	/// inputs whose property type the component satisfies — or to boolean inputs, which they
	/// hold on for as long as the link exists.
	/// </summary>
	public static bool AreCompatible( SignalOutputDescription output, SignalInputDescription input )
	{
		if ( output is null || input is null ) return false;

		if ( output.ComponentType is null )
			return input.ComponentType is null;

		if ( input.ComponentType is not null )
			return output.Component.GetType().IsAssignableTo( input.ComponentType );

		return IsPresenceBindable( input.Binding );
	}

	private static bool IsPresenceBindable( SignalInputBinding binding )
	{
		return binding.Kind is SignalInputKind.BoolProperty or SignalInputKind.BoolMethod;
	}

	public static bool AreCompatible( SignalPortDescription first, SignalPortDescription second )
	{
		return TryResolve( first, second, out _, out _ );
	}

	public static bool IsConnected( SignalOutputDescription output, SignalInputDescription input )
	{
		if ( output is null || input is null ) return false;

		if ( input.ComponentType is not null )
			return input.Binding.Property?.GetValue( input.Component ) as Component == output.Component;

		return HasConnection( output.Port, input.Component, input.Id );
	}

	public static void SetConnected( SignalOutputDescription output, SignalInputDescription input, bool connected )
	{
		if ( output is null || input is null ) return;
		SetConnectionRpc( output.Component, output.Id, input.Component, input.Id, connected );
	}

	public static void SetConnected( SignalPortDescription first, SignalPortDescription second, bool connected )
	{
		if ( TryResolve( first, second, out var output, out var input ) )
			SetConnected( output, input, connected );
	}

	/// <summary>
	/// Create a logical object link and connect the selected signal ports in one host operation.
	/// </summary>
	public static void CreateLink( SignalOutputDescription output, SignalInputDescription input )
	{
		if ( output is null || input is null ) return;
		CreateLinkRpc( output.Component, output.Id, input.Component, input.Id );
	}

	public static void CreateLink( SignalPortDescription first, SignalPortDescription second )
	{
		if ( TryResolve( first, second, out var output, out var input ) )
			CreateLink( output, input );
	}

	private static bool TryResolve( SignalPortDescription first, SignalPortDescription second, out SignalOutputDescription output, out SignalInputDescription input )
	{
		output = first as SignalOutputDescription ?? second as SignalOutputDescription;
		input = first as SignalInputDescription ?? second as SignalInputDescription;
		return AreCompatible( output, input );
	}

	[Rpc.Host]
	private static void CreateLinkRpc( Component source, string outputId, Component target, string inputId )
	{
		if ( !source.IsValid() || !target.IsValid() ) return;
		if ( !TryFindOutput( source, outputId, out var output ) || !TryFindInput( target, inputId, out var input ) ) return;
		if ( IsConnectedInternal( source, output, target, input ) ) return;

		var sourceRoot = source.GameObject.Root;
		var targetRoot = target.GameObject.Root;
		if ( !sourceRoot.IsValid() || !targetRoot.IsValid() || sourceRoot == targetRoot ) return;

		// A component input holds one reference — relinking replaces the old wire entirely.
		if ( input.ComponentType is not null )
			DestroyStampedWires( target, input.Id );

		var player = Player.FindForConnection( Rpc.Caller );
		var links = ManualLink.CreatePair( sourceRoot, targetRoot );

		if ( !SetConnectedInternal( source, output, target, input, true, player ) )
		{
			links[0].Destroy();
			return;
		}

		links[0].GetComponent<ManualLink>()?.SetWire( source, outputId, target, inputId );

		SendConnectionNotice( Rpc.Caller, source, output, target, input, true );

		if ( player.IsValid() )
		{
			var undo = player.Undo.Create();
			undo.Name = "Link";
			undo.Add( links[0] );
		}
	}

	[Rpc.Host]
	private static void SetConnectionRpc( Component source, string outputId, Component target, string inputId, bool connected )
	{
		if ( !source.IsValid() || !target.IsValid() ) return;

		// Wires can't leave the contraption.
		var linked = new LinkedGameObjectBuilder();
		linked.AddConnected( source.GameObject );
		if ( !linked.Objects.Contains( target.GameObject.Root ) ) return;

		if ( !TryFindOutput( source, outputId, out var output ) || !TryFindInput( target, inputId, out var input ) ) return;

		// A component input holds one reference — relinking replaces the old wire entirely.
		if ( connected && input.ComponentType is not null )
			DestroyStampedWires( target, input.Id );

		if ( !SetConnectedInternal( source, output, target, input, connected, Player.FindForConnection( Rpc.Caller ) ) ) return;

		if ( connected )
			StampWire( source, outputId, target, inputId );

		SendConnectionNotice( Rpc.Caller, source, output, target, input, connected );
	}

	/// <summary>
	/// Record the wire on the ManualLink pair that carries it, so destroying the link
	/// (undo, the wire tool's unlink) also disconnects the wire. Wires between objects
	/// joined some other way (welds, same root) have no pair — that's fine.
	/// </summary>
	private static void StampWire( Component source, string outputId, Component target, string inputId )
	{
		var targetRoot = target.GameObject.Root;

		foreach ( var link in source.GameObject.Root.GetComponentsInChildren<ManualLink>( true ) )
		{
			if ( link.HasWire || link.Body?.Root != targetRoot ) continue;

			link.SetWire( source, outputId, target, inputId );
			return;
		}
	}

	/// <summary>
	/// Disconnect the wire a dying ManualLink was carrying.
	/// </summary>
	internal static void DisconnectWire( ManualLink link )
	{
		if ( !Networking.IsHost || !link.HasWire ) return;
		if ( !link.SignalTarget.IsValid() ) return;

		// Component wires can let go of a dead source; signal wires need a live one to prune.
		if ( !link.IsComponentWire && !link.SignalSource.IsValid() ) return;

		SetConnectedInternal( link.SignalSource, link.SignalOutputId, link.SignalTarget, link.SignalInputId, false, dying: link );
	}

	private static void SendConnectionNotice( Connection caller, Component source, SignalPort output, Component target, SignalPort input, bool connected )
	{
		// Keep component and port phrase references intact until they reach the recipient.
		var sourceName = Game.TypeLibrary.GetType( source.GetType() )?.Title ?? source.GetType().Name;
		var targetName = Game.TypeLibrary.GetType( target.GetType() )?.Title ?? target.GetType().Name;

		Sandbox.UI.Notices.SendLocalizedNotice(
			caller,
			connected ? "link" : "link_off",
			connected ? Color.Green : Color.Yellow,
			connected ? "#signal.linked" : "#signal.unlinked",
			new()
			{
				{ "source", sourceName },
				{ "output", output.Title },
				{ "target", targetName },
				{ "input", input.Title }
			},
			3f );
	}

	private static bool SetConnectedInternal( Component source, string outputId, Component target, string inputId, bool connected, Player instigator = null, ManualLink dying = null )
	{
		if ( !TryFindOutput( source, outputId, out var output ) ) return false;
		if ( !TryFindInput( target, inputId, out var input ) ) return false;

		return SetConnectedInternal( source, output, target, input, connected, instigator, dying );
	}

	private static bool SetConnectedInternal( Component source, SignalPort output, Component target, SignalPort input, bool connected, Player instigator = null, ManualLink dying = null )
	{
		if ( output.ComponentType is not null || input.ComponentType is not null )
			return SetComponentLinkInternal( source, output, target, input, connected, instigator, dying );

		if ( output.Property.GetValue( source ) is not SignalOutput port ) return false;

		var connections = port.Connections ??= new();
		var wasConnected = HasConnection( port, target, input.Id );
		connections.RemoveAll( connection => connection is null
			|| !connection.Target.IsValid()
			|| (connection.Target == target && connection.Input == input.Id) );

		if ( connected )
			connections.Add( new SignalConnection { Target = target, Input = input.Id } );
		else if ( wasConnected )
			Deliver( target, input, new SignalEvent( 0f, false, false, true, null ) ); // let go of anything held on

		source.GameObject.Network?.Refresh();
		return true;
	}

	private static bool HasConnection( SignalOutput port, Component target, string inputId )
	{
		return port?.Connections?.Any( connection => connection?.Target == target && connection.Input == inputId ) == true;
	}

	/// <summary>
	/// Component links set the input property to the source component directly — there is
	/// no connection list and nothing ever travels; the reference is the whole wire.
	/// Into a boolean input, the reference implicitly reads as a bool instead: on while
	/// anything is wired in, off when the last wire dies.
	/// </summary>
	private static bool SetComponentLinkInternal( Component source, SignalPort output, Component target, SignalPort input, bool connected, Player instigator, ManualLink dying )
	{
		if ( output.ComponentType is null || !target.IsValid() ) return false;

		if ( input.ComponentType is null )
		{
			if ( !IsPresenceBindable( input.Binding ) ) return false;

			if ( connected )
			{
				Deliver( target, input, new SignalEvent( 1f, true, true, false, instigator ) );
			}
			else
			{
				// Presence wires only die with their ManualLink pair — refuse anything else,
				// or the input would stay held with no wire left to release it.
				if ( dying is null ) return false;
				if ( FindPresenceWire( target, input.Id, exclude: dying ) ) return true;

				Deliver( target, input, new SignalEvent( 0f, false, false, true, instigator ) );
			}

			target.GameObject.Network?.Refresh();
			return true;
		}

		if ( input.Binding.Property is not { } property ) return false;

		if ( connected )
		{
			if ( !source.IsValid() || !source.GetType().IsAssignableTo( input.ComponentType ) ) return false;
			property.SetValue( target, source );
		}
		else
		{
			// Only let go if we still hold this source (or a dead one) — a relink may have replaced it.
			if ( property.GetValue( target ) is not Component current ) return false;
			if ( current != source && current.IsValid() ) return false;

			property.SetValue( target, null );
		}

		// The mutated state lives on the target for component links.
		target.GameObject.Network?.Refresh();
		return true;
	}

	/// <summary>
	/// Is there a stamped component wire into this input? Optionally from one specific
	/// source, optionally ignoring a link that's currently being destroyed.
	/// </summary>
	private static bool FindPresenceWire( Component target, string inputId, Component source = null, ManualLink exclude = null )
	{
		foreach ( var link in target.GameObject.Root.GetComponentsInChildren<ManualLink>( true ) )
		{
			if ( link == exclude || !link.HasWire || !link.IsComponentWire ) continue;
			if ( link.SignalTarget != target || !link.SignalSource.IsValid() ) continue;
			if ( source is not null && link.SignalSource != source ) continue;
			if ( !string.Equals( link.SignalInputId, inputId, StringComparison.OrdinalIgnoreCase ) ) continue;

			return true;
		}

		return false;
	}

	private static bool IsConnectedInternal( Component source, SignalPort output, Component target, SignalPort input )
	{
		if ( input.ComponentType is not null )
			return input.Binding.Property?.GetValue( target ) as Component == source;

		if ( output.ComponentType is not null )
			return FindPresenceWire( target, input.Id, source );

		return output.Property?.GetValue( source ) is SignalOutput port && HasConnection( port, target, input.Id );
	}

	/// <summary>
	/// Destroy the stamped ManualLink pairs carrying wires into this input.
	/// </summary>
	private static void DestroyStampedWires( Component target, string inputId )
	{
		foreach ( var link in target.GameObject.Root.GetComponentsInChildren<ManualLink>( true ).ToArray() )
		{
			if ( !link.IsValid() || link.SignalTarget != target ) continue;
			if ( !string.Equals( link.SignalInputId, inputId, StringComparison.OrdinalIgnoreCase ) ) continue;

			link.GameObject.Destroy();
		}
	}

	internal static void Emit( SignalOutput port, SignalEvent value )
	{
		if ( !Networking.IsHost || port?.Connections is null ) return;

		foreach ( var connection in port.Connections )
		{
			if ( connection?.Target.IsValid() != true ) continue;

			if ( !TryFindInput( connection.Target, connection.Input, out var input ) ) continue;

			Deliver( connection.Target, input, value );
		}
	}

	/// <summary>
	/// Every output on this object's contraption (anything joined by welds, joints or links).
	/// Tools and UI should use this so they all agree on what's wireable.
	/// </summary>
	public static IEnumerable<SignalOutputDescription> GetContraptionOutputs( GameObject gameObject )
	{
		return GetContraptionComponents( gameObject ).SelectMany( GetOutputs );
	}

	/// <summary>
	/// Every input on this object's contraption.
	/// </summary>
	public static IEnumerable<SignalInputDescription> GetContraptionInputs( GameObject gameObject )
	{
		return GetContraptionComponents( gameObject ).SelectMany( GetInputs );
	}

	private static IEnumerable<Component> GetContraptionComponents( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) yield break;

		var linked = new LinkedGameObjectBuilder();
		linked.AddConnected( gameObject );

		foreach ( var root in linked.Objects )
		{
			foreach ( var component in root.GetComponentsInChildren<Component>( true ) )
			{
				if ( component.IsValid() ) yield return component;
			}
		}
	}

	private static void Deliver( Component component, SignalPort input, SignalEvent value )
	{
		RememberInstigator( component, value.Instigator );

		try
		{
			using var scope = ControlContext.Push( value.Instigator );
			input.Binding.Invoke( component, value );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Signal input {component}.{input.Id} failed: {exception.Message}" );
		}
	}

	private static IEnumerable<SignalPort> GetOutputPorts( Component component )
	{
		var type = Game.TypeLibrary.GetType( component.GetType() );
		if ( type is null ) yield break;

		if ( type.GetAttribute<SignalOutputAttribute>() is { } classAttribute )
			yield return CreateComponentOutput( type, classAttribute );

		var ids = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var property in type.Properties )
		{
			var attribute = property.GetCustomAttribute<SignalOutputAttribute>();
			if ( attribute is null ) continue;
			if ( !TryCreateOutput( type, property, attribute, out var port ) ) continue;

			if ( !ids.Add( port.Id ) )
			{
				Log.Warning( $"Signal output '{port.Id}' is declared twice on {type.FullName}; the duplicate was ignored." );
				continue;
			}

			yield return port;
		}
	}

	private static IEnumerable<SignalPort> GetInputPorts( Component component )
	{
		var type = Game.TypeLibrary.GetType( component.GetType() );
		if ( type is null ) yield break;

		var ids = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var method in type.Methods )
		{
			var attribute = method.GetCustomAttribute<SignalInputAttribute>();
			if ( attribute is null ) continue;
			if ( !TryCreateInput( type, method, attribute, out var port ) ) continue;
			if ( TryAddInput( type, ids, port ) ) yield return port;
		}

		foreach ( var property in type.Properties )
		{
			var attribute = property.GetCustomAttribute<SignalInputAttribute>();
			if ( attribute is null ) continue;
			if ( !TryCreateInput( type, property, attribute, out var port ) ) continue;
			if ( TryAddInput( type, ids, port ) ) yield return port;
		}
	}

	private static bool TryAddInput( TypeDescription type, HashSet<string> ids, SignalPort port )
	{
		if ( ids.Add( port.Id ) ) return true;

		Log.Warning( $"Signal input '{port.Id}' is declared twice on {type.FullName}; the duplicate was ignored." );
		return false;
	}

	private static bool TryFindOutput( Component component, string id, out SignalPort result )
	{
		result = default;
		var found = false;
		var type = Game.TypeLibrary.GetType( component.GetType() );
		if ( type is null ) return false;

		if ( string.Equals( id, ComponentOutputId, StringComparison.OrdinalIgnoreCase ) )
		{
			if ( type.GetAttribute<SignalOutputAttribute>() is not { } classAttribute ) return false;

			result = CreateComponentOutput( type, classAttribute );
			return true;
		}

		foreach ( var property in type.Properties )
		{
			var attribute = property.GetCustomAttribute<SignalOutputAttribute>();
			if ( attribute is null || !string.Equals( property.Name, id, StringComparison.OrdinalIgnoreCase ) ) continue;
			if ( !TryCreateOutput( type, property, attribute, out var port ) ) continue;
			if ( found )
			{
				Log.Warning( $"Signal output '{id}' is declared twice on {type.FullName}; refusing to choose one." );
				return false;
			}

			result = port;
			found = true;
		}

		return found;
	}

	private static bool TryFindInput( Component component, string id, out SignalPort result )
	{
		result = default;
		var found = false;
		var type = Game.TypeLibrary.GetType( component.GetType() );
		if ( type is null ) return false;

		foreach ( var method in type.Methods )
		{
			var attribute = method.GetCustomAttribute<SignalInputAttribute>();
			var portId = attribute?.Id ?? method.Name;
			if ( attribute is null || !string.Equals( portId, id, StringComparison.OrdinalIgnoreCase ) ) continue;
			if ( !TryCreateInput( type, method, attribute, out var port ) ) continue;
			if ( found ) return WarnDuplicateInput( type, id, out result );

			result = port;
			found = true;
		}

		foreach ( var property in type.Properties )
		{
			var attribute = property.GetCustomAttribute<SignalInputAttribute>();
			var portId = attribute?.Id ?? property.Name;
			if ( attribute is null || !string.Equals( portId, id, StringComparison.OrdinalIgnoreCase ) ) continue;
			if ( !TryCreateInput( type, property, attribute, out var port ) ) continue;
			if ( found ) return WarnDuplicateInput( type, id, out result );

			result = port;
			found = true;
		}

		return found;
	}

	private static bool WarnDuplicateInput( TypeDescription type, string id, out SignalPort result )
	{
		result = default;
		Log.Warning( $"Signal input '{id}' is declared twice on {type.FullName}; refusing to choose one." );
		return false;
	}

	private static SignalPort CreateComponentOutput( TypeDescription type, SignalOutputAttribute attribute )
	{
		// The component itself is the port; it lists ahead of its signal ports.
		return new SignalPort(
			ComponentOutputId,
			attribute.Name ?? type.Title ?? type.Name,
			type.Description,
			type.Icon,
			attribute.Default,
			-1 )
		{ ComponentType = type.TargetType };
	}

	private static bool TryCreateOutput( TypeDescription type, PropertyDescription property, SignalOutputAttribute attribute, out SignalPort port )
	{
		port = default;
		if ( property.PropertyType != typeof( SignalOutput ) || !property.CanRead )
		{
			Log.Warning( $"Signal output {type.FullName}.{property.Name} must be a readable SignalOutput property." );
			return false;
		}

		port = new SignalPort(
			property.Name,
			attribute.Name ?? property.Title ?? property.Name,
			property.Description,
			property.Icon,
			attribute.Default,
			property.Order )
		{ Property = property };
		return true;
	}

	private static bool TryCreateInput( TypeDescription type, MethodDescription method, SignalInputAttribute attribute, out SignalPort port )
	{
		port = default;
		if ( method.ReturnType != typeof( void ) || !TryBindMethod( method, out var binding ) )
		{
			Log.Warning( $"Signal input {type.FullName}.{method.Name} has an unsupported signature. Use no parameters, or one bool / float / SignalEvent parameter, and return void." );
			return false;
		}

		var id = attribute.Id ?? method.Name;
		var declaredPort = type.Properties.FirstOrDefault( property => string.Equals( property.Name, id, StringComparison.OrdinalIgnoreCase ) );
		var description = !string.IsNullOrWhiteSpace( method.Description ) ? method.Description : declaredPort?.Description;
		port = new SignalPort(
			id,
			attribute.Name ?? declaredPort?.Title ?? id,
			description,
			method.Icon ?? declaredPort?.Icon,
			attribute.Default,
			method.Order )
		{ Binding = binding };
		return true;
	}

	private static bool TryCreateInput( TypeDescription type, PropertyDescription property, SignalInputAttribute attribute, out SignalPort port )
	{
		port = default;
		if ( !TryBindProperty( property, attribute, out var binding ) )
		{
			Log.Warning( $"Signal input {type.FullName}.{property.Name} has an unsupported type. Use bool, float, a Component type, or register an ISignalAdapter." );
			return false;
		}

		var id = attribute.Id ?? property.Name;
		port = new SignalPort(
			id,
			attribute.Name ?? property.Title ?? id,
			property.Description,
			property.Icon,
			attribute.Default,
			property.Order )
		{
			Binding = binding,
			ComponentType = binding.Kind == SignalInputKind.ComponentProperty ? property.PropertyType : null
		};
		return true;
	}

	private static bool TryBindMethod( MethodDescription method, out SignalInputBinding binding )
	{
		binding = default;
		var parameters = method.Parameters;

		if ( parameters.Length == 0 )
		{
			binding = new SignalInputBinding( method, null, SignalInputKind.TriggerMethod );
			return true;
		}

		if ( parameters.Length > 1 ) return false;

		var parameterType = parameters[0].ParameterType;

		var kind = parameterType == typeof( SignalEvent ) ? SignalInputKind.EventMethod
			: parameterType == typeof( bool ) ? SignalInputKind.BoolMethod
			: parameterType == typeof( float ) ? SignalInputKind.FloatMethod
			: (SignalInputKind?)null;
		if ( kind is null ) return false;

		binding = new SignalInputBinding( method, null, kind.Value );

		return true;
	}

	private static bool TryBindProperty( PropertyDescription property, SignalInputAttribute attribute, out SignalInputBinding binding )
	{
		binding = default;

		if ( property.PropertyType == typeof( bool ) && property.CanWrite )
		{
			binding = new SignalInputBinding( null, property, SignalInputKind.BoolProperty );
			return true;
		}

		if ( property.PropertyType == typeof( float ) && property.CanWrite )
		{
			binding = new SignalInputBinding( null, property, SignalInputKind.FloatProperty );
			return true;
		}

		if ( property.PropertyType.IsAssignableTo( typeof( Component ) ) && property.CanWrite )
		{
			binding = new SignalInputBinding( null, property, SignalInputKind.ComponentProperty );
			return true;
		}

		if ( FindAdapter( property.PropertyType ) is { } adapter )
		{
			try
			{
				return adapter.TryBind( new SignalInputContext( property, attribute ), out binding );
			}
			catch ( Exception exception )
			{
				Log.Warning( $"Signal adapter for {property.PropertyType.FullName} failed to bind {property.Name}: {exception.Message}" );
			}
		}

		return false;
	}

	private static ISignalAdapter FindAdapter( Type targetType )
	{
		ISignalAdapter result = null;

		foreach ( var description in Game.TypeLibrary.GetTypes<ISignalAdapter>().Where( type => !type.IsAbstract ).OrderBy( type => type.FullName, StringComparer.Ordinal ) )
		{
			var adapter = Game.TypeLibrary.Create<ISignalAdapter>( description.TargetType );
			if ( adapter?.TargetType is null )
			{
				Log.Warning( $"Signal adapter {description.FullName} could not be created. Adapters need a parameterless constructor and a TargetType." );
				continue;
			}

			if ( adapter.TargetType != targetType ) continue;
			if ( result is not null )
			{
				Log.Warning( $"Signal adapters for {targetType.Name} are ambiguous; refusing to choose one." );
				return null;
			}

			result = adapter;
		}

		return result;
	}
}
