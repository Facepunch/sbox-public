using Editor.AssetPickers;
using Sandbox.Diagnostics;

namespace Editor;

/// <summary>
/// File paths stored as strings
/// </summary>
[CustomEditor( typeof( string ), WithAllAttributes = new[] { typeof( FilePathAttribute ) } )]
public class FilePathStringControlWidget : ControlWidget
{
	public override bool IsControlButton => true;
	public override bool SupportsMultiEdit => true;

	public string Extension { get; set; } = string.Empty;

	public FilePathStringControlWidget( SerializedProperty property ) : base( property )
	{
		property.TryGetAttribute<FilePathAttribute>( out var attr );
		Assert.NotNull( attr, "FilePathStringControlWidget property has no FilePathAttribute" );

		Extension = attr.Extension ?? "";

		Cursor = CursorShape.Finger;
		MouseTracking = true;
		AcceptDrops = true;
		IsDraggable = true;

		OnValueChanged();
	}

	protected override void PaintControl()
	{
		var filePath = SerializedProperty.GetValue<string>( null );
		var asset = filePath != null ? AssetSystem.FindByPath( filePath ) : null;
		var isEmpty = string.IsNullOrWhiteSpace( filePath );

		var rect = new Rect( 0, Size );

		var iconRect = rect.Shrink( 2 );
		iconRect.Width = iconRect.Height;

		rect.Left = iconRect.Right + 10;

		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.2f ) );
		Paint.DrawRect( iconRect, 2 );

		var pickerName = "Any File";
		if ( !string.IsNullOrEmpty( Extension ) )
		{
			var extensions = Extension.Split( ',' );
			if ( extensions.Length > 1 )
			{
				pickerName = $"(*.{extensions[0]} | *." + string.Join( " | *.", extensions.Skip( 1 ) ) + ")";
			}
			else if ( extensions.Length == 1 )
			{
				pickerName = $"(*.{extensions[0]})";
			}
		}

		Pixmap icon = null;
		var relatedAssetType = AssetType.All.FirstOrDefault( x => x.FileExtension == Extension );
		if ( relatedAssetType is not null )
		{
			icon = relatedAssetType.Icon64;
		}

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			var textRect = rect.Shrink( 0, 3 );
			if ( icon != null ) Paint.Draw( iconRect, icon );

			Paint.SetDefaultFont();
			Paint.SetPen( Theme.MultipleValues );
			Paint.DrawText( textRect, $"Multiple Values", TextFlag.LeftCenter );
		}
		else if ( !isEmpty )
		{
			if ( asset is not null && !asset.IsDeleted )
			{
				Paint.Draw( iconRect, asset.GetAssetThumb( true ) );
			}

			var textRect = rect.Shrink( 0, 3 );
			var fileExt = System.IO.Path.GetExtension( filePath );
			var fileName = System.IO.Path.GetFileName( filePath );
			fileName = fileName.Remove( fileName.IndexOf( fileExt ) ).ToTitleCase().ToLower();

			Paint.SetPen( Theme.Text.WithAlpha( 0.9f ) );
			Paint.SetHeadingFont( 8, 450 );
			var t = Paint.DrawText( textRect, $"{fileName}", TextFlag.LeftTop );

			textRect.Left = t.Right + 6;
			Paint.SetDefaultFont( 7 );
			Theme.DrawFilename( textRect, filePath, TextFlag.LeftCenter, Theme.Text.WithAlpha( 0.5f ) );
		}
		else
		{
			var textRect = rect.Shrink( 0, 3 );
			if ( icon != null ) Paint.Draw( iconRect, icon );

			Paint.SetDefaultFont( italic: true );
			Paint.SetPen( Theme.Text.WithAlpha( 0.2f ) );
			Paint.DrawText( textRect, pickerName, TextFlag.LeftCenter );
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var m = new ContextMenu();

		var filePath = SerializedProperty.GetValue<string>( null );
		var isEmpty = string.IsNullOrEmpty( filePath );

		//m.AddOption( "Open in Editor", "edit", () => asset?.OpenInEditor() ).Enabled = asset != null;
		//m.AddOption( "Find in Asset Browser", "search", () => AssetBrowser.OpenTo( asset, true ) ).Enabled = asset is not null;
		//m.AddSeparator();
		m.AddOption( "Copy", "file_copy", action: Copy ).Enabled = !isEmpty;
		m.AddOption( "Paste", "content_paste", action: Paste );
		m.AddSeparator();
		m.AddOption( "Clear", "backspace", action: Clear ).Enabled = !isEmpty;

		m.OpenAtCursor( false );
		e.Accepted = true;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( ReadOnly ) return;

		var filePath = SerializedProperty.GetValue<string>( null );

		var picker = new GenericPicker( Parent, [], new() );
		var query = "";
		foreach ( var ext in Extension.Split( ',' ) )
		{
			if ( string.IsNullOrEmpty( ext ) ) continue;
			query += $"t:{ext} ";
		}
		picker.AssetBrowser.Search.Value = query;

		picker.Title = $"Select {SerializedProperty.DisplayName}";
		picker.AssetBrowser.OnHighlight += ( entries ) =>
		{
			PropertyStartEdit();
			UpdateFromObject( entries.First() );
			PropertyFinishEdit();
		};
		picker.AssetBrowser.OnFileSelected += ( o ) =>
		{
			PropertyStartEdit();
			UpdateFromObject( o );
			PropertyFinishEdit();
		};
		picker.Show();

		picker.SetSelection( filePath );
	}

	private void UpdateFromObject( object obj )
	{
		if ( obj is null ) return;

		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		if ( obj is Asset asset )
		{
			SerializedProperty.SetValue( asset.RelativePath );
		}
		else if ( obj is string str )
		{
			var relativePath = AssetSystem.FindByPath( str )?.RelativePath
				?? System.IO.Path.GetRelativePath( Project.Current.GetAssetsPath(), str );
			SerializedProperty.SetValue( relativePath );
		}
		else if ( obj is AssetEntry assetEntry )
		{
			var path = AssetSystem.FindByPath( assetEntry.AbsolutePath )?.RelativePath
				?? System.IO.Path.GetRelativePath( Project.Current.GetAssetsPath(), assetEntry.AbsolutePath );
			SerializedProperty.SetValue( path );
		}
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}

	bool CanAssign( string path )
	{
		var ext = System.IO.Path.GetExtension( path ).Substring( 1 );
		if ( !string.IsNullOrEmpty( Extension ) && !Extension.Split( ',' ).Contains( ext ) )
		{
			return false;
		}

		return true;
	}

	public override void OnDragHover( DragEvent ev )
	{
		if ( string.IsNullOrEmpty( ev.Data.Text ) )
			return;

		ev.Action = DropAction.Link;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		var path = ev.Data.Text;
		if ( string.IsNullOrEmpty( path ) )
			return;

		if ( !CanAssign( path ) )
			return;

		PropertyStartEdit();
		UpdateFromObject( path );
		PropertyFinishEdit();

		ev.Action = DropAction.Link;
	}

	protected override void OnDragStart()
	{
		var filePath = SerializedProperty.GetValue<string>( null );

		var drag = new Drag( this );
		drag.Data.Text = filePath;
		drag.Execute();
	}

	void Copy()
	{
		var filePath = SerializedProperty.GetValue<string>( null );
		if ( string.IsNullOrEmpty( filePath ) ) return;

		EditorUtility.Clipboard.Copy( filePath );
	}

	void Paste()
	{
		var path = EditorUtility.Clipboard.Paste();
		if ( !CanAssign( path ) )
			return;

		UpdateFromObject( path );
	}

	void Clear()
	{
		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( (string)null );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}
}
