using System.IO;

namespace Editor;

public class PackageEntry : IAssetListEntry
{
	public string Name
	{
		get
		{
			if ( !Package.Public )
				return $"{Package.Title} (hidden)";

			return Package.Title;
		}
	}
	public AssetType AssetType => GetAssetType( Package.TypeName );
	public string Author => Package.Org.Title;
	public string Date => Package.Updated.DateTime.ToString();
	public string Thumb => Package.Thumb;
	public string GetStatusText() => Package.Ident;

	private readonly Color TypeColor;

	public readonly string TypeName;
	public readonly Pixmap IconLarge;
	public readonly Pixmap IconSmall;

	public readonly Package Package;

	private readonly static Dictionary<string, Pixmap> CachedGenericIcons = new();

	public static AssetType GetAssetType( string typeName ) => typeName switch
	{
		"map" => AssetType.MapFile,
		"model" => AssetType.Model,
		"material" => AssetType.Material,
		"sound" => AssetType.SoundFile,
		"shader" => AssetType.Shader,

		_ => null
	};

	public PackageEntry( Package package )
	{
		Package = package;
		IconLarge = Pixmap.FromFile( package.Thumb );

		var assetType = GetAssetType( package.TypeName );

		if ( assetType == null )
		{
			TypeName = package.TypeName;
		}
		else
		{
			TypeName = assetType.FriendlyName;
			TypeColor = assetType.Color;
			IconSmall = assetType.Icon16;
		}
	}

	public void DrawOverlay( Rect rect )
	{
		Paint.BilinearFiltering = true;

		//
		// Mini icon
		//
		if ( IconSmall != null )
		{
			Paint.ClearPen();
			Paint.SetBrush( TypeColor );
			var miniIconRect = rect.Shrink( 4 );
			miniIconRect.Width = 16;
			miniIconRect.Height = 16;
			Paint.Draw( miniIconRect, IconSmall );
		}

		//
		// Org icon
		// 
		Paint.BilinearFiltering = false;
		Paint.ClearPen();
		Paint.SetBrush( TypeColor );
		var orgIconRect = rect.Shrink( 2 );
		orgIconRect.Top = rect.Top + rect.Width + 4;
		orgIconRect.Left += 2;
		orgIconRect.Width = 16;
		orgIconRect.Height = 16;
		Paint.Draw( orgIconRect, Package.Org.Thumb );

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

	public void DrawIcon( Rect rect )
	{
		Paint.BilinearFiltering = true;

		Paint.ClearPen();

		var aPos = rect.TopLeft;
		var bPos = rect.BottomLeft;

		var aColor = TypeColor.WithAlpha( 0 );
		var bColor = TypeColor.WithAlpha( 0.5f );

		Paint.SetBrushLinear( aPos, bPos, aColor, bColor );
		Paint.DrawRect( rect );
		Paint.Draw( rect, Package.Thumb );

		Paint.BilinearFiltering = false;
	}

	public void DrawText( Rect rect )
	{
		Paint.SetDefaultFont( 7 );
		Paint.ClearPen();
		Paint.SetPen( Theme.Text.WithAlpha( 0.7f ) );

		rect.Top += 2; // Pull down to avoid conflicting with asset type strip
		rect.Left += 20;

		var strText = Path.GetFileNameWithoutExtension( Name );
		strText = Paint.GetElidedText( strText, rect.Width, ElideMode.Middle );

		Paint.DrawText( rect, strText, TextFlag.LeftTop );

		rect.Top += 10;

		Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
		Paint.DrawText( rect, TypeName, TextFlag.LeftTop );
	}

	public bool OnDoubleClicked( AssetList list )
	{
		if ( list.Browser is CloudAssetBrowser browser )
			browser.OnPackageSelected?.Invoke( Package );

		return true;
	}

	public override bool Equals( object obj )
	{
		if ( obj is not PackageEntry pe )
		{
			return false;
		}

		return Package.FullIdent.Equals( pe.Package.FullIdent );
	}

	public override int GetHashCode()
	{
		return Package.FullIdent.GetHashCode();
	}
}
