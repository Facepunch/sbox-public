using Sandbox.UI;

[Icon( "🧱" )]
[Title( "#tool.name.builder" )]
[ClassName( "builder" )]
[Group( "#tool.group.building" )]
public sealed class BuilderTool : ToolMode
{
	const string ShapeGroup = "#tool.setting.group.shape";
	const string MaterialGroup = "#tool.setting.group.material";
	const string PhysicsGroup = "#tool.setting.group.physics";

	[Property, Group( ShapeGroup ), Order( 0 ), Title( "#tool.setting.builder.shape" ), Description( "#tool.setting.builder.shape.description" )]
	public BuilderCatalog.Shape Shape { get; set; } = BuilderCatalog.Shape.Box;

	[Property, Group( ShapeGroup ), Order( 1 ), BuilderParameter( BuilderShapes.Thickness ), Range( 1, 256 ), Step( 1 ), Title( "#tool.setting.builder.thickness" ), Description( "#tool.setting.builder.thickness.description" )]
	public float Thickness { get; set; } = 6;

	[Property, Group( ShapeGroup ), Order( 2 ), Range( BuilderPlacement.MinGrid, BuilderPlacement.MaxGrid ), Step( BuilderPlacement.GridStep ), Title( "#tool.setting.builder.grid" ), Description( "#tool.setting.builder.grid.description" )]
	public int GridSize { get; set; } = 16;

	[Property, Group( ShapeGroup ), Order( 3 ), Title( "#tool.setting.builder.adjacent" ), Description( "#tool.setting.builder.adjacent.description" )]
	public bool SnapToAdjacentLines { get; set; } = true;

	[Property, Group( ShapeGroup ), Order( 4 ), Title( "#tool.setting.builder.symmetry" ), Description( "#tool.setting.builder.symmetry.description" )]
	public bool Symmetrical { get; set; }

	[Property, Group( MaterialGroup ), Order( 10 ), ResourceSelect( Extension = "material", AllowPackages = true, Popup = true ), Title( "#tool.setting.material" )]
	public string MaterialPath { get; set; } = BuilderCatalog.DefaultMaterial;

	[Property, Group( MaterialGroup ), Order( 11 ), ResourceSelect( Extension = "surface", Popup = true ), Title( "#tool.setting.builder.surface" ), Description( "#tool.setting.builder.surface.description" )]
	public string SurfacePath { get; set; } = "surfaces/plastic.surface";

	[Property, Group( MaterialGroup ), Order( 12 ), Title( "#tool.setting.tint" )]
	public Color Tint { get; set; } = Color.White;

	[Property, Group( PhysicsGroup ), Order( 20 ), Title( "#tool.setting.builder.collisions" ), Description( "#tool.setting.builder.collisions.description" )]
	public bool Collisions { get; set; } = true;

	[Property, Group( PhysicsGroup ), Order( 21 ), Title( "#tool.setting.freeze" ), Description( "#tool.setting.builder.frozen.description" )]
	public bool SpawnFrozen { get; set; } = true;

	const int MaxPathLength = 256;

	const float ReachTolerance = 256f;

	const float PreviewRebuildInterval = 0.1f;

	const float MinPlaceInterval = 0.1f;

	const float StickyMargin = 4f;

	RealTimeSince _sinceLastPlace = 1;

	BuilderCatalog.Shape _cachedShape;
	Vector3 _cachedSize;
	BuilderShapeKey _cachedKey;
	readonly float[] _cachedValues = new float[BuilderShapes.MaxParameters];
	bool _cachedValid;
	bool _hasCache;

	BuilderBoxFlow _boxFlow;
	BuilderPathFlow _pathFlow;
	BuilderFlow _drawingFlow;
	BuilderPlane _hoverPlane;
	(BuilderPlane Plane, Vector2 Point, SelectionPoint Hit)? _hoverStart;
	bool _placeThisFrame;
	bool _primaryQueued;
	BuilderFaceRef? _stickyFace;

	SnapGrid _grid;
	BuilderSnapMarkers _markers;
	readonly BuilderAlignment _alignment = new();

	Model _previewModel;
	Vector3 _previewSize;
	BuilderShapeKey _previewKey;
	RealTimeSince _sincePreviewBuilt;
	Material _previewMaterial;

	ToolStatus? _status;
	int _statusGrid;
	string _statusDimensions;
	string _statusHint;
	string _statusLanguage;

