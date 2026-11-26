using System.IO;

namespace Editor;

public class FolderMetadataDialog : Dialog
{
	Label FolderIcon;
	Label SizeLabel;
	Label ContainsLabel;

	public FolderMetadataDialog( DirectoryInfo directory )
	{
		Layout = Layout.Column();
		Layout.Margin = 8f;
		Layout.Spacing = 8f;

		var metadata = DirectoryEntry.GetMetadata( directory.FullName );

		{
			var header = Layout.Add( Layout.Row() );
			header.Margin = 4f;
			header.Spacing = 8f;

			FolderIcon = header.Add( new Label( this ) );
			FolderIcon.Text = "folder";
			FolderIcon.SetStyles( $"font-family: Material Symbols Outlined; font-size: 42px; color: {metadata.Color.Hex};" );
			FolderIcon.OnPaintOverride = () =>
			{
				Paint.ClearBrush();
				Paint.SetPen( metadata.Color );
				var folderRect = Paint.DrawIcon( FolderIcon.LocalRect, "folder", FolderIcon.LocalRect.Width );

				var icon = string.IsNullOrEmpty( metadata.Icon ) ? DirectoryEntry.GetUniqueIcon( directory.Name.ToLowerInvariant() ) : metadata.Icon;
				if ( !string.IsNullOrEmpty( icon ) )
				{
					folderRect.Top += 5f;
					Paint.SetPen( metadata.Color.Darken( 0.25f ) );
					Paint.DrawIcon( folderRect, icon, folderRect.Width / 3f, TextFlag.DontClip | TextFlag.Center );
				}

				return true;
			};

			var folderName = header.Add( new LineEdit( this ) );
			folderName.Text = directory.Name;
			folderName.Enabled = false;
		}

		Layout.Add( new Separator( 1f ) );

		{
			var infoColumn = Layout.Add( Layout.Column() );
			infoColumn.Margin = 8f;
			infoColumn.Spacing = 12f;

			var locationRow = infoColumn.Add( Layout.Row() );
			locationRow.Spacing = 8f;
			locationRow.Add( new Label( this ) { Text = "Location:", FixedWidth = 80 } );
			locationRow.Add( new Label( this ) { Text = directory.FullName } );

			var sizeRow = infoColumn.Add( Layout.Row() );
			sizeRow.Spacing = 8f;
			sizeRow.Add( new Label( this ) { Text = "Size:", FixedWidth = 80 } );
			SizeLabel = sizeRow.Add( new Label( this ) { Text = "0 B" } );

			var containsRow = infoColumn.Add( Layout.Row() );
			containsRow.Spacing = 8f;
			containsRow.Add( new Label( this ) { Text = "Contains:", FixedWidth = 80 } );
			ContainsLabel = containsRow.Add( new Label( this ) { Text = "0 Files, 0 Folders" } );
		}

		Layout.Add( new Separator( 1f ) );

		{
			var sheet = new ControlSheet();
			sheet.Spacing = 4f;
			var serialized = metadata.GetSerialized();
			foreach ( var prop in serialized )
			{
				sheet.AddRow( prop );
			}
			Layout.Add( sheet );

			serialized.OnPropertyChanged += ( prop ) =>
			{
				if ( prop.Name == "Color" )
					FolderIcon.SetStyles( $"font-family: Material Symbols Outlined; font-size: 42px; color: {prop.GetValue<Color>().Hex};" );
				else if ( prop.Name == "Icon" )
					FolderIcon.Update();
			};
		}

		Layout.AddStretchCell();

		Window.Size = new Vector2( 500, 250 );
		Window.WindowTitle = $"{directory.Name} Metadata";

		CalculateFolderSize( directory );
	}

	void CalculateFolderSize( DirectoryInfo info )
	{
		var files = info.EnumerateFiles( "*", SearchOption.AllDirectories );
		long size = 0;
		int fileCount = files.Count();
		int folderCount = info.EnumerateDirectories( "*", SearchOption.AllDirectories ).Count();
		foreach ( var file in files )
		{
			size += file.Length;
		}
		SizeLabel.Text = size.SizeFormat();
		ContainsLabel.Text = $"{fileCount} Files, {folderCount} Folders";
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		DirectoryEntry.SaveMetadata();
	}
}
