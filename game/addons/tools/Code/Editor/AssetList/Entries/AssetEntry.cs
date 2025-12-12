using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Editor;

public class AssetEntry : IAssetListEntry
{
	private readonly Color TypeColor;

	public readonly FileInfo FileInfo;
	public readonly string TypeName;
	public Asset Asset { get; private set; }

	public readonly Pixmap IconSmall;
	public string Name => FileInfo.Name;
	public string Date => FileInfo.Exists ? GetPrettyDate( FileInfo.LastWriteTime ) : default;
	public string Size => FileInfo.Exists ? FileInfo.Length.SizeFormat() : default;
	public string GetStatusText() => Asset?.AbsolutePath ?? FileInfo.FullName;

	public string AbsolutePath => Asset?.AbsolutePath ?? FileInfo.FullName;
	public AssetType AssetType => Asset?.AssetType ?? null;

	public static string GetPrettyDate( DateTime timestamp )
	{
		// standardised date/time format that keeps the system date format (?)
		// so it's a bit less ass to read in column view

		CultureInfo culture = CultureInfo.InstalledUICulture;

		string datePattern = culture.DateTimeFormat.ShortDatePattern;
		datePattern = Regex.Replace( datePattern, @"\b(?<!d)d(?!d)\b", "dd" );
		datePattern = Regex.Replace( datePattern, @"\b(?<!M)M(?!M)\b", "MM" );
		datePattern = Regex.Replace( datePattern, @"\b(?<!y)y(?!y)\b", "yy" );

		string timePattern = "hh:mm tt";
		return timestamp.ToString( $"{datePattern} {timePattern}" );
	}

	public AssetEntry( Asset asset ) : this( new FileInfo( asset.AbsolutePath ), asset )
	{

	}

	public AssetEntry( FileInfo fileInfo, Asset asset )
	{
		FileInfo = fileInfo;

		var fileExtension = Path.GetExtension( fileInfo.Name );

		if ( asset != null )
		{
			TypeColor = asset.AssetType.Color;
			Asset = asset;

			if ( asset.AssetType.IsGameResource )
			{
				string name = asset.AssetType.FriendlyName;
				if ( name.Contains( "/" ) )
					name = name.Substring( name.LastIndexOf( '/' ) + 1 );

				TypeName = name;
			}
			else
				TypeName = asset.AssetType.FriendlyName;

			IconSmall = asset.AssetType.Icon16;
			return;
		}

		var assetType = AssetType.FromExtension( fileExtension );
		if ( assetType != null )
		{
			TypeColor = assetType.Color;
			TypeName = assetType.FriendlyName;
			IconSmall = assetType.Icon16;
			return;
		}

		IconSmall = Pixmap.FromFile( "common/document_sm.png" );
		TypeName = $"{Path.GetExtension( fileInfo.Name ).ToLower()} file";
		TypeColor = Color.Gray;
	}

	//
	// Events
	//
	public void DrawOverlay( Rect rect )
	{
		Paint.BilinearFiltering = true;

		//
		// Mini icon
		//
		Paint.ClearPen();
		Paint.SetBrush( TypeColor );
		var miniIconRect = rect.Shrink( 4 );
		miniIconRect.Width = 16;
		miniIconRect.Height = 16;
		Paint.Draw( miniIconRect, IconSmall );

		Paint.BilinearFiltering = false;

		//
		// Asset type strip
		//
		Paint.ClearPen();
		Paint.SetBrush( TypeColor );
		var stripRect = rect;
		stripRect.Top = rect.Top + rect.Width - 4;
		stripRect.Left = rect.Left + 4;
		stripRect.Right = rect.Right - 4;
		stripRect.Height = 4;
		Paint.DrawRect( stripRect );
	}

	public void OnScrollEnter()
	{
		if ( Asset is null || Asset.HasCachedThumbnail )
			return;

		EditorEvent.Register( this );
		Asset.GetAssetThumb( true );
	}
	public void OnScrollExit()
	{
		if ( Asset is null )
			return;

		Asset.CancelThumbBuild();
		EditorEvent.Unregister( this );
	}

