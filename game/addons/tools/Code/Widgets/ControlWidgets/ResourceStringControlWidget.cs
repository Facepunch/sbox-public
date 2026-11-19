using Sandbox.Diagnostics;
using System.IO;

namespace Editor;

/// <summary>
/// Resources stored as strings
/// </summary>
[CustomEditor( typeof( string ), WithAllAttributes = new[] { typeof( AssetPathAttribute ) } )]
public class ResourceStringControlWidget : ControlWidget
{
	AssetType AssetType;

	public override bool IsControlButton => true;
	public override bool SupportsMultiEdit => true;

	public string WarningText { get; set; }

	public ResourceStringControlWidget( SerializedProperty property ) : base( property )
	{
		property.TryGetAttribute<AssetPathAttribute>( out var attr );
		Assert.NotNull( attr, "ResourceStringControlWidget property has no AssetPathAttribute" );

		var resourceExtension = attr.AssetTypeExtension;
		resourceExtension = resourceExtension.Replace( "resource:", "" );
		resourceExtension = resourceExtension.Trim().ToLower();

		AssetType = AssetType.All.FirstOrDefault( x => x.FileExtension == resourceExtension );

		Cursor = CursorShape.Finger;
		MouseTracking = true;
		AcceptDrops = true;
		IsDraggable = true;

		OnValueChanged();
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		if ( AssetType == AssetType.MapFile )
		{
			var resource = SerializedProperty.GetValue<string>( null );
			var asset = resource != null ? AssetSystem.FindByPath( resource ) : null;
			if ( asset is not null )
			{
				bool isCompiled = File.Exists( Path.ChangeExtension( asset.AbsolutePath, ".vpk" ) );
				if ( !isCompiled ) WarningText = ToolTip = "Map is not compiled";
			}
		}
	}

	protected override void PaintControl()
	{
		var resource = SerializedProperty.GetValue<string>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource ) : null;

		var rect = new Rect( 0, Size );

		var iconRect = rect.Shrink( 2 );
		iconRect.Width = iconRect.Height;

