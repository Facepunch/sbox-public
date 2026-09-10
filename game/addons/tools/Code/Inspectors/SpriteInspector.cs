using Editor.Assets;

namespace Editor.Inspectors;

[Inspector( typeof( Sprite ) )]
public class SpriteInspector : InspectorWidget, AssetInspector.IAssetInspector
{
	Sprite Sprite;
	TextureWidget TexturePreview;
	PreviewSprite SpritePreview;
	ExpandGroup AnimationGroup;
	AnimationList Animations;
	Sprite.Animation SelectedAnimation;

	Sprite.AnimationState _animationState = new();
	int _cleanStateHash;

	bool IsSingleFrame => Sprite.Animations.Count == 1 && SelectedAnimation.Frames.Count == 1;

	public SpriteInspector( SerializedObject so ) : base( so )
	{
		Layout = Layout.Row();
		Layout.Margin = 4;
		Layout.Spacing = 4;

		UpdateTarget();
	}

	void PlayAnimation( Sprite.Animation anim )
	{
		if ( anim is null )
			return;

		SelectedAnimation = anim;
		SpritePreview?.SetAnimation( anim );
	}

	void UpdateTarget()
	{
		Sprite = SerializedObject.Targets.FirstOrDefault() as Sprite;

		if ( SelectedAnimation is not null )
		{
			SelectedAnimation = Sprite?.Animations?.FirstOrDefault( x => x.Name == SelectedAnimation.Name );
		}

		RebuildSheet();
	}

	void RebuildSheet()
	{
		Layout.Clear( true );

		var leftWidget = Layout.Add( new Widget() );
		var leftColumn = leftWidget.Layout = Layout.Column();
		TexturePreview = leftColumn.Add( new TextureWidget() );
		TexturePreview.FixedSize = 186f;
		leftColumn.AddStretchCell();
		leftColumn.Margin = 16;
		leftWidget.Visible = false;

		var rightWidget = Layout.Add( new Widget() );
		var rightColumn = rightWidget.Layout = Layout.Column();
		rightWidget.HorizontalSizeMode = SizeMode.Flexible;

		if ( Sprite is not null )
		{
			if ( SelectedAnimation is null && Sprite.Animations.Count > 0 )
			{
				SelectedAnimation = Sprite.Animations[0];
			}

			if ( IsSingleFrame )
			{
				// Show single frame editor if we only have one animation with one frame for easy inline editing
				var controlSheet = new ControlSheet();

				var frameSerialized = SelectedAnimation.Frames[0].GetSerialized();
				var animationSerialized = SelectedAnimation.GetSerialized();

				controlSheet.AddObject( frameSerialized, sp =>
				{
					if ( sp.Name == nameof( Sprite.Frame.Texture ) )
						return true;
					return false;
				} );
				controlSheet.AddObject( animationSerialized, sp =>
				{
					if ( sp.Name == nameof( Sprite.Animation.Name ) || sp.Name == nameof( Sprite.Animation.Frames ) )
						return false;
					return true;
				} );

				// Single Frame editors edit the child objects (frame/animation) directly so we check those properties for changes made
				_cleanStateHash = GetStateHash();
				frameSerialized.OnPropertyChanged += _ => MarkDirtyIfChanged();
				animationSerialized.OnPropertyChanged += _ => MarkDirtyIfChanged();

				rightColumn.Add( controlSheet );
			}
			else
			{
				// Show animation list if we have multiple animations or frames
				AnimationGroup = new ExpandGroup( this );
				AnimationGroup.StateCookieName = $"{nameof( SpriteInspector )}.{nameof( AnimationGroup )}";
				AnimationGroup.Icon = "directions_run";
				AnimationGroup.Title = $"Animations";
				AnimationGroup.HorizontalSizeMode = SizeMode.Flexible;
				AnimationGroup.SetOpenState( true );
				AnimationGroup.MinimumWidth = 250;
				rightColumn.Add( AnimationGroup );

				Animations = new AnimationList( AnimationGroup );
				Animations.ItemSelected = PlayAnimation;
				Animations.SetSprite( Sprite );
				AnimationGroup.SetWidget( Animations );
			}
		}

		rightColumn.AddStretchCell();
	}

	int GetStateHash()
	{
		if ( Sprite is null )
			return 0;

		return Sprite.Serialize().ToJsonString().FastHash();
	}

	void MarkDirtyIfChanged()
	{
		if ( Sprite is null )
			return;

		if ( Sprite.HasUnsavedChanges )
			return;

		var hash = GetStateHash();
		if ( hash == _cleanStateHash )
			return;

		_cleanStateHash = hash;
		Sprite.StateHasChanged();
	}

