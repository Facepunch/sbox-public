using Facepunch.ActionGraphs;
using System;
using System.Reflection;

namespace Editor;

/// <summary>
/// A control widget is used to edit the value of a single SerializedProperty.
/// </summary>
public abstract class ControlWidget : Widget
{
	static Logger log = new Logger( "ControlWidget" );

	public static Color ControlHighlightPrimary = "#77BBFF";
	public static Color ControlHighlightSecondary = "#B0E24D";

	public SerializedProperty SerializedProperty { get; private set; }

	/// <summary>
	/// If none, when in a grid, the control will fill the entire cell
	/// </summary>
	public virtual TextFlag CellAlignment => TextFlag.None;

	public ControlWidget( SerializedProperty property ) : this()
	{
		ArgumentNullException.ThrowIfNull( property, "SerializedProperty" );

		SerializedProperty = property;
		ToolTip = property.Description ?? property.DisplayName;
		MinimumWidth = 200;

		//
		// This actually makes no real sense. Think of these scenarios
		//
		// -- float value { get; } // yes - don't edit
		// -- Model value { get; } // yes - don't edit
		// -- DataClass value { get; } // class is editable, but not the property
		//
		// I think this should be more explicit, only readonly if there's a [readonly] property
		//
		if ( !property.IsEditable && property.PropertyType.IsValueType || property.HasAttribute<ReadOnlyAttribute>() )
			base.ReadOnly = true;

		if ( property.TryGetAttribute<TintAttribute>( out var tintAttribute ) )
		{
			Tint = Theme.GetTint( tintAttribute.Tint );
		}
	}

	internal ControlWidget() : base( null )
	{
		HorizontalSizeMode = SizeMode.CanShrink;
		VerticalSizeMode = SizeMode.CanGrow;
	}

	/// <summary>
	/// Selects this widget and starts editing. Used when we want to focus on the widget in the
	/// inspector, like when double-clicking on something in a graph editor that maps to this widget.
	/// </summary>
	public virtual void StartEditing()
	{
		Focus();
	}

	/// <summary>
	/// If true we prefer to be full inspector width
	/// with the label above us
	/// </summary>
	public virtual bool IsWideMode => false;

	/// <summary>
	/// If true (default) we'll include a label next to the control
	/// </summary>
	public virtual bool IncludeLabel => true;
	public virtual bool IsControlActive => IsFocused;
	public virtual bool IsControlHovered => IsUnderMouse && Enabled && !ReadOnly;
	public virtual bool IsControlDisabled => !Enabled || ReadOnly;
	public virtual bool IsControlButton => false;
	public Color Tint { get; set; } = Color.White;
	public virtual bool SupportsMultiEdit => false;
	public bool PaintBackground = true;

	protected override Vector2 MinimumSizeHint()
	{
		return Theme.RowHeight;
	}

	protected override Vector2 SizeHint()
	{
		var size = base.SizeHint();
		size.x = 1000;
		return size;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		PaintUnder();
		PaintControl();
		PaintOver();
	}

	protected virtual void PaintUnder()
	{
		if ( !PaintBackground )
			return;

		bool active = IsControlActive;
		bool hovered = IsControlHovered;
		bool read = IsControlDisabled;

		Paint.ClearPen();

		if ( IsControlButton )
		{
			if ( hovered )
			{
				Paint.SetPen( Color.Lerp( Theme.ControlBackground, ControlHighlightPrimary, 0.6f ), 1 );
				Paint.SetBrush( Color.Lerp( Theme.ControlBackground, ControlHighlightPrimary, 0.2f ) );
				Paint.DrawRect( LocalRect.Shrink( 1 ), Theme.ControlRadius );
				return;
			}

			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( LocalRect, Theme.ControlRadius );
			return;
		}

		if ( read )
		{
			Paint.SetBrush( Theme.ControlBackground.Lighten( 0.5f ) );
		}
		else if ( active )
		{
			Paint.SetBrush( Theme.ControlBackground.Darken( 0.3f ) );
		}
		else if ( hovered )
		{
			Paint.SetBrush( Theme.ControlBackground.Darken( 0.2f ) );
		}
		else
		{
			Paint.SetBrush( Theme.ControlBackground );
		}

		Paint.DrawRect( LocalRect, Theme.ControlRadius );
	}

	protected virtual void PaintControl()
	{

	}