	public override string Description => "#tool.hint.builder.description";
	public override string SnapAction => "#tool.hint.builder.offgrid";
	protected override IEnumerable<ToolHint> ExtraHints => Flow.SupportsSymmetry
		? [new( Symmetrical ? "#tool.hint.builder.symmetry_on" : "#tool.hint.builder.symmetry_off", ["Symmetry"] )]
		: [];
	public override bool AbsorbMouseInput => ShapeMenu.IsOpen || (_drawingFlow?.AbsorbsMouse ?? false);

	internal BuilderShapeDefinition Definition => BuilderShapes.Get( Shape ) ?? BuilderShapes.Get( BuilderCatalog.Shape.Box );

	internal bool OffGrid => Input.Down( "snap" );

	internal bool Aligning => SnapToAdjacentLines;

	internal bool KeepProportions => Input.Down( "run" );

	/// <summary>The flow that draws the current shape, whether or not a drawing is in progress.</summary>
	BuilderFlow Flow => Definition.Placement switch
	{
		BuilderPlacementMode.Path => _pathFlow ??= new BuilderPathFlow( this ),
		_ => _boxFlow ??= new BuilderBoxFlow( this )
	};

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => _drawingFlow?.PrimaryHint ?? Flow.StartHint, OnPrimary );
		RegisterAction( ToolInput.Secondary, () => _drawingFlow?.SecondaryHint, OnSecondary );
		RegisterAction( ToolInput.Reload, () => Flow.GetReloadHint( _drawingFlow is not null ), OnReload );
	}

	/// <summary>The radial shape menu, held open on the tool menu button. <see cref="BuilderRadialMenu"/> draws it.</summary>
	public BuilderShapeMenu ShapeMenu { get; } = new();

	public override string MenuAction => "#tool.hint.builder.shapes";

	static bool IsOtherMenuOpen => SpawnMenuHost.IsOpen || ChatBox.IsOpen || Inventory.IsOpen || Input.Down( "score" );

	bool UpdateShapeMenu()
	{
		var menu = ShapeMenu;

		if ( !menu.IsOpen )
		{
			if ( !Input.Pressed( "ToolMenu" ) || IsOtherMenuOpen ) return false;
			menu.Open();
		}

		HideGrid();
		_markers?.Hide();
		_hoverStart = null;
		_primaryQueued = false;

		Inventory.ConsumeMouseWheel();

		if ( IsOtherMenuOpen )
		{
			menu.Close();
			return true;
		}

		var hovered = menu.Hovered;
		menu.Move( Input.AnalogLook );
		if ( menu.Hovered is not null && menu.Hovered != hovered ) Sound.Play( MenuHoverSound );

		if ( Input.Down( "ToolMenu" ) && !Input.Pressed( "attack1" ) ) return true;

		if ( menu.Hovered is { } shape )
		{
			Shape = shape;
			Sound.Play( MenuSelectSound );
		}

		menu.Close();
		return true;
	}

	const string MenuHoverSound = "sounds/ui/ui.hover.sound";

	// The same sound as undoing an action
	const string MenuSelectSound = "sounds/ui/ui.undo.sound";

	protected override void OnDisabled()
	{
		base.OnDisabled();

		ShapeMenu.Close();
		_drawingFlow = null;
		_hoverStart = null;
		_primaryQueued = false;
		_placeThisFrame = false;
		_grid?.Destroy();
		_grid = null;
		_markers?.Destroy();
		_markers = null;
	}

	public override void OnControl()
	{
		if ( UpdateShapeMenu() ) return;

		base.OnControl();

		_previewMaterial ??= Material.Load( "materials/effects/duplicator_override.vmat" );

		if ( _drawingFlow is not null && _drawingFlow != Flow ) _drawingFlow = null;

		if ( Input.Pressed( "Symmetry" ) && Flow.SupportsSymmetry ) Symmetrical = !Symmetrical;

		AdjustGridSize();
		ScrollDrawing();
		_placeThisFrame = true;
	}

	protected override void OnPreRender()
	{
		if ( !_placeThisFrame ) return;
		_placeThisFrame = false;

		_markers ??= new BuilderSnapMarkers();
		_markers.Begin( Scene.SceneWorld );
		_alignment.BeginFrame( Aligning );
		UpdatePlacement();
		if ( _primaryQueued )
		{
			_primaryQueued = false;
			ApplyPrimary();
		}

		foreach ( var guide in _alignment.Guides )
			_markers.AddGuide( guide.From, guide.To, guide.Direction, guide.Extend, Player.EyeTransform.Position );
		_markers.End();
		DrawPreview();
	}

	void AdjustGridSize()
	{
		var wheel = Input.MouseWheel.y;
		if ( !Input.Down( "run" ) || MathF.Abs( wheel ) < 0.001f ) return;

		GridSize = BuilderPlacement.ClampGrid( GridSize + MathF.Sign( wheel ) * BuilderPlacement.GridStep );
		Inventory.ConsumeMouseWheel();
	}

	void ScrollDrawing()
	{
		var wheel = Input.MouseWheel.y;
		if ( _drawingFlow is null || MathF.Abs( wheel ) < 0.001f ) return;

		var steps = (int)MathF.Round( wheel );
		_drawingFlow.Scroll( steps != 0 ? steps : MathF.Sign( wheel ) );

		Inventory.ConsumeMouseWheel();
	}

	void UpdatePlacement()
	{
		var grid = BuilderPlacement.ClampGrid( GridSize );
		var eyes = Player.EyeTransform.ForwardRay;
		_hoverStart = null;

		if ( _drawingFlow is not null )
		{
			_drawingFlow.Update( eyes, grid );
			IsValidState = _drawingFlow.IsValid;
			return;
		}

		if ( !TryResolveStart( eyes, out var plane, out var point, out var hit ) )
		{
			IsValidState = false;
			HideGrid();
			return;
		}

		var start = PlacePoint( plane, point, grid );
		IsValidState = true;
		_hoverPlane = plane;
		_hoverStart = (plane, start, hit);
		DrawSnapPoints( plane );
		ShowGrid( plane, grid, plane.ToWorld( point ), plane.ToWorld( start ) );
	}

	void OnPrimary() => _primaryQueued = true;

	void ApplyPrimary()
	{
		if ( _drawingFlow is null )
		{
			if ( _hoverStart is not { } hover ) return;

			_drawingFlow = Flow;
			_previewModel = null;
			_drawingFlow.Begin( hover.Plane, hover.Point );
			ShootEffects( hover.Hit );
			return;
		}

		if ( !_drawingFlow.Advance() ) return;

		var flow = _drawingFlow;
		_drawingFlow = null;
		Place( flow.Transform.Position, flow.Transform.Rotation, flow.Key.Size, Shape, BuilderShapes.Format( flow.Key ), MaterialPath, SurfacePath, Tint, Collisions, SpawnFrozen );
	}

	void OnSecondary()
	{
		if ( _drawingFlow is not null && !_drawingFlow.Back() ) _drawingFlow = null;
	}

	void OnReload() => Flow.Reload( _hoverPlane, _drawingFlow is not null );

	internal bool TryResolveStart( Ray eyes, out BuilderPlane plane, out Vector2 point, out SelectionPoint hit )
	{
		hit = TraceSelect();
		var found = BuilderPlacement.TryGetPlane( hit, eyes.Forward, out plane, out point, out var face );

		if ( found && face is not null )
		{
			_stickyFace = face;
			return true;
		}

		if ( _stickyFace is { } sticky && BuilderPlacement.TryGetFacePlane( sticky, out var stickyPlane )
			&& Vector3.Dot( eyes.Forward, stickyPlane.Normal ) < 0
			&& BuilderPlacement.TryProject( stickyPlane, eyes, out var onFace ) && stickyPlane.Contains( onFace, StickyMargin ) )
		{
			var facePoint = stickyPlane.ToWorld( onFace );
			var blocked = hit.IsValid() && hit.GameObject.Root != sticky.Object.Root
				&& hit.WorldPosition().Distance( eyes.Position ) < facePoint.Distance( eyes.Position ) - 1f;

			if ( !blocked )
			{
				plane = stickyPlane;
				point = onFace;

				if ( !hit.IsValid() || hit.GameObject.Root != sticky.Object.Root )
					hit = new SelectionPoint { GameObject = sticky.Object, LocalTransform = sticky.Object.WorldTransform.ToLocal( new Transform( facePoint, Rotation.LookAt( stickyPlane.Normal ) ) ) };

				return true;
			}
		}

		_stickyFace = null;
		return found;
	}

	internal void GetSettings( Span<float> values )
	{
		var parameters = Definition.Parameters;
		for ( int i = 0; i < parameters.Length; i++ )
			values[i] = parameters[i].Name == BuilderShapes.Thickness ? Thickness : parameters[i].Default;
	}

	internal bool TryValidate( Vector3 size, ReadOnlySpan<float> values, out BuilderShapeKey key )
	{
		var count = Definition.Parameters.Length;
		if ( !_hasCache || _cachedShape != Shape || _cachedSize != size || !values[..count].SequenceEqual( _cachedValues.AsSpan( 0, count ) ) )
		{
			_hasCache = true;
			_cachedShape = Shape;
			_cachedSize = size;
			values[..count].CopyTo( _cachedValues );
			_cachedValid = BuilderShapeKey.TryCustom( Shape, size, values, out _cachedKey );
		}

		key = _cachedKey;
		return _cachedValid;
	}

	internal void DrawSnapPoints( in BuilderPlane plane )
	{
		if ( !plane.HasFace || OffGrid ) return;

		_markers?.Add( plane, Player.EyeTransform.Position );
	}

	internal Vector2 PlacePoint( in BuilderPlane plane, Vector2 raw, int grid )
	{
		var snapped = OffGrid ? raw : plane.Snap( raw, grid );
		if ( !Aligning || Scene.Camera is not { } camera ) return snapped;

		return _alignment.AlignPoint( Scene, camera, plane, raw, snapped );
	}

	internal Vector2 PlaceProportional( in BuilderPlane plane, Vector2 start, Vector2 raw, Vector2 ratio, int grid )
	{
		var snapped = BuilderPlacement.Proportional( start, raw, ratio, OffGrid ? 0 : grid );
		if ( !Aligning || Scene.Camera is not { } camera ) return snapped;

		var free = BuilderPlacement.Proportional( start, raw, ratio, 0 );
		var direction = free - start;
		if ( direction.Length < 0.01f ) return snapped;

		return _alignment.AlignOnRay( Scene, camera, plane, start, direction.Normal, free, snapped );
	}

	internal bool TryAlignHeight( Vector3 center, Vector3 normal, out float height )
	{
		height = 0;
		return Aligning && Scene.Camera is { } camera && _alignment.TryAlignHeight( Scene, camera, center, normal, out height );
	}

	internal void ShowGrid( in BuilderPlane plane, int grid, Vector3 aim, Vector3 snapped, Vector3? cursor = null )
	{
		if ( OffGrid )
		{
			HideGrid();
			_markers?.AddCursor( cursor ?? snapped, plane.Right, plane.Forward, Player.EyeTransform.Position );
			return;
		}

		_grid ??= new SnapGrid();
		_grid.UpdatePlane( Scene.SceneWorld, plane.Origin, plane.Right, plane.Forward, plane.Normal, grid, aim, snapped );
	}

	internal void HideGrid() => _grid?.Hide();

	void DrawPreview()
	{
		var flow = _drawingFlow;
		if ( flow is null || !flow.HasBox ) return;

		if ( !flow.IsValid )
		{
			Model model;
			Vector3 size;
			if ( _previewModel is not null && _previewKey.Shape == Shape && (!flow.StretchesPreview || PreviewHasCurrentParameters()) )
			{
				model = _previewModel;
				size = _previewSize;
			}
			else if ( !flow.TryGetFallbackPreview( out model, out size ) )
			{
				return;
			}

			var box = flow.Transform;
			if ( flow.StretchesPreview )
			{
				box = box.WithScale( flow.Size / size );
				box.Position -= box.Rotation * (model.Bounds.Center * box.Scale);
			}

			DebugOverlay.Model( model, color: Color.Red, transform: box, castShadows: false, materialOveride: _previewMaterial );
			return;
		}

		var rebuild = _previewKey != flow.Key && (!flow.StretchesPreview || _sincePreviewBuilt > PreviewRebuildInterval);
		if ( _previewModel is null || _previewKey.Shape != Shape || rebuild )
		{
			_previewModel = BuilderModelCache.TryGet( flow.Key, out var cached ) ? cached : BuilderGeometry.Create( flow.Key, collision: false );
			_previewKey = flow.Key;
			_previewSize = flow.Key.Size;
			_sincePreviewBuilt = 0;
		}

		if ( _previewModel is null ) return;

		var transform = flow.StretchesPreview ? flow.Transform.WithScale( flow.Size / _previewSize ) : flow.Transform;
		DebugOverlay.Model( _previewModel, transform: transform, castShadows: false, materialOveride: _previewMaterial );
	}

	bool PreviewHasCurrentParameters()
	{
		Span<float> values = stackalloc float[BuilderShapes.MaxParameters];
		GetSettings( values );

		var parameters = Definition.Parameters;
		for ( int i = 0; i < parameters.Length; i++ )
		{
			if ( MathF.Abs( _previewKey.Parameter( i ) - values[i] ) > 0.5f ) return false;
		}

		return true;
	}

	public override void DrawHud( Painter painter, Vector2 crosshair )
	{
		// The shape menu covers the middle of the screen
		if ( ShapeMenu.IsOpen ) return;

		base.DrawHud( painter, crosshair );
	}

	/// <summary>
	/// The grid size, the drawn block's dimensions once it can be built, and the drawing's hint. Rebuilt only when one
	/// of them changes.
	/// </summary>
	public override ToolStatus? Status
	{
		get
		{
			var grid = OffGrid ? 0 : BuilderPlacement.ClampGrid( GridSize );
			var dimensions = _drawingFlow is { IsValid: true } ? _drawingFlow.Dimensions : null;
			var hint = _drawingFlow?.HudHint;
			var language = Game.Language.SelectedCode;

			if ( _status is null || grid != _statusGrid || !ReferenceEquals( dimensions, _statusDimensions ) || hint != _statusHint || language != _statusLanguage )
			{
				_statusGrid = grid;
				_statusDimensions = dimensions;
				_statusHint = hint;
				_statusLanguage = language;

				var gridText = grid == 0
					? Game.Language.GetPhrase( "tool.hint.builder.hud_offgrid" )
					: Game.Language.GetPhrase( "tool.hint.builder.hud_grid", new() { { "size", grid.ToString() } } );

				_status = new ToolStatus( gridText, dimensions, LocalizedText.Resolve( hint ) );
			}

			return _status;
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Place( Vector3 position, Rotation rotation, Vector3 size, BuilderCatalog.Shape shape, string parameters, string materialPath, string surfacePath, Color tint, bool collisions, bool frozen )
	{
		var player = Player;
		if ( !player.IsValid() ) return;
		var owner = player.Network.Owner;

		if ( _sinceLastPlace < MinPlaceInterval ) return;
		_sinceLastPlace = 0;

		if ( !BuilderPolicy.CheckCanBuild( owner ) ) return;
		if ( LimitsSystem.Get( Scene ) is { } limits && !limits.CheckBuilderLimit( owner, 1 ) ) return;

		if ( !BuilderPlacement.IsFinite( position ) || !IsUsable( rotation ) )
		{
			Reject( owner, "invalid transform" );
			return;
		}

		if ( !BuilderShapeKey.TryCustom( shape, size, parameters, out var key ) )
		{
			Reject( owner, $"invalid {shape} recipe" );
			return;
		}

		var transform = new Transform( position, rotation.Normal );
		var bounds = BuilderShapes.Get( shape ).GetBounds( key );
		var reach = BuilderPlacement.Reach + bounds.Size.Length * 0.5f + ReachTolerance;
		if ( transform.PointToWorld( bounds.Center ).Distance( player.EyeTransform.Position ) > reach )
		{
			Reject( owner, "out of reach" );
			return;
		}

		if ( materialPath?.Length > MaxPathLength || surfacePath?.Length > MaxPathLength )
		{
			Reject( owner, "path too long" );
			return;
		}

		var surface = string.IsNullOrWhiteSpace( surfacePath ) ? null : ResourceLibrary.Get<Surface>( surfacePath );
		if ( !FireToolAction( ToolInput.Primary ) ) return;

		var material = BuilderMaterials.LoadLocal( materialPath );
		var go = BuilderProp.Create( key, null, transform, material, surface, collisions, frozen, owner, pendingMaterial: material is null ? materialPath : null, tint: ClampTint( tint ) );
		Track( go );

		var undo = player.Undo.Create();
		undo.Name = "#tool.undo.builder";
		undo.Icon = "🧱";
		undo.Add( go );

		PlaceEffects( transform );
		FirePostToolAction( ToolInput.Primary );
	}

	static Color ClampTint( Color tint )
	{
		static float Channel( float value ) => float.IsFinite( value ) ? Math.Clamp( value, 0f, 1f ) : 1f;
		return new Color( Channel( tint.r ), Channel( tint.g ), Channel( tint.b ), Channel( tint.a ) );
	}

	static bool IsUsable( Rotation rotation )
	{
		var length = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
		return float.IsFinite( length ) && length > 0.25f;
	}

	static void Reject( Connection owner, string reason )
	{
		Log.Warning( $"Builder: refused a block from {owner?.DisplayName}: {reason}" );
		if ( owner is not null ) Notices.SendLocalizedNotice( owner, "block", Color.Red, "#builder.refused", null, 3 );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlaceEffects( Transform target ) => Toolgun?.PlayToolEffects( target );
}
