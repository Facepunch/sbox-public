namespace Editor.SpriteEditor;

public class SpritesheetRenderingWidget : SceneRenderingWidget
{
	private readonly SpritesheetImporter _importer;
	private SpriteRenderer _textureRenderer;

	public Vector2 TextureSize { get; private set; }
	private float _planeWidth;
	private float _planeHeight;
	private float _startX, _startY, _frameWidth, _frameHeight, _xSep, _ySep;

	private bool _isDragging;
	private bool _isRightDrag;

	public SpritesheetRenderingWidget( SpritesheetImporter importer, Widget parent ) : base( parent )
	{
		_importer = importer;

		Scene = Scene.CreateEditorScene();
		using ( Scene.Push() )
		{
			var cameraObj = new GameObject( true, "Camera" );
			Camera = cameraObj.Components.Create<CameraComponent>();
			Camera.Orthographic = true;
			Camera.OrthographicHeight = 100;
			Camera.WorldRotation = new Angles( 90, 180, 0 );
			Camera.WorldPosition = new Vector3( 0, 0, 115 );
			Camera.BackgroundColor = Theme.SurfaceBackground;

			var ambientLight = cameraObj.Components.Create<AmbientLight>();
			ambientLight.Color = Color.White;

			var textureObj = new GameObject( true, "Texture" );
			textureObj.Flags = textureObj.Flags.WithFlag( GameObjectFlags.EditorOnly, true );
			_textureRenderer = textureObj.Components.Create<SpriteRenderer>();
			_textureRenderer.WorldPosition = Vector3.Zero;
			_textureRenderer.Size = 100;
			_textureRenderer.IsSorted = true;

			var backgroundObject = new GameObject( true, "background" );
			var background = backgroundObject.Components.Create<SpriteRenderer>();
			background.WorldPosition = new Vector3( 0, 0, -1 );
			background.Size = 100;
			background.IsSorted = true;
			background.Color = Theme.ControlBackground.Darken( 0.9f );
			background.Sprite = new Sprite()
			{
				Animations = new()
				{
					new Sprite.Animation()
					{
						Name = "background",
						Frames = new()
						{
							new Sprite.Frame()
							{
								Texture = Texture.White
							}
						}
					}
				}
			};
		}
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		Scene?.Destroy();
	}

	public void SetTexture( Texture texture )
	{
		if ( texture is null ) return;

		TextureSize = new Vector2( texture.Width, texture.Height );

		_textureRenderer.Sprite = new Sprite()
		{
			Animations = new()
			{
				new Sprite.Animation()
				{
					Name = "sheet",
					Frames = new() { new Sprite.Frame() { Texture = texture } }
				}
			}
		};
		_textureRenderer.PlayAnimation( "sheet" );

		ComputePlaneDimensions();
	}

	private void ComputePlaneDimensions()
	{
		if ( TextureSize.y == 0 ) return;
		var ar = TextureSize.x / TextureSize.y;
		if ( ar >= 1f )
		{
			_planeWidth = 100f;
			_planeHeight = 100f / ar;
		}
		else
		{
			_planeWidth = 100f * ar;
			_planeHeight = 100f;
		}
	}

	protected override void PreFrame()
	{
		_textureRenderer.TextureFilter = _importer.Antialiasing switch
		{
			0 => Sandbox.Rendering.FilterMode.Point,
			1 => Sandbox.Rendering.FilterMode.Bilinear,
			3 => Sandbox.Rendering.FilterMode.Anisotropic,
			_ => Sandbox.Rendering.FilterMode.Trilinear
		};

		Scene.EditorTick( RealTime.Now, RealTime.Delta );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( TextureSize.x == 0 || TextureSize.y == 0 ) return;

		var s = _importer.Settings;

		// Clamp frame counts to valid range
		s.HorizontalFrames = Math.Max( 1, s.HorizontalFrames );
		s.VerticalFrames = Math.Max( 1, s.VerticalFrames );

		float availPixelW = TextureSize.x - s.PaddingLeft - s.PaddingRight;
		float availPixelH = TextureSize.y - s.PaddingTop - s.PaddingBottom;
		float pixelFW = Math.Max( 1f, (availPixelW - (s.HorizontalFrames - 1) * s.HorizontalSeparation) / s.HorizontalFrames );
		float pixelFH = Math.Max( 1f, (availPixelH - (s.VerticalFrames - 1) * s.VerticalSeparation) / s.VerticalFrames );

		_frameWidth = pixelFW / TextureSize.x * _planeWidth;
		_frameHeight = pixelFH / TextureSize.y * _planeHeight;
		_xSep = s.HorizontalSeparation / TextureSize.x * _planeWidth;
		_ySep = s.VerticalSeparation / TextureSize.y * _planeHeight;
		_startX = (s.PaddingLeft / TextureSize.x * _planeWidth) - (_planeWidth / 2f);
		_startY = (s.PaddingTop / TextureSize.y * _planeHeight) - (_planeHeight / 2f);

		using ( GizmoInstance.Push() )
		{
			using ( Gizmo.Scope( "import_settings" ) )
			{
				for ( int row = 0; row < s.VerticalFrames; row++ )
				{
					for ( int col = 0; col < s.HorizontalFrames; col++ )
					{
						float x = _startX + col * (_frameWidth + _xSep);
						float y = _startY + row * (_frameHeight + _ySep);

						// White outer grid outline
						Gizmo.Draw.Color = Color.White;
						Gizmo.Draw.LineThickness = 2f;
						DrawBox( x, y, _frameWidth, _frameHeight );

						// Selected: yellow inner outline + frame index
						int selIdx = _importer.GetSelectionIndex( new Vector2Int( col, row ) );
						if ( selIdx >= 0 )
						{
							float inset = Math.Min( _frameWidth, _frameHeight ) * 0.06f;
							Gizmo.Draw.Color = Color.Yellow;
							Gizmo.Draw.LineThickness = 2f;
							DrawBox( x + inset, y + inset, _frameWidth - inset * 2, _frameHeight - inset * 2 );

							Gizmo.Draw.Color = Color.Yellow;
							Gizmo.Draw.WorldText( $"{selIdx + 1}", new Transform( new Vector3( y + inset * 3, x + inset * 3, 0 ), new Angles( 0, 90, 0 ), Math.Min( _frameWidth, _frameHeight ) / 128f ), "Poppins", 32f );
						}
					}
				}
			}
		}

		UpdateInputs();
	}

