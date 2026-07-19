using System.Text.Json.Nodes;

namespace Editor;

[CustomEmbeddedEditor( typeof( Sprite ) )]
public class EmbeddedSpriteControlWidget : EmbeddedResourceControlWidget
{
	/// <summary>
	/// Whether or not we are displaying an advanced version of the control
	/// </summary>
	public bool AdvancedMode => IsAdvanced( Sprite );

	static bool IsAdvanced( Sprite sprite ) => (sprite?.Animations?.Count ?? 0) > 1
										   || (sprite?.Animations?.FirstOrDefault()?.Frames?.Count ?? 0) > 1;

	/// <summary>
	/// An easy accessor for the first texture in the sprite, great for embedded sprites
	/// </summary>
	Texture Texture
	{
		get
		{
			//
			// Never write from here. This is read while the inspector is being built, and again on a
			// timer for as long as it stays open. On a multi-selection SerializedProperty.SetValue
			// writes to every selected object, so a write here would copy the first selection's
			// sprite on to all of the others without anyone touching anything.
			//
			return Sprite?.Animations?.FirstOrDefault()?.Frames?.FirstOrDefault()?.Texture;
		}
		set
		{
			PropertyStartEdit();

			//
			// Update each selected object's own sprite. SerializedProperty.SetValue would write one
			// instance to all of them, permanently aliasing every selected SpriteRenderer on to
			// whichever sprite happened to be first in the selection.
			//
			foreach ( var property in SerializedProperty.MultipleProperties )
			{
				var sprite = property.GetValue<Sprite>( null );

				if ( sprite is null || IsAdvanced( sprite ) )
				{
					// Create a new sprite if we don't have one, or if we're in advanced mode to avoid setting texture of pre-existing sprite
					sprite = new Sprite();
				}

				EnsureFirstFrame( sprite ); // Ensure frame/animation exists
				sprite.Animations[0].Frames[0].Texture = value;
				property.SetValue( sprite );
			}

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
				SerializedProperty.SetValue( sprite );
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

	/// <summary>
	/// Make sure the sprite has an animation and a frame for us to put a texture in. Mutates the
	/// sprite in place - the caller is responsible for writing it back to its own property.
	/// </summary>
	private static void EnsureFirstFrame( Sprite sprite )
	{
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
		}
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