	protected virtual void PaintOver()
	{
		bool active = IsControlActive;
		bool hovered = IsControlHovered;

		if ( hovered && IsBeingDroppedOn )
		{
			Paint.SetPen( ControlHighlightSecondary.WithAlpha( 0.8f ), 2, PenStyle.Dot );
			Paint.SetBrush( ControlHighlightSecondary.WithAlpha( 0.2f ) );
			Paint.DrawRect( LocalRect.Shrink( 2 ), Theme.ControlRadius );
			return;
		}
	}

	[EditorEvent.Hotload]
	static void FlushCache()
	{
		editorAttributes = null;
	}

	static (TypeDescription Type, CustomEditorAttribute Attribute)[] editorAttributes;

	public static ControlWidget Create( SerializedProperty property )
	{
		if ( property is null )
		{
			return new InvalidPropertyControlWidget();
		}

		ArgumentNullException.ThrowIfNull( property );

		var type = property.PropertyType;

		log.Trace( $"Target Type: {type}" );

		editorAttributes ??= EditorTypeLibrary.GetTypesWithAttribute<CustomEditorAttribute>( false )
					.Where( x => x.Type.TargetType.IsAssignableTo( typeof( ControlWidget ) ) )
					.ToArray();

		var allEditors = editorAttributes
							.Select( x => new { score = x.Attribute.GetEditorScore( property ), editor = x } )
							.Where( x => x.score > 0 )
							.OrderByDescending( x => x.score )
							.ToArray();

		// debug output
		int i = 0;
		foreach ( var entry in allEditors )
		{
			log.Trace( $" {++i}. [{entry.score}]\t{entry.editor.Type.FullName}" );
		}

		// Use the first editor we can successfully create
		foreach ( var entry in allEditors )
		{
			var c = entry.editor.Type.Create<ControlWidget>( new[] { property } );
			if ( c is not null )
			{
				if ( property.IsMultipleValues && !c.SupportsMultiEdit )
				{
					c.Destroy();
					return new MultiEditNotSupported( property );
				}

				c.Prime();
				return c;
			}
		}

		// Nope - sorry, nothing for you
		if ( property.IsMethod )
			return null;

		var w = TryCreateGenericObjectControlWidget( property );
		if ( w is not null ) return w;

		return new MissingSerializedPropertyWidget( property );
	}

	public static ControlWidget TryCreateGenericObjectControlWidget( SerializedProperty property )
	{
		//
		// Is this appropriate for the GenericControlWidget?
		//

		// primitive, nope
		if ( property.PropertyType.IsPrimitive ) return null;

		// readonly struct
		if ( property.PropertyType.IsValueType && property.PropertyType.GetCustomAttribute<System.Runtime.CompilerServices.IsReadOnlyAttribute>() is not null ) return null;


		var foundType = EditorTypeLibrary.GetType<ControlWidget>( "GenericControlWidget" );
		if ( foundType is null ) return default;

		var w = foundType.Create<ControlWidget>( new[] { property } );

		return w;
	}

	protected virtual int ValueHash => HashCode.Combine( this, SerializedProperty?.GetValue<object>() );

	[EditorEvent.Frame]
	public virtual void Think()
	{
		if ( !Visible )
			return;

		if ( SetContentHash( ValueHash, 0.1f ) )
		{
			OnValueChanged();
			CheckDifferentState();
		}
	}

	/// <summary>
	/// Should get called right after creation
	/// </summary>
	public void Prime()
	{
		SetContentHash( ValueHash, -1000.0f );
		OnValueChanged();
		UpdateDifferentState();
	}

	protected void PropertyStartEdit()
	{
		SerializedProperty.NoteStartEdit( SerializedProperty );
	}

	protected void PropertyFinishEdit()
	{
		SerializedProperty.NoteFinishEdit( SerializedProperty );
	}

	protected virtual void OnValueChanged()
	{

	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		e.Accepted = true;
	}

	/// <summary>
	/// Called when right clicking a label in a ControlSheet for this widget. This allows
	/// you to add advanced menu items for this widget at the top of the menu, before the default ones.
	/// </summary>
	public virtual void OnLabelContextMenu( ContextMenu menu )
	{

	}

