using Sandbox;

namespace Editor;

/// <summary>
/// Control widget for selecting a layer in the inspector.
/// Used by LayerMember component to assign objects to layers.
/// </summary>
[CustomEditor( typeof( Layer ) )]
public class LayerControlWidget : ControlWidget
{
	public LayerControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Spacing = 4;

		var button = Layout.Add( new Button.Primary( "" ) { Icon = "layers" }, 1 );
		button.FixedHeight = ControlRowHeight;
		button.Clicked = OpenLayerPicker;

		UpdateButton( button );

		// Store button reference for updates
		_button = button;
	}

	private Button _button;

	private void UpdateButton( Button button )
	{
		var layer = SerializedProperty.GetValue<Layer>();
		button.Text = layer?.Name ?? "None";

		if ( layer is not null )
		{
			button.IconColor = layer.Color;
		}
	}

	private void OpenLayerPicker()
	{
		var gameObject = GetGameObject();
		if ( gameObject is null ) return;

		var manager = LayerManager.Get( gameObject.Scene );
		if ( manager is null ) return;

		var popup = new PopupWidget( this );
		popup.Layout = Layout.Column();
		popup.Layout.Spacing = 2;
		popup.Layout.Margin = 8;
		popup.MinimumWidth = 200;

		// "None" option
		AddLayerOption( popup, null, "None", Color.Gray );
		popup.Layout.AddSeparator();

		// All layers
		foreach ( var layer in manager.All )
		{
			AddLayerOption( popup, layer, layer.Name, layer.Color );
		}

		popup.Layout.AddSeparator();

		// Create new layer button
		var createBtn = popup.Layout.Add( new Button( "Create New Layer", "add" ) );
		createBtn.Clicked = () =>
		{
			var newLayer = manager.CreateLayer();
			SetLayer( newLayer );
			popup.Close();
		};

		popup.Position = _button.ScreenPosition + new Vector2( 0, _button.Height );
		popup.Visible = true;
	}

	private void AddLayerOption( PopupWidget popup, Layer layer, string name, Color color )
	{
		var row = popup.Layout.AddRow();
		row.Spacing = 8;

		// Color indicator
		var colorWidget = row.Add( new Widget() );
		colorWidget.FixedSize = new Vector2( 12, 12 );
		colorWidget.OnPaintOverride = () =>
		{
			Paint.SetBrush( color );
			Paint.ClearPen();
			Paint.DrawRect( colorWidget.LocalRect, 2 );
			return true;
		};

		// Layer name button
		var btn = row.Add( new Button( name ), 1 );
		btn.Clicked = () =>
		{
			SetLayer( layer );
			popup.Close();
		};

		var currentLayer = SerializedProperty.GetValue<Layer>();
		if ( ( layer is null && currentLayer is null ) || ( layer is not null && currentLayer?.Id == layer.Id ) )
		{
			btn.Icon = "check";
		}
	}

	private void SetLayer( Layer layer )
	{
		SerializedProperty.SetValue( layer );
		UpdateButton( _button );
	}

	private GameObject GetGameObject()
	{
		var target = SerializedProperty.Parent?.Targets?.FirstOrDefault();
		if ( target is Component component )
		{
			return component.GameObject;
		}
		return null;
	}

	protected override void OnPaint()
	{
		// Update button text in case layer was changed elsewhere
		UpdateButton( _button );
	}
}

/// <summary>
/// Dropdown widget for quickly assigning a layer to selected GameObjects.
/// Can be added to the scene hierarchy toolbar.
/// </summary>
public class LayerAssignmentDropdown : Widget
{
	public LayerAssignmentDropdown() : base( null )
	{
		Layout = Layout.Row();
		Layout.Spacing = 4;

		var icon = Layout.Add( new IconButton( "layers" ) );
		icon.ToolTip = "Assign to Layer";
		icon.OnClick = OpenLayerPicker;
		icon.FixedSize = new Vector2( Theme.RowHeight );
	}

	private void OpenLayerPicker()
	{
		var session = SceneEditorSession.Active;
		if ( session is null ) return;

		var selectedObjects = session.Selection.OfType<GameObject>().ToList();
		if ( selectedObjects.Count == 0 ) return;

		var scene = session.Scene;
		var manager = LayerManager.Get( scene );
		if ( manager is null ) return;

		var menu = new ContextMenu( this );

		// None option
		menu.AddOption( "None", "block", () => AssignToLayer( selectedObjects, null ) );
		menu.AddSeparator();

		// All layers
		foreach ( var layer in manager.All )
		{
			var layerCapture = layer;
			var option = menu.AddOption( layer.Name, "layers", () => AssignToLayer( selectedObjects, layerCapture ) );
			option.IconColor = layer.Color;
		}

		menu.AddSeparator();

		// Create new layer
		menu.AddOption( "Create New Layer...", "add", () =>
		{
			var newLayer = manager.CreateLayer();
			AssignToLayer( selectedObjects, newLayer );
		} );

		menu.OpenAtCursor( false );
	}

	private void AssignToLayer( List<GameObject> objects, Layer layer )
	{
		var session = SceneEditorSession.Active;
		if ( session is null ) return;

		using ( session.UndoScope( "Assign to Layer" ).Push() )
		{
			foreach ( var go in objects )
			{
				var member = go.Components.GetOrCreate<LayerMember>();
				member.Layer = layer;
			}
		}
	}
}