	private Vector2Int? GetCellAt()
	{
		if ( TextureSize.x == 0 || Camera is null ) return null;

		var cursorPos = Application.CursorPosition - ScreenPosition;
		var ray = Camera.ScreenPixelToRay( cursorPos );

		if ( MathF.Abs( ray.Forward.z ) < 0.0001f ) return null;
		float t = -ray.Position.z / ray.Forward.z;
		var worldPos = ray.Position + ray.Forward * t;

		// DrawBox(x,y,w,h) places corners at Vector3(y,x,0) so worldPos.y = col, worldPos.x = row
		float stepCol = _frameWidth + _xSep;
		float stepRow = _frameHeight + _ySep;
		if ( stepCol <= 0 || stepRow <= 0 ) return null;

		float relCol = (worldPos.y - _startX) / stepCol;
		float relRow = (worldPos.x - _startY) / stepRow;

		int col = (int)MathF.Floor( relCol );
		int row = (int)MathF.Floor( relRow );

		if ( _xSep > 0 && (relCol - col) > _frameWidth / stepCol ) return null;
		if ( _ySep > 0 && (relRow - row) > _frameHeight / stepRow ) return null;

		var s = _importer.Settings;
		if ( col < 0 || col >= s.HorizontalFrames || row < 0 || row >= s.VerticalFrames ) return null;

		return new Vector2Int( col, row );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		var cell = GetCellAt();
		if ( cell is null ) return;

		_isDragging = true;
		_isRightDrag = e.RightMouseButton;

		if ( _isRightDrag )
		{
			_importer.DeselectCell( cell.Value );
		}
		else
		{
			if ( !_importer.IsSelected( cell.Value ) )
				_importer.SelectCell( cell.Value );
		}

		_importer.UpdateImportButton();
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		if ( !_isDragging ) return;

		var cell = GetCellAt();
		if ( cell is null ) return;

		if ( _isRightDrag )
		{
			_importer.DeselectCell( cell.Value );
		}
		else
		{
			if ( !_importer.IsSelected( cell.Value ) )
				_importer.SelectCell( cell.Value );
		}

		_importer.UpdateImportButton();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );
		_isDragging = false;
	}

	private void DrawBox( float x, float y, float width, float height )
	{
		Gizmo.Draw.Line( new Vector3( y, x, 0 ), new Vector3( y, x + width, 0 ) );
		Gizmo.Draw.Line( new Vector3( y, x, 0 ), new Vector3( y + height, x, 0 ) );
		Gizmo.Draw.Line( new Vector3( y + height, x, 0 ), new Vector3( y + height, x + width, 0 ) );
		Gizmo.Draw.Line( new Vector3( y + height, x + width, 0 ), new Vector3( y, x + width, 0 ) );
	}

	private void UpdateInputs()
	{
		Camera.CustomSize = Size;

		GizmoInstance.Input.IsHovered = IsUnderMouse;
		GizmoInstance.Input.Modifiers = Application.KeyboardModifiers;
		GizmoInstance.Input.CursorPosition = Application.CursorPosition;
		GizmoInstance.Input.LeftMouse = Application.MouseButtons.HasFlag( MouseButtons.Left );
		GizmoInstance.Input.RightMouse = Application.MouseButtons.HasFlag( MouseButtons.Right );

		GizmoInstance.Input.CursorPosition -= ScreenPosition;
		GizmoInstance.Input.CursorRay = Camera.ScreenPixelToRay( GizmoInstance.Input.CursorPosition );

		if ( !GizmoInstance.Input.IsHovered )
		{
			GizmoInstance.Input.LeftMouse = false;
			GizmoInstance.Input.RightMouse = false;
		}
	}
}