	/// <summary>
	/// ActionGraph serializer needs hints about the GameObject that contains this property.
	/// </summary>
	private IDisposable PushSerializationOptions()
	{
		if ( SerializedProperty.GetContainingGameObject() is { } gameObject )
		{
			return ActionGraph.PushTarget( InputDefinition.Target( typeof( GameObject ), gameObject ) );
		}

		return null;
	}

	public virtual string ToClipboardString()
	{
		using var optionScope = PushSerializationOptions();

		string str = Json.Serialize( SerializedProperty.GetValue<object>() );
		if ( str?.StartsWith( '"' ) ?? false ) str = str.Substring( 1, str.Length - 2 );
		return str;
	}

	public virtual void FromClipboardString( string clipboard )
	{
		clipboard = clipboard.Trim();

		if ( !clipboard.StartsWith( '{' ) && !clipboard.StartsWith( '[' ) && !clipboard.StartsWith( '"' ) )
			clipboard = $"\"{clipboard}\"";

		using var optionScope = PushSerializationOptions();
		using var uniqueActionGuidScope = ActionGraph.PushMakeGuidsUnique( true );

		if ( Json.TryDeserialize( clipboard, SerializedProperty.PropertyType, out var jsonValue ) && jsonValue is not null )
		{
			SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
			SerializedProperty.SetValue( jsonValue );
			SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
		}
	}

	bool _multipleDifferent;

	void CheckDifferentState()
	{
		if ( SerializedProperty is null ) // only possible in InvalidPropertyControlWidget
			return;

		if ( _multipleDifferent == SerializedProperty.IsMultipleDifferentValues )
			return;

		UpdateDifferentState();
	}

	void UpdateDifferentState()
	{
		_multipleDifferent = SerializedProperty.IsMultipleDifferentValues;
		OnMultipleDifferentValues( _multipleDifferent );
	}

	protected virtual void OnMultipleDifferentValues( bool state )
	{

	}
}

/// <summary>
/// Used when there's no defined ControlWidget
/// </summary>
file class MissingSerializedPropertyWidget : ControlWidget
{
	public MissingSerializedPropertyWidget( SerializedProperty property ) : base( property )
	{
		ReadOnly = true;
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		UpdateGeometry();
	}

	protected override Vector2 SizeHint()
	{
		var text = SerializedProperty.GetValue( "Missing Value" );
		var sh = base.SizeHint();
		sh.x -= 16;
		var rect = Paint.MeasureText( new Rect( 0, sh ), text, TextFlag.LeftTop );

		if ( rect.Height < Theme.RowHeight )
			rect.Height = Theme.RowHeight;

		return rect.Size;
	}

	protected override void OnPaint()
	{
		var text = SerializedProperty.GetValue( "Missing Value" );
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.5f ) );
		Paint.DrawText( LocalRect.Shrink( 8, 0 ), text, TextFlag.LeftCenter );
	}
}

/// <summary>
/// Used when there's no defined ControlWidget
/// </summary>
file class InvalidPropertyControlWidget : ControlWidget
{
	Label Label;

	public InvalidPropertyControlWidget() : base()
	{
		ReadOnly = true;
		HorizontalSizeMode = SizeMode.Flexible;

		Label = new Label( "Null Property" );
		Label.WordWrap = true;
		Label.SetStyles( "background-color: transparent;" );
		Label.ContentMargins = new Sandbox.UI.Margin( 6, 0 );
		Label.FixedHeight = Theme.RowHeight;

		Layout = Layout.Column();
		Layout.Add( Label );
	}

	protected override Vector2 SizeHint() => new Vector2( 22, Theme.RowHeight );

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		Label.Text = "Null Property";
	}
}


/// <summary>
/// Used when there's no defined ControlWidget
/// </summary>
file class MultiEditNotSupported : ControlWidget
{
	Label Label;

	public MultiEditNotSupported( SerializedProperty property ) : base( property )
	{
		ReadOnly = true;
		HorizontalSizeMode = SizeMode.Flexible;

		Label = new Label( "Multiedit not supported", this );
		Label.WordWrap = true;
		Label.SetStyles( "background-color: transparent;" );
		Label.ContentMargins = new Sandbox.UI.Margin( 6, 0 );
		Label.MaximumSize = new Vector2( 4096, Theme.RowHeight );

		Layout = Layout.Column();
		Layout.Add( Label );
	}

	protected override Vector2 SizeHint() => new Vector2( 22, Theme.RowHeight );
}
