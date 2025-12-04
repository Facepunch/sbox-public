using System.Linq;
using System.Text;

namespace Editor;

class GameObjectHeader : Widget
{
	public SerializedObject Target { get; }

	public GameObjectHeader( Widget parent, SerializedObject targetObject ) : base( parent )
	{
		Target = targetObject;

		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.CanShrink;

		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		//var networkModeControl = new NetworkModeControlWidget( targetObject );

		// top section
		{
			var topRow = Layout.AddRow();
			topRow.Spacing = 4;
			topRow.Margin = 8;

			// big icon left
			{
				var left = topRow.AddRow();
				left.Add( new GameObjectIconButton( this ) );
			}


			// 2 rows right
			{
				var right = topRow.AddColumn();
				right.Spacing = 2;

				{
					var top = right.AddRow();
					top.Spacing = 4;
					top.Add( new GameObjectEnabledWidget( targetObject.GetProperty( nameof( GameObject.Enabled ) ) ) );
					var s = top.Add( ControlWidget.Create( targetObject.GetProperty( nameof( GameObject.Name ) ) ), 1 );
					s.HorizontalSizeMode = SizeMode.Flexible;
					top.Add( new GameObjectFlagsWidget( targetObject ) );
				}

				{
					var bottom = right.AddRow();
					bottom.Spacing = 4;
					//bottom.Add( networkModeControl );
					// TODO: Remove these
					//bottom.Add( new BoolControlWidget( targetObject.GetProperty( nameof( GameObject.NetworkInterpolation ) ) ) { Icon = "linear_scale" } );
					//bottom.Add( new AdvancedNetworkControlWidget( targetObject ) );
					bottom.Add( new NetworkModeControlWidget( targetObject ) );
					var s = bottom.Add( ControlWidget.Create( targetObject.GetProperty( nameof( GameObject.Tags ) ) ), 1 );
					s.HorizontalSizeMode = SizeMode.Flexible;
				}
			}
		}
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground );
		Paint.DrawRect( LocalRect );
	}
}

/// <summary>
/// Draggable icon.
/// </summary>
file sealed class GameObjectIconButton : IconButton
{
	private readonly GameObjectHeader _parent;

	private Drag _drag;

	public GameObjectIconButton( GameObjectHeader parent )
		: base( GetCurrentIcon( parent.Target ), null )
	{
		_parent = parent;

		FixedHeight = Theme.RowHeight * 2;
		FixedWidth = Theme.RowHeight * 2;
		IconSize = 27;
		Background = Color.Transparent;

		// Use custom color for the button foreground if one is set on the GameObject
		Foreground = GetCurrentColor( parent.Target );

		IsDraggable = !parent.Target.IsMultipleTargets;
	}

	private static string GetCurrentIcon( SerializedObject target )
	{
		var go = target.Targets.OfType<GameObject>().FirstOrDefault();
		if ( go is null ) return "folder";

		// Check for persistent icon tag first (saved with the scene)
		var iconTag = go.Tags.FirstOrDefault( t => t.StartsWith( "icon_" ) );
		if ( iconTag is not null )
		{
			var decoded = Editor.IconTagEncoding.DecodeIconFromTag( iconTag );
			if ( !string.IsNullOrEmpty( decoded ) )
				return decoded;
		}

		// Fallback to session-only storage
		if ( CustomIconStorage.Icons.TryGetValue( go, out var customIcon ) )
		{
			return customIcon;
		}

		// Default icon based on children and components
		return go.Children.Where( x => x.ShouldShowInHierarchy() ).Any() ? "📂" : (go.Components.Count > 0 ? "layers" : "📁");
	}

	private static Color GetCurrentColor( SerializedObject target )
	{
		var go = target.Targets.OfType<GameObject>().FirstOrDefault();
		if ( go is null ) return Color.White;

		var colorTag = go.Tags.FirstOrDefault( t => t.StartsWith( "icon_color_" ) );
		if ( colorTag is not null )
		{
			var hex = colorTag.Substring( 11 ); // Remove "icon_color_"
			if ( Color.TryParse( $"#{hex}", out var color ) )
			{
				return color;
			}
		}

		return Color.White;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.Button == MouseButtons.Left )
		{
			OnIconClicked();
			e.Accepted = true;
		}

		base.OnMousePress( e );
	}

	private void OnIconClicked()
	{
		var go = _parent.Target.Targets.OfType<GameObject>().FirstOrDefault();
		if ( go is null ) return;

		var currentIcon = GetCurrentIcon( _parent.Target );
		var currentColor = GetCurrentColor( _parent.Target );

		IconColorPicker.OpenPopup( this, currentIcon, currentColor, ( selectedIcon, selectedColor ) =>
		{
			// Prepare new tag values
			var hasChildren = go.Children.Where( x => x.ShouldShowInHierarchy() ).Any();
			var defaultIcon = hasChildren ? "folder_open" : (go.Components.Count > 0 ? "layers" : "folder");
			string newIconTag = selectedIcon != defaultIcon ? IconTagEncoding.EncodeIconToTag( selectedIcon ) : null;
			string newColorTag = null;
			if ( selectedColor != Color.White )
			{
				newColorTag = $"icon_color_{((int)(selectedColor.r * 255)):X2}{((int)(selectedColor.g * 255)):X2}{((int)(selectedColor.b * 255)):X2}{((int)(selectedColor.a * 255)):X2}";
			}

			// Apply to all selected targets to avoid inconsistent state
			var targets = _parent.Target.Targets.OfType<GameObject>().ToArray();
			foreach ( var targetGo in targets )
			{
				// Icon (store by Id)
				if ( selectedIcon == defaultIcon )
				{
					CustomIconStorage.Icons.Remove( targetGo );
				}
				else
				{
					CustomIconStorage.Icons[targetGo] = selectedIcon;
				}

				// Color tag (keep using tags for color)
				var existingColorTag = targetGo.Tags.FirstOrDefault( t => t.StartsWith( "icon_color_" ) );
				if ( existingColorTag is not null )
				{
					if ( newColorTag is null || existingColorTag != newColorTag )
						targetGo.Tags.Remove( existingColorTag );
				}
				if ( newColorTag is not null && !targetGo.Tags.Contains( newColorTag ) )
				{
					targetGo.Tags.Add( newColorTag );
				}

				// Icon tag (persisted with the scene)
				var existingIconTag = targetGo.Tags.FirstOrDefault( t => t.StartsWith( "icon_" ) );
				if ( existingIconTag is not null )
				{
					if ( newIconTag is null || existingIconTag != newIconTag )
						targetGo.Tags.Remove( existingIconTag );
				}
				if ( newIconTag is not null && !targetGo.Tags.Contains( newIconTag ) )
				{
					targetGo.Tags.Add( newIconTag );
				}
			}

			// Mark the tree item as dirty so it will update its rendering
			if ( SceneTreeWidget.Current?.TreeView is { } tv )
			{
				foreach ( var t in targets )
				{
					tv.Dirty( t );
				}
				tv.UpdateIfDirty();
			}

			// Update the button icon and foreground color
			Icon = selectedIcon;
			Foreground = selectedColor;
			Update();

			// Update the scene tree to reflect the change
			SceneTreeWidget.Current?.TreeView?.Update();
		} );
	}

	protected override void OnDragStart()
	{
		base.OnDragStart();

		var target = _parent.Target.Targets.OfType<GameObject>().FirstOrDefault();

		if ( target is null ) return;

		_drag = new Drag( this )
		{
			Data = { Object = target, Text = target.Name }
		};

		_drag.Execute();
	}

	protected override void OnPaint()
	{
		Background = _drag.IsValid() ? Theme.Pink.WithAlpha( 0.6f ) : Color.Transparent;

		base.OnPaint();

		if ( _drag.IsValid() )
		{
			Update();
		}
	}
}

