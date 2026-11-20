using Sandbox.UI;

[Icon( "✨" )]
[Title( "#tool.name.trail" )]
[ClassName( "trail" )]
[Group( "#tool.group.render" )]
public sealed class TrailTool : ToolMode
{
	[Property, ResourceSelect( Extension = "ldef", AllowPackages = true ), Title( "#tool.setting.line" )]
	public string Definition { get; set; } = "entities/trails/basic.ldef";

	[Property, Sync, Title( "#tool.setting.trail_color" )]
	public Color TrailColor { get; set; } = Color.White;

	[Property, Sync, Range( 0.1f, 128.0f ), Title( "#tool.setting.start_width" )]
	public float StartWidth { get; set; } = 4.0f;

	[Property, Sync, Range( 0.0f, 128.0f ), Title( "#tool.setting.end_width" )]
	public float EndWidth { get; set; } = 0.0f;

	[Property, Sync, Range( 0.1f, 10.0f ), Title( "#tool.setting.lifetime" )]
	public float Lifetime { get; set; } = 1.0f;

	[Property, Sync, Title( "#tool.setting.cast_shadows" )]
	public bool CastShadows { get; set; } = false;

	public override string Description => "#tool.hint.trail.description";

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.trail.add", OnAddTrail );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.trail.remove", OnRemoveTrail );
	}

	void OnAddTrail()
	{
		var select = TraceSelect();

		IsValidState = select.IsValid() && !select.IsWorld && !select.IsPlayer;
		if ( !IsValidState ) return;

		var lineDef = ResourceLibrary.Get<LineDefinition>( Definition );
		AddTrail( select.GameObject, TrailColor, StartWidth, EndWidth, Lifetime, CastShadows, lineDef );
		ShootEffects( select );
	}

	void OnRemoveTrail()
	{
		var select = TraceSelect();

		IsValidState = select.IsValid() && !select.IsWorld && !select.IsPlayer;
		if ( !IsValidState ) return;

		RemoveTrail( select.GameObject );
		ShootEffects( select );
	}

	/// <summary>
	/// The trail is a child object of its own rather than a component on the target, so undo (which removes objects)
	/// can take it away again.
	/// </summary>
	const string TrailObjectName = "trail";

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void AddTrail( GameObject go, Color color, float startWidth, float endWidth, float lifetime, bool castShadows, LineDefinition lineDef )
	{
		if ( !go.IsValid() || !Player.IsValid() ) return;
		if ( !FireToolAction( ToolInput.Primary ) ) return;

		var root = go.Network?.RootGameObject ?? go;
		RemoveTrails( root );

		var trailObject = new GameObject( false, TrailObjectName );
		trailObject.Parent = root;
		trailObject.LocalTransform = global::Transform.Zero;

		var trail = trailObject.AddComponent<TrailRenderer>();
		trail.Color = new Gradient( new Gradient.ColorFrame( 0, color ), new Gradient.ColorFrame( 1, color.WithAlpha( 0 ) ) );
		trail.Width = new Curve( new Curve.Frame( 0, startWidth ), new Curve.Frame( 1, endWidth ) );
		trail.LifeTime = lifetime;
		trail.CastShadows = castShadows;
		trail.Face = SceneLineObject.FaceMode.Camera;
		if ( lineDef.IsValid() )
		{
			trail.Opaque = lineDef.Opaque;
			trail.BlendMode = lineDef.BlendMode;
		}

		if ( lineDef.IsValid() && lineDef.Material.IsValid() )
		{
			trail.Texturing = trail.Texturing with
			{
				Material = lineDef.Material,
				WorldSpace = lineDef.WorldSpace,
				UnitsPerTexture = lineDef.UnitsPerTexture
			};
		}

		trailObject.NetworkSpawn( true, null );
		Track( trailObject );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.trail";
		undo.Add( trailObject );

		FirePostToolAction( ToolInput.Primary );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RemoveTrail( GameObject go )
	{
		if ( !go.IsValid() ) return;
		if ( !FireToolAction( ToolInput.Secondary ) ) return;

		RemoveTrails( go.Network?.RootGameObject ?? go );

		FirePostToolAction( ToolInput.Secondary );
	}

	/// <summary>
	/// An object has one trail at a time: remove any this tool put on it.
	/// </summary>
	static void RemoveTrails( GameObject root )
	{
		foreach ( var trail in root.GetComponentsInChildren<TrailRenderer>( true ).ToList() )
		{
			if ( trail.GameObject.Name == TrailObjectName && trail.GameObject.Parent == root )
				trail.GameObject.Destroy();
		}
	}
}