	protected override void DoLayout()
	{
		if ( Parent?.Name == "StickyPopupCanvas" )
		{
			// If we are in a popup, show the texture preview on the left
			if ( TexturePreview?.Parent is not null )
			{
				TexturePreview.Parent.Visible = true;
			}

			// This is pretty hacky, but its enough to give the popup room to show the texture preview without overthinking it
			if ( Parent?.Parent?.Parent?.Parent is not null )
			{
				Parent.Parent.Parent.Parent.MinimumWidth = IsSingleFrame ? 750 : 500;
				Parent.Parent.Parent.Parent.MinimumHeight = 300;
			}
		}
	}

	[EditorEvent.Frame]
	public void FrameUpdate()
	{
		// Update the texture preview if visible
		if ( TexturePreview.Visible )
		{
			_animationState.TryAdvanceFrame( SelectedAnimation, RealTime.Delta );
			if ( SelectedAnimation is not null && SelectedAnimation.Frames.Count > 0 )
			{
				var frame = SelectedAnimation.Frames[_animationState.CurrentFrameIndex];
				TexturePreview.Texture = frame?.Texture;
			}
		}
	}

	void AssetInspector.IAssetInspector.SetAsset( Asset asset ) { }
	void AssetInspector.IAssetInspector.SetAssetPreview( AssetPreview preview )
	{
		if ( preview is PreviewSprite spritePreview )
		{
			SpritePreview = spritePreview;
		}
	}

	private class AnimationList : Widget
	{
		protected readonly ListView ListView;
		protected List<Sprite.Animation> Items;

		public Action<Sprite.Animation> ItemSelected { get; set; }

		public void SetSprite( Sprite sprite )
		{
			Items = Enumerable.Range( 0, sprite.Animations.Count )
				.Select( x => sprite.Animations[x] )
				.ToList();

			ListView.SetItems( Items );
		}

		public AnimationList( Widget parent ) : base( parent )
		{
			Layout = Layout.Column();
			Layout.Margin = 4;
			Layout.Spacing = 4;

			ListView = new ListView( this )
			{
				ItemSize = new Vector2( 0, 25 ),
				Margin = new( 4, 4, 16, 4 ),
				ItemPaint = PaintAnimationItem,
				ItemContextMenu = ShowItemContext,
				ToggleSelect = true,
				ItemSelected = ( o ) => ItemSelected?.Invoke( o as Sprite.Animation ),
				ItemDeselected = ( o ) => ItemSelected?.Invoke( null ),
			};

			//var filter = new LineEdit( this )
			//{
			//	PlaceholderText = $"Filter {ItemName}s..",
			//	FixedHeight = 25
			//};

			//filter.TextEdited += ( t ) =>
			//{
			//	ListView.SetItems( Items == null || Items.Count == 0 ? null : string.IsNullOrWhiteSpace( t ) ? Items :
			//		Items.Where( x => x.Contains( t, StringComparison.OrdinalIgnoreCase ) ) );
			//};

			//Layout.Add( filter );
			Layout.Add( ListView, 1 );
		}

		private void ShowItemContext( object obj )
		{
			if ( obj is not string name ) return;

			var m = new Menu();

			m.AddOption( "Copy", "content_copy", () =>
			{
				EditorUtility.Clipboard.Copy( name );
			} );

			m.OpenAt( Editor.Application.CursorPosition );
		}

		private void PaintAnimationItem( VirtualWidget v )
		{
			if ( v.Object is not Sprite.Animation anim )
				return;

			var rect = v.Rect;

			Paint.Antialiasing = true;

			var fg = Theme.Text.Darken( 0.2f );

			if ( Paint.HasSelected )
			{
				fg = Theme.Text;
				Paint.ClearPen();
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.5f ) );
				Paint.DrawRect( rect, 2 );
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.4f ) );
			}
			else if ( Paint.HasMouseOver )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.25f ) );
				Paint.DrawRect( rect, 2 );
			}

			var firstFrame = anim.Frames.FirstOrDefault();
			var iconRect = rect.Shrink( 8, 4 );
			iconRect.Width = iconRect.Height;
			if ( firstFrame is not null )
			{
				Paint.Draw( iconRect, Pixmap.FromTexture( firstFrame.Texture ) );
			}

			var textRect = rect.Shrink( 4 );
			textRect.Left = iconRect.Right + 8;

			Paint.SetDefaultFont();
			Paint.SetPen( fg );
			Paint.DrawText( textRect, $"{anim.Name}", TextFlag.LeftCenter );
		}
	}
}
