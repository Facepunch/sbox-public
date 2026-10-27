using System.Text.Json.Nodes;

namespace Editor;

[CustomEmbeddedEditor( typeof( Sprite ) )]
public class EmbeddedSpriteControlWidget : EmbeddedResourceControlWidget
{
	/// <summary>
	/// Whether or not we are displaying an advanced version of the control
	/// </summary>
	public bool AdvancedMode => (Sprite?.Animations?.Count ?? 0) > 1
							 || (Sprite?.Animations?.FirstOrDefault()?.Frames?.Count ?? 0) > 1;

	/// <summary>
	/// An easy accessor for the first texture in the sprite, great for embedded sprites
	/// </summary>
	Texture Texture
	{
		get
		{
			var sprite = Sprite;
			CheckForSpriteTexture( sprite ); // Ensure frame/animation exists
			return sprite.Animations[0].Frames[0].Texture;
		}
		set
		{
			PropertyStartEdit();
			var sprite = Sprite;
			if ( sprite == null || AdvancedMode )
			{
				// Create a new sprite if we don't have one, or if we're in advanced mode to avoid setting texture of pre-existing sprite
				sprite = CreateEmbeddedSprite();
			}
			CheckForSpriteTexture( sprite ); // Ensure frame/animation exists
			sprite.Animations[0].Frames[0].Texture = value;
			SerializedProperty.SetValue( sprite );
			PropertyFinishEdit();
		}
	}

	Sprite Sprite => SerializedProperty.GetValue<Sprite>( null );
	Widget ContentWidget;

	public EmbeddedSpriteControlWidget( SerializedProperty property ) : base( property )
	{
		ContentWidget = new Widget( this );
		ContentWidget.Layout = Layout.Row();
		ContentWidget.Layout.Spacing = 4;
		ContentWidget.Visible = false;

		AcceptDrops = true;

		var so = this.GetSerialized();
		var texControlWidget = new Widget();
		texControlWidget.Layout = Layout.Row();
		var texControl = ControlSheetRow.CreateEditor( so.GetProperty( nameof( this.Texture ) ) );
		texControlWidget.Layout.Add( texControl );
		ContentWidget.Layout.Add( texControlWidget );
		texControlWidget.OnPaintOverride = () =>
		{
			var rect = texControlWidget.LocalRect;
			Paint.SetBrushAndPen( Theme.ControlBackground, Color.Transparent, 0 );
			Paint.DrawRect( rect, 2 );

			bool active = IsControlActive;
			bool hovered = IsControlHovered;

			if ( hovered && IsBeingDroppedOn )
			{
				Paint.SetPen( ControlHighlightSecondary.WithAlpha( 0.8f ), 2, PenStyle.Dot );
				Paint.SetBrush( ControlHighlightSecondary.WithAlpha( 0.2f ) );
				Paint.DrawRect( rect.Shrink( 2 ), Theme.ControlRadius );
			}
			return false;
		};

		var btnAdvanced = new IconButton( "edit_note" );
		btnAdvanced.Background = Theme.ControlBackground;
		btnAdvanced.Foreground = Theme.Text;
		btnAdvanced.IconSize = 16;
		btnAdvanced.Name = "dropdown";
		btnAdvanced.ToolTip = "Open Advanced Settings";
		btnAdvanced.OnClick = () =>
		{
			if ( _popup.IsValid() )
			{
				_popup?.Destroy();
				_popup = null;
				return;
			}

			OpenPopupEditor();
		};
		ContentWidget.Layout.Add( btnAdvanced );

		System.HashCode.Combine( Texture, AdvancedMode );
	}

	public override void OnDragHover( DragEvent ev )
	{
		ev.Action = DropAction.Move;
		base.OnDragHover( ev );
	}

	public override void OnDragDrop( DragEvent ev )
	{
		var asset = AssetSystem.FindByPath( ev.Data.FileOrFolder );

		if ( asset is not null )
		{
			var so = this.GetSerialized();
			var prop = so.GetProperty( nameof( this.Texture ) );
			var didDrag = false;

			if ( asset.AssetType == AssetType.ImageFile )
			{
				var texture = Texture.Load( asset.RelativePath );
				texture.EmbeddedResource = new()
				{
					ResourceCompiler = "texture",
					ResourceGenerator = "imagefile",
					Data = new JsonObject()
					{
						["FilePath"] = asset.RelativePath
					}
				};
				prop.SetValue( texture );
				didDrag = true;
			}
			if ( asset.AssetType == AssetType.Texture )
			{
				prop.SetValue( asset.LoadResource<Texture>() );
				didDrag = true;
			}
			if ( asset.TryLoadResource<Sprite>( out var sprite ) )
			{
				// Embed a copy, the asset's instance is shared with everything else that references it
				PropertyStartEdit();
				CreateEmbeddedFromFile( SerializedProperty, sprite );
				PropertyFinishEdit();
				didDrag = true;
			}

			if ( didDrag )
			{
				if ( Parent is ResourceWrapperControlWidget rwcw )
				{
					rwcw.RebuildControl();
				}
				return;
			}
		}

		base.OnDragDrop( ev );
	}

	protected override void OnPaint()
	{
		if ( !AdvancedMode )
			return;

		base.OnPaint();
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		ContentWidget.Visible = !AdvancedMode;
		if ( ContentWidget.Visible )
		{
			CleanseWidgets( ContentWidget );
			ContentWidget.Width = Width;
			ContentWidget.Height = Height;
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( !AdvancedMode )
			return;

		base.OnMouseReleased( e );
	}

	private void CheckForSpriteTexture( Sprite sprite )
	{
		if ( sprite is null )
		{
			sprite = CreateEmbeddedSprite();
			SerializedProperty.SetValue( sprite );
		}

		// Ensure first animation exists
		if ( (sprite.Animations?.Count ?? 0) == 0 )
		{
			sprite.Animations = [new Sprite.Animation()];
		}
		// Ensure first frame exists
		var anim = sprite.Animations[0];
		if ( (anim.Frames?.Count ?? 0) == 0 )
		{
			anim.Frames = [new Sprite.Frame()];
			SerializedProperty.SetValue( sprite );
		}
	}

	/// <summary>
	/// A new sprite marked as embedded, so it's deep copied on clone and saved inline
	/// </summary>
	static Sprite CreateEmbeddedSprite()
	{
		return new Sprite { EmbeddedResource = new() { ResourceCompiler = "embed" } };
	}

	// Disable any IconButtons and disable drop events
	void CleanseWidgets( Widget widget )
	{
		foreach ( var child in widget.Children )
		{
			if ( child is TextureWidget )
			{
				child.Visible = false;
				continue;
			}

			if ( child is IconButton && child is not IconButton.WithCornerIcon && child.Name != "dropdown" )
			{
				child.Visible = false;
				continue;
			}

			if ( child is ControlWidget cw )
			{
				cw.PaintBackground = false;
			}

			child.AcceptDrops = false;
			CleanseWidgets( child );
		}
	}

	private RealTimeSince _debounce = 0;
	private int _lastHash;

	[EditorEvent.Frame]
	void OnFrame()
	{
		if ( _debounce < 1 )
			return;

		var currentHash = System.HashCode.Combine( Texture, AdvancedMode );
		if ( currentHash != _lastHash )
		{
			Update();
			DoLayout();
		}
		_lastHash = currentHash;

		_debounce = Random.Shared.Float();
	}
}