	public void DrawIcon( Rect rect )
	{
		var iconLarge = Asset?.GetAssetThumb( true );

		iconLarge ??= GenericTypeIcons.GetForFile( FileInfo.ToString() );

		if ( iconLarge is null )
			return;

		Paint.BilinearFiltering = true;

		Paint.ClearPen();

		var aPos = rect.TopLeft;
		var bPos = rect.BottomLeft;

		var aColor = TypeColor.WithAlpha( 0 );
		var bColor = TypeColor.WithAlpha( 0.5f );

		Paint.SetBrushLinear( aPos, bPos, aColor, bColor );
		Paint.DrawRect( rect );
		Paint.Draw( rect, iconLarge );

		Paint.BilinearFiltering = false;
	}

	public void DrawText( Rect rect )
	{
		Paint.SetDefaultFont( 7 );
		Paint.ClearPen();
		Paint.SetPen( Theme.Text.WithAlpha( 0.7f ) );

		rect.Top += 2; // Pull down to avoid conflicting with asset type strip

		var strText = Path.GetFileNameWithoutExtension( Name );
		strText = Paint.GetElidedText( strText, rect.Width, ElideMode.Middle );

		Paint.DrawText( rect, strText, TextFlag.LeftTop );

		rect.Top += 12;

		Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
		Paint.DrawText( rect, TypeName, TextFlag.LeftTop );
	}

	public void Delete()
	{
		if ( Asset is not null )
		{
			Asset.Delete();
		}
		else
		{
			FileInfo.Delete();
		}
	}

	public void Rename( string newName )
	{
		string compiledPath = Asset?.GetCompiledFile( true );
		if ( !string.IsNullOrEmpty( compiledPath ) )
		{
			var compiled = new FileInfo( compiledPath );

			compiled.MoveTo( compiled.GetNewPath( $"{newName}_c" ) );
		}

		FileInfo.MoveTo( FileInfo.GetNewPath( newName ) );
		Asset = AssetSystem.RegisterFile( FileInfo.FullName );
	}

	public void Duplicate( string newName = null )
	{
		FileInfo.CopyTo( FileInfo.GetNewPath( newName ?? FileInfo.GetDefaultDuplicateName() ) );
	}

	public bool OnDoubleClicked( AssetList list )
	{
		if ( Asset is not null )
		{
			if ( list.Browser is AssetBrowser browser )
				browser.OnAssetSelected?.Invoke( Asset );
		}
		else
		{
			if ( EditorUtility.IsCodeFile( FileInfo.FullName ) )
				CodeEditor.OpenFile( FileInfo.FullName );
			else if ( list.Browser is AssetBrowser browser )
				browser.OnFileSelected?.Invoke( FileInfo.FullName );
		}

		return true;
	}

	public bool OnClicked( AssetList list )
	{
		return true;
	}

	public override bool Equals( object obj )
	{
		if ( obj is not AssetEntry ae )
		{
			return false;
		}

		return FileInfo.FullName.Equals( ae.FileInfo.FullName );
	}

	public override int GetHashCode()
	{
		return FileInfo.FullName.GetHashCode();
	}
}


static class GenericTypeIcons
{
	private readonly static Dictionary<string, Pixmap> _cache = new();

	public static Pixmap GetForFile( string filepath )
	{
		var fileExtension = System.IO.Path.GetExtension( filepath ).ToLowerInvariant();

		if ( fileExtension.StartsWith( "." ) )
			fileExtension = fileExtension[1..];

		if ( _cache.TryGetValue( fileExtension, out var cachedIcon ) )
			return cachedIcon;

		var size = 128;
		var pm = new Pixmap( size, size );
		var rect = new Rect( 0, 0, size, size );

		using ( Paint.ToPixmap( pm ) )
		{
			Paint.ClearBrush();
			Paint.SetPen( Theme.Border );
			Paint.DrawIcon( rect, "insert_drive_file", rect.Width );
		}

		_cache[fileExtension] = pm;

		return pm;
	}
}