file sealed class GameObjectEnabledWidget : BoolControlWidget
{
	public GameObjectEnabledWidget( SerializedProperty property )
		: base( property )
	{
		Icon = "power_settings_new";
		Tint = Theme.Green;

		IsDraggable = !property.Parent.IsMultipleTargets;
	}

	protected override void OnDragStart()
	{
		base.OnDragStart();

		var drag = new Drag( this )
		{
			Data = { Object = SerializedProperty, Text = SerializedProperty.As.String }
		};

		drag.Execute();
	}
}

/// <summary>
/// Custom popup for selecting icon and color.
/// </summary>
file static class IconColorPicker
{
	public static void OpenPopup( Widget parent, string currentIcon, Color currentColor, Action<string, Color> onSelected )
	{
		var popup = new PopupWidget( parent );
		popup.Visible = false;
		popup.FixedWidth = 300;
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 8;
		popup.Layout.Spacing = 8;

		// Icon Picker
		var iconPicker = popup.Layout.Add( new IconPickerWidget( popup ), 1 );
		iconPicker.Icon = currentIcon;

		// Color Picker
		var colorPicker = popup.Layout.Add( new ColorPicker( popup ) );
		colorPicker.Value = currentColor;

		// Live update when icon or color changes
		iconPicker.ValueChanged = ( v ) =>
		{
			onSelected?.Invoke( v, colorPicker.Value );
		};

		colorPicker.ValueChanged = ( c ) =>
		{
			onSelected?.Invoke( iconPicker.Icon, c );
		};

		// Buttons
		var buttonRow = popup.Layout.AddRow();
		buttonRow.Spacing = 4;

		var cancelButton = buttonRow.Add( new Button( "Cancel" ) );
		cancelButton.Clicked += () => popup.Destroy();

		var okButton = buttonRow.Add( new Button.Primary( "OK" ) );
		okButton.Clicked += () =>
		{
			onSelected?.Invoke( iconPicker.Icon, colorPicker.Value );
			popup.Destroy();
		};

		popup.OpenAtCursor();
	}
}
