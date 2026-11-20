public abstract partial class ToolMode : Component, IToolInfo
{
	public Toolgun Toolgun => GetComponent<Toolgun>();
	public Player Player => GetComponentInParent<Player>();

	/// <summary>
	/// The mode should set this true or false in OnControl to indicate if the current state is valid for performing actions.
	/// </summary>
	public bool IsValidState { get; protected set; } = true;

	/// <summary>
	/// When true, the toolgun will absorb mouse input so the camera doesn't move.
	/// The mode can then read <see cref="Input.AnalogLook"/> to use the mouse for rotation etc.
	/// </summary>
	public virtual bool AbsorbMouseInput => false;

	/// <summary>
	/// Display name for the tool, defaults to the TypeDescription title.
	/// </summary>
	public virtual string Name => Game.Language.GetPhrase( (TypeDescription?.Title ?? GetType().Name).TrimStart( '#' ) );

	/// <summary>
	/// Description of what this tool does.
	/// </summary>
	public virtual string Description => string.Empty;

	/// <summary>
	/// Label for the primary action (attack1), or null if none.
	/// Auto-populated from registered <see cref="ToolActionEntry"/> when not overridden.
	/// </summary>
	public virtual string PrimaryAction => GetActionName( ToolInput.Primary );

	/// <summary>
	/// Label for the secondary action (attack2), or null if none.
	/// Auto-populated from registered <see cref="ToolActionEntry"/> when not overridden.
	/// </summary>
	public virtual string SecondaryAction => GetActionName( ToolInput.Secondary );

	/// <summary>
	/// Label for the reload action, or null if none.
	/// Auto-populated from registered <see cref="ToolActionEntry"/> when not overridden.
	/// </summary>
	public virtual string ReloadAction => GetActionName( ToolInput.Reload );

	/// <summary>
	/// Label for reload and secondary held together, or null if none.
	/// </summary>
	public virtual string ReloadSecondaryAction => null;

	/// <summary>
	/// Label for the tool menu button (ToolMenu, the middle mouse button), or null if the tool has no menu.
	/// </summary>
	public virtual string MenuAction => null;

	/// <summary>
	/// The tool's live readouts, shown under its hints in the tool panel, or null for none.
	/// </summary>
	public virtual ToolStatus? Status => null;

	public IEnumerable<ToolHint> Hints
	{
		get
		{
			if ( !string.IsNullOrEmpty( PrimaryAction ) ) yield return new( PrimaryAction, ["attack1"] );
			if ( !string.IsNullOrEmpty( SecondaryAction ) ) yield return new( SecondaryAction, ["attack2"] );
			if ( !string.IsNullOrEmpty( ReloadAction ) ) yield return new( ReloadAction, ["reload"] );
			if ( !string.IsNullOrEmpty( ReloadSecondaryAction ) ) yield return new( ReloadSecondaryAction, ["reload", "attack2"] );
			if ( !string.IsNullOrEmpty( SnapAction ) ) yield return new( SnapAction, ["snap"] );
			if ( !string.IsNullOrEmpty( MenuAction ) ) yield return new( MenuAction, ["ToolMenu"] );

			foreach ( var hint in ExtraHints ) yield return hint;
		}
	}

	protected virtual IEnumerable<ToolHint> ExtraHints => [];

	/// <summary>
	/// Tags that TraceSelect will ignore. Override per-tool to filter out specific objects.
	/// Defaults to "player" so tools cannot target players.
	/// </summary>
	public virtual IEnumerable<string> TraceIgnoreTags => ["player"];

	/// <summary>
	/// When true, TraceSelect will also hit hitboxes.
	/// </summary>
	public virtual bool TraceHitboxes => false;

	/// <summary>
	/// Reflection info for this tool's type. Lazy so it's valid before the component starts.
	/// </summary>
	public TypeDescription TypeDescription => _typeDescription ??= TypeLibrary.GetType( GetType() );
	private TypeDescription _typeDescription;

	private readonly List<ToolActionEntry> _actions = new();
	private readonly List<GameObject> _createdObjects = new();

	/// <summary>
	/// Register a tool action that will be dispatched automatically by the base <see cref="OnControl"/>.
	/// The display name is a lambda so it can vary with tool state (e.g. stage-dependent hints).
	/// </summary>
	protected void RegisterAction( ToolInput input, Func<string> name, Action callback, InputMode mode = InputMode.Pressed )
	{
		if ( IsProxy ) return;

		_actions.Add( new ToolActionEntry( input, name, callback, mode ) );
	}

	/// <summary>
	/// Track a GameObject created by this tool action on the host. These are passed through
	/// to <see cref="IToolActionEvents.PostActionData.CreatedObjects"/> when the post-event fires.
	/// </summary>
	protected void Track( params GameObject[] objects )
	{
		foreach ( var go in objects )
		{
			if ( go.IsValid() )
				_createdObjects.Add( go );
		}
	}

	/// <summary>
	/// Returns the display name for the first registered action matching <paramref name="input"/>, or null.
	/// </summary>
	private string GetActionName( ToolInput input )
	{
		foreach ( var action in _actions )
		{
			if ( action.Input == input )
				return action.Name?.Invoke();
		}

		return null;
	}

	/// <summary>
	/// Fire <see cref="IToolActionEvents.OnToolAction"/> at the top of the host RPC that carries out an action,
	/// so listeners can refuse it where it counts. Returns true if the action should proceed, false if cancelled.
	/// </summary>
	protected bool FireToolAction( ToolInput input )
	{
		_createdObjects.Clear();

		var data = new IToolActionEvents.ActionData
		{
			Tool = this,
			Input = input,
			Player = Player?.Network.Owner
		};

		Scene.RunEvent<IToolActionEvents>( x => x.OnToolAction( data ) );
		return !data.Cancelled;
	}

	/// <summary>
	/// Fire <see cref="IToolActionEvents.OnPostToolAction"/> at the end of the host RPC once the action has succeeded.
	/// Passes a snapshot of tracked objects, then clears the list.
	/// </summary>
	protected void FirePostToolAction( ToolInput input )
	{
		var objects = _createdObjects.Count > 0 ? new List<GameObject>( _createdObjects ) : null;
		_createdObjects.Clear();

		Scene.RunEvent<IToolActionEvents>( x => x.OnPostToolAction( new IToolActionEvents.PostActionData
		{
			Tool = this,
			Input = input,
			Player = Player?.Network.Owner,
			CreatedObjects = objects
		} ) );
	}

	/// <summary>
	/// Check registered actions and invoke any whose input condition is met this frame.
	/// The host RPCs the callbacks call fire the <see cref="IToolActionEvents"/> pre/post events.
	/// </summary>
	private void DispatchActions()
	{
		foreach ( var action in _actions )
		{
			var inputName = action.InputAction;
			if ( inputName is null ) continue;

			bool active = action.Mode == InputMode.Down
				? Input.Down( inputName )
				: Input.Pressed( inputName );

			if ( active )
			{
				action.Callback?.Invoke();
			}
		}
	}

	protected override void OnEnabled()
	{
		if ( Network.IsOwner )
		{
			this.LoadCookies();
		}
	}

	protected override void OnDisabled()
	{
		DisableSnapGrid();

		if ( Network.IsOwner )
		{
			this.SaveCookies();
		}
	}

	/// <summary>
	/// Scrolls the tool name across the viewmodel screen's dot matrix in <see cref="Toolgun.ScreenColor"/>.
	/// </summary>
	public virtual void DrawScreen( Rect rect, Painter paint )
	{
		using var state = paint.Scope();
		const float scrollSpeed = 5f;

		// these settings are set for the 'at01' font specifically, you need to adjust these values if you are changing the font
		const float nameSize = 16f;
		const float capHeight = 7f;
		const float capTop = 4.375f;

		var color = Toolgun.IsValid() ? Toolgun.ScreenColor : Color.White;
		var title = Game.Language.GetPhrase( TypeDescription.Title.TrimStart( '#' ) ).ToUpperInvariant();

		paint.TextStyle = new TextStyle( "at01", nameSize, color ) { Alignment = TextFlag.LeftTop | TextFlag.SingleLine };
		var textSize = paint.MeasureText( title );

		var top = rect.Top + MathF.Floor( (rect.Height - capHeight) * 0.5f ) - capTop;

		paint.Translate( 0, top - MathF.Floor( top ) );
		top = MathF.Floor( top );

		var cycle = MathF.Ceiling( textSize.x + rect.Width * 0.5f );
		var x = MathF.Round( rect.Right - (Time.Now * scrollSpeed) % cycle );

		for ( ; x > rect.Left - textSize.x; x -= cycle )
			paint.Text( title, new Rect( x, top, textSize.x, textSize.y ) );
	}

	/// <summary>
	/// Draws a crosshair showing whether the current tool action is valid.
	/// </summary>
	public virtual void DrawHud( Painter painter, Vector2 crosshair )
	{
		using var state = painter.Scope();
		painter.BlendMode = BlendMode.Normal;

		Color invalidColor = "#e53";
		painter.Fill = IsValidState ? Color.White : invalidColor;
		painter.Stroke = Stroke.Solid( IsValidState ? Color.Black : invalidColor.Darken( 0.3f ), 1f )
			.WithAlignment( Stroke.StrokeAlignment.Outside );
		painter.Circle( crosshair, 1.5f );
	}

	/// <summary>
	/// Called on the host after placing an entity or constraint. Fires an RPC to the owning
	/// client so it can walk the contraption graph and record achievement stats locally.
	/// </summary>
	[Rpc.Owner]
	protected void CheckContraptionStats( GameObject anchor )
	{
		var builder = new LinkedGameObjectBuilder();
		builder.AddConnected( anchor );

		var wheels = builder.Objects.Sum( o => o.GetComponentsInChildren<WheelEntity>().Count() );
		var thrusters = builder.Objects.Sum( o => o.GetComponentsInChildren<ThrusterEntity>().Count() );
		var hoverballs = builder.Objects.Sum( o => o.GetComponentsInChildren<HoverballEntity>().Count() );
		var constraints = builder.Objects.Sum( o => o.GetComponentsInChildren<ConstraintCleanup>().Count() );
		var chairs = builder.Objects.Sum( o => o.GetComponentsInChildren<BaseChair>().Count() );

		Sandbox.Services.Stats.Increment( "tool.constraint.create", 1 );
		Sandbox.Services.Stats.SetValue( "tool.contraption.wheel", wheels );
		Sandbox.Services.Stats.SetValue( "tool.contraption.thruster", thrusters );
		Sandbox.Services.Stats.SetValue( "tool.contraption.hoverball", hoverballs );
		Sandbox.Services.Stats.SetValue( "tool.contraption.constraint", constraints );
		Sandbox.Services.Stats.SetValue( "tool.contraption.chair", chairs );
	}
}


/// <summary>
/// Not shown in the tool list, seperate from <see cref="HideAttribute"/> because that's a much more general thing.
/// </summary>
public class HiddenToolMode : Attribute { }
