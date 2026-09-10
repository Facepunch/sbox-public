namespace Editor.SpriteEditor;

public class AnimationList : Widget
{
	public Window SpriteEditor { get; private set; }

	Layout Content;
	List<AnimationButton> AnimationButtons = new();

	public AnimationList( Window window ) : base( null )
	{
		SpriteEditor = window;

		Name = "Animations";
		WindowTitle = Name;
		SetWindowIcon( "directions_walk" );

		Layout = Layout.Column();

		var scrollArea = new ScrollArea( this );
		scrollArea.Canvas = new Widget();
		scrollArea.Canvas.Layout = Layout.Column();
		scrollArea.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		scrollArea.Canvas.HorizontalSizeMode = SizeMode.Flexible;

		// Empty Content Layout
		Content = Layout.Column();
		Content.Margin = 4;
		Content.AddStretchCell();
		scrollArea.Canvas.Layout.Add( Content );

		// New Animation button
		var row = scrollArea.Canvas.Layout.AddRow();
		row.AddStretchCell();
		row.Margin = 16;
		var button = row.Add( new Button.Primary( "Create New Animation", "add" ) );
		button.MinimumWidth = 300;
		button.Clicked = CreateAnimationPopup;
		row.AddStretchCell();

		scrollArea.Canvas.Layout.AddStretchCell();
		Layout.Add( scrollArea );

		SetSizeMode( SizeMode.Default, SizeMode.Flexible );

		UpdateAnimationList();
		SpriteEditor.OnAssetLoaded += UpdateAnimationList;
		SpriteEditor.OnSpriteModified += UpdateAnimationList;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		SpriteEditor.OnAssetLoaded -= UpdateAnimationList;
		SpriteEditor.OnSpriteModified -= UpdateAnimationList;
	}

	[EditorEvent.Hotload]
	private void UpdateAnimationList()
	{
		Content?.Clear( true );
		AnimationButtons.Clear();

		var animations = SpriteEditor.Sprite?.Animations;
		if ( animations is not null )
		{
			foreach ( var animation in animations )
			{
				var button = Content.Add( new AnimationButton( this, animation ) );
				button.MouseClick = () => SelectAnimation( button );
				AnimationButtons.Add( button );
			}
		}

		// Re-draw the parent to ensure there is no leftover text when removing the last item
		Parent?.Update();
	}
	private void CreateAnimationPopup()
	{
		var popup = new PopupWidget( SpriteEditor );
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 16;
		popup.Layout.Spacing = 8;

		popup.Layout.Add( new Label( $"What would you like to name the animation?" ) );

		var entry = new LineEdit( popup );
		var button = new Button.Primary( "Create" );

		button.MouseClick = () =>
		{
			if ( !string.IsNullOrEmpty( entry.Text ) && !SpriteEditor.Sprite.Animations.Any( a => a.Name.ToLowerInvariant() == entry.Text.ToLowerInvariant() ) )
			{
				CreateAnimation( entry.Text );
				UpdateAnimationList();
			}
			else
			{
				Window.ShowNamingError( entry.Text );
			}
			popup.Visible = false;
		};

		entry.ReturnPressed += button.MouseClick;

		popup.Layout.Add( entry );

		var bottomBar = popup.Layout.AddRow();
		bottomBar.AddStretchCell();
		bottomBar.Add( button );

		popup.Position = Editor.Application.CursorPosition;
		popup.Visible = true;

		entry.Focus();
	}

	private void CreateAnimation( string name )
	{
		if ( SpriteEditor?.Sprite is null )
			return;

		SpriteEditor.ExecuteUndoableAction( $"Create Animation \"{name}\"", () =>
		{
			var animation = new Sprite.Animation();
			animation.Name = name;
			SpriteEditor.Sprite.Animations.Add( animation );
		} );
	}

	private void SelectAnimation( AnimationButton button )
	{
		SpriteEditor.SelectedAnimation = button.Animation;
		SpriteEditor.OnAnimationSelected?.Invoke();
	}
}
