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

		SpriteEditor.OnAssetLoaded -= UpdateControlSheet;
		SpriteEditor.OnAnimationSelected -= UpdateControlSheet;
		SpriteEditor.OnSpriteModified -= UpdateControlSheet;
	}

	private void UpdateControlSheet()
	{
		if ( SpriteEditor?.SelectedAnimation is null ) return;

		controlSheet?.Clear( true );

		var serializedObject = SpriteEditor.SelectedAnimation.GetSerialized();
		controlSheet.AddObject( serializedObject, ( prop ) => prop.Name != nameof( Sprite.Animation.Name ) );

		var oldestSerialized = SpriteEditor.Sprite.Serialize();
		serializedObject.OnPropertyChanged += ( prop ) =>
		{
			if ( prop is null ) return;

			var undoName = $"Modify {prop.Name}";
			var serializedSprite = SpriteEditor.Sprite.Serialize();
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
	}
}
