namespace Editor.SpriteEditor;

public class Inspector : Widget
{
	public Window SpriteEditor { get; private set; }

	ControlSheet controlSheet;

	public Inspector( Window window ) : base( null )
	{
		SpriteEditor = window;

		Name = "Inspector";
		WindowTitle = Name;
		SetWindowIcon( "manage_search" );

		Layout = Layout.Column();
		controlSheet = new ControlSheet();

		MinimumWidth = 350f;

		var scroller = new ScrollArea( this );
		scroller.Canvas = new Widget();
		scroller.Canvas.Layout = Layout.Column();
		scroller.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		scroller.Canvas.HorizontalSizeMode = SizeMode.Flexible;

		scroller.Canvas.Layout.Add( controlSheet );
		scroller.Canvas.Layout.AddStretchCell();

		Layout.Add( scroller );

		SetSizeMode( SizeMode.Default, SizeMode.Flexible );

		UpdateControlSheet();
		SpriteEditor.OnAssetLoaded += UpdateControlSheet;
		SpriteEditor.OnAnimationSelected += UpdateControlSheet;
		SpriteEditor.OnSpriteModified += UpdateControlSheet;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		DetachSerializedObject();

		SpriteEditor.OnAssetLoaded -= UpdateControlSheet;
		SpriteEditor.OnAnimationSelected -= UpdateControlSheet;
		SpriteEditor.OnSpriteModified -= UpdateControlSheet;
	}

	SerializedObject _serializedObject;
	SerializedObject.PropertyChangedDelegate _propertyChangedHandler;

	// Controls can still finish async work (texture generators) and write to their old serialized object, so stop listening before replacing it.
	void DetachSerializedObject()
	{
		if ( _serializedObject is not null && _propertyChangedHandler is not null )
		{
			_serializedObject.OnPropertyChanged -= _propertyChangedHandler;
		}

		_serializedObject = null;
		_propertyChangedHandler = null;
	}

	private void UpdateControlSheet()
	{
		if ( SpriteEditor?.SelectedAnimation is null ) return;

		DetachSerializedObject();
		controlSheet?.Clear( true );

		var serializedObject = SpriteEditor.SelectedAnimation.GetSerialized();
		controlSheet.AddObject( serializedObject, ( prop ) => prop.Name != nameof( Sprite.Animation.Name ) );

		var oldestSerialized = SpriteEditor.Sprite.Serialize();
		var lastStateHash = oldestSerialized.ToJsonString().FastHash();
		_serializedObject = serializedObject;
		_propertyChangedHandler = ( prop ) =>
		{
			if ( prop is null ) return;

			var serializedSprite = SpriteEditor.Sprite.Serialize();
			var stateHash = serializedSprite.ToJsonString().FastHash();
			if ( stateHash == lastStateHash ) return;
			lastStateHash = stateHash;

			var undoName = $"Modify {prop.Name}";
			if ( SpriteEditor.UndoStack.Back.Count > 0 )
			{
				var lastUndo = SpriteEditor.UndoStack.Back.Peek();
				if ( lastUndo?.Name == undoName )
				{
					lastUndo = SpriteEditor.UndoStack.Back.Pop();
					SpriteEditor.UndoStack.Insert( undoName, lastUndo.Undo, () =>
					{
						SpriteEditor.Sprite.Deserialize( serializedSprite );
						SpriteEditor.OnSpriteModified?.Invoke();
					} );
				}
				else
				{
					SpriteEditor.UndoStack.Insert( undoName, lastUndo.Redo, () =>
					{
						SpriteEditor.Sprite.Deserialize( serializedSprite );
						SpriteEditor.OnSpriteModified?.Invoke();
					} );
				}
			}
			else
			{
				SpriteEditor.UndoStack.Insert( undoName, () =>
				{
					SpriteEditor.Sprite.Deserialize( oldestSerialized );
					SpriteEditor.OnSpriteModified?.Invoke();
				}, () =>
				{
					SpriteEditor.Sprite.Deserialize( serializedSprite );
					SpriteEditor.OnSpriteModified?.Invoke();
				} );
			}

			// Invoke when frames/textures have changed
			if ( prop.Name.ToInt( -1 ) != -1 || prop.Name == nameof( Sprite.Animation.Frames ) || prop.Name == nameof( Sprite.Frame.Texture ) )
			{
				SpriteEditor.OnFramesChanged?.Invoke();
			}

			SpriteEditor?.SetModified();
		};

		serializedObject.OnPropertyChanged += _propertyChangedHandler;
	}
}