		rect.Left = iconRect.Right + 10;

		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.2f ) );
		Paint.DrawRect( iconRect, 2 );

		var pickerName = "Unknown Resource";
		if ( AssetType is not null ) pickerName = AssetType.FriendlyName;

		if ( !string.IsNullOrEmpty( WarningText ) )
		{
			Rect warningRect = iconRect;
			warningRect.Left = rect.Left + 16;
			Paint.SetPen( Theme.Yellow );
			Paint.DrawIcon( warningRect, "warning", Math.Max( 16, warningRect.Height / 2 ) );
			rect.Left += 16;
		}

		Pixmap icon = AssetType?.Icon64;

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			var textRect = rect.Shrink( 0, 3 );
			if ( icon != null ) Paint.Draw( iconRect, icon );

			Paint.SetDefaultFont();
			Paint.SetPen( Theme.MultipleValues );
			Paint.DrawText( textRect, $"Multiple Values", TextFlag.LeftCenter );
		}
		else if ( asset is not null && !asset.IsDeleted )
		{
			Paint.Draw( iconRect, asset.GetAssetThumb( true ) );

			var textRect = rect.Shrink( 0, 3 );

			Paint.SetPen( Theme.Text.WithAlpha( 0.9f ) );
			Paint.SetHeadingFont( 8, 450 );
			var t = Paint.DrawText( textRect, $"{asset.Name}", TextFlag.LeftTop );

			textRect.Left = t.Right + 6;
			Paint.SetDefaultFont( 7 );
			Theme.DrawFilename( textRect, asset.RelativePath, TextFlag.LeftCenter, Theme.Text.WithAlpha( 0.5f ) );
		}
		else if ( !string.IsNullOrWhiteSpace( resource ) )
		{
			var textRect = rect.Shrink( 0, 3 );

			bool isPackage = !resource.Contains( ".vmap" ) && Package.TryParseIdent( resource, out _ );
			if ( !isPackage )
			{
				Paint.SetBrush( Theme.Red.Darken( 0.8f ) );
				Paint.DrawRect( iconRect, 2 );

				Paint.SetPen( Theme.Red );
				Paint.DrawIcon( iconRect, "error", Math.Max( 16, iconRect.Height / 2 ) );
			}
			else if ( icon != null ) Paint.Draw( iconRect, icon );

			Paint.SetPen( Theme.Text.WithAlpha( 0.9f ) );
			Paint.SetHeadingFont( 8, 450 );
			var t = Paint.DrawText( textRect, isPackage ? (Package.TryGetCached( resource, out Package package ) ? $"{package.Title} ☁️" : $"Cloud {pickerName}") : $"Missing {pickerName}", TextFlag.LeftTop );

			textRect.Left = t.Right + 6;
			Paint.SetDefaultFont( 7 );
			Theme.DrawFilename( textRect, resource, TextFlag.LeftCenter, Theme.Text.WithAlpha( 0.5f ) );
		}
		else
		{
			var textRect = rect.Shrink( 0, 3 );
			if ( icon != null ) Paint.Draw( iconRect, icon );

			Paint.SetDefaultFont( italic: true );
			Paint.SetPen( Theme.Text.WithAlpha( 0.2f ) );
			Paint.DrawText( textRect, $"{pickerName}", TextFlag.LeftCenter );
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var m = new ContextMenu();

		var resource = SerializedProperty.GetValue<string>( null );
		var asset = (resource != null) ? AssetSystem.FindByPath( resource ) : null;

		m.AddOption( "Open in Editor", "edit", () => asset?.OpenInEditor() ).Enabled = asset != null && !asset.IsProcedural;
		m.AddOption( "Find in Asset Browser", "search", () => LocalAssetBrowser.OpenTo( asset, true ) ).Enabled = asset is not null;
		m.AddSeparator();
		m.AddOption( "Copy", "file_copy", action: Copy ).Enabled = AssetType == AssetType.MapFile ? !string.IsNullOrWhiteSpace( resource ) : asset != null;
		m.AddOption( "Paste", "content_paste", action: Paste );
		m.AddSeparator();
		m.AddOption( "Clear", "backspace", action: Clear ).Enabled = resource != null;

		m.OpenAtCursor( false );
		e.Accepted = true;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( ReadOnly ) return;

		var resource = SerializedProperty.GetValue<string>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource ) : null;

		var picker = AssetPicker.Create( null, AssetType );
		PropertyStartEdit();

		picker.SetSelection( resource );
		picker.Title = $"Select {SerializedProperty.DisplayName}";
		if ( AssetType == AssetType.MapFile )
		{
			picker.OnPackagePicked = ( o ) =>
			{
				SerializedProperty.SetValue( o.FullIdent );
				PropertyFinishEdit();
			};
		}
		else
		{
			picker.OnAssetHighlighted = ( o ) =>
			{
				UpdateFromAsset( o.FirstOrDefault() );
				PropertyFinishEdit();
			};
		}
		picker.OnAssetPicked = ( o ) => UpdateFromAsset( o.FirstOrDefault() );
		picker.Show();

		picker.SetSelection( asset );

		if ( SerializedProperty.TryGetAttribute<AssetHintAttribute>( out var hint ) )
		{
			picker.SetSearchText( hint.Pattern );
		}
	}

	private void UpdateFromAsset( Asset asset )
	{
		if ( asset is null ) return;

		SerializedProperty.Parent?.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( asset.RelativePath );
		SerializedProperty.Parent?.NoteFinishEdit( SerializedProperty );
	}

	public override void OnDragHover( DragEvent ev )
	{
		if ( AssetType == AssetType.MapFile && ev.Data.Url?.Scheme == "https" )
		{
			ev.Action = DropAction.Link;
			return;
		}

		if ( !ev.Data.HasFileOrFolder )
			return;

		var asset = AssetSystem.FindByPath( ev.Data.FileOrFolder );

		if ( asset == null || AssetType != asset.AssetType )
			return;

		ev.Action = DropAction.Link;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		if ( AssetType == AssetType.MapFile && ev.Data.Url?.Scheme == "https" )
		{
			if ( !Package.TryParseIdent( ev.Data.Url.ToString(), out var ident ) )
				return;

			PropertyStartEdit();
			SerializedProperty.SetValue( $"{ident.org}.{ident.package}" );
			PropertyFinishEdit();
			ev.Action = DropAction.Link;
			return;
		}

		if ( !ev.Data.HasFileOrFolder )
			return;

		var asset = AssetSystem.FindByPath( ev.Data.FileOrFolder );

		if ( asset == null || AssetType != asset.AssetType )
			return;

		PropertyStartEdit();
		UpdateFromAsset( asset );
		PropertyFinishEdit();
		ev.Action = DropAction.Link;
	}

	protected override void OnDragStart()
	{
		var resource = SerializedProperty.GetValue<string>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource ) : null;

		if ( asset == null )
			return;

		var drag = new Drag( this );
		drag.Data.Url = new Uri( $"file://{asset.AbsolutePath}" );
		drag.Execute();
	}

	void Copy()
	{
		var resource = SerializedProperty.GetValue<string>( null );
		if ( resource == null ) return;

		var asset = AssetSystem.FindByPath( resource );
		if ( asset != null )
			resource = asset.RelativePath;

		EditorUtility.Clipboard.Copy( resource );
	}

	void Paste()
	{
		var path = EditorUtility.Clipboard.Paste();
		var asset = AssetSystem.FindByPath( path );
		UpdateFromAsset( asset );
	}

	void Clear()
	{
		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( (Resource)null );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}
}
