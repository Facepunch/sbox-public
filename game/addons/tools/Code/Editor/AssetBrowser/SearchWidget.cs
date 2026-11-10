using System.IO;
using System.Threading;

namespace Editor;

public class SearchWidget : Widget
{
	private LineEdit LineEdit;
	private ToolButton ClearButton;

	public string Value
	{
		get => LineEdit.Value;
		set
		{
			LineEdit.Value = value;
			Update();
		}
	}

	public string BaseQuery { get; set; } = string.Empty;
	public string Query => $"{BaseQuery} {Value}".TrimStart();

	public string PlaceholderText
	{
		get => LineEdit.PlaceholderText;
		set { LineEdit.PlaceholderText = value; LineEdit.Update(); }
	}

	public Action ValueChanged;

	public TagPicker AssetTypes;

	public bool IsEmpty => string.IsNullOrEmpty( Query );

	public SearchWidget( Widget parent, bool showAssetTypes = true ) : base( parent )
	{
		Layout = Layout.Row();
		Layout.Alignment = TextFlag.LeftCenter;

		LineEdit = Layout.Add( new LineEdit(), 1 );
		LineEdit.PlaceholderText = $"⌕  Search";
		LineEdit.TextChanged += OnTextChanged;

		if ( parent is AssetBrowser ab )
		{
			LineEdit.PlaceholderText = $"⌕  Search {ab.CurrentLocation?.Name}";

			ab.OnFolderOpened = () =>
			{
				LineEdit.PlaceholderText = $"⌕  Search {ab.CurrentLocation?.Name}";
				LineEdit.Update();
			};
		}

		ClearButton = Layout.Add( new ToolButton( string.Empty, "clear", this ) );
		ClearButton.Visible = false;
		ClearButton.MouseLeftPress = () =>
		{
			LineEdit.Text = string.Empty;
		};

		if ( showAssetTypes )
		{
			AssetTypes = new TagPicker();
			AssetTypes.Icon = "filter_list";
			AssetTypes.OnValueChanged = () =>
			{
				ValueChanged?.Invoke();
				Update();
			};

			Layout.Add( AssetTypes );
		}
	}

	/// <summary>
	/// Clears the search text and any active type/tag filters. Does nothing if no filter is active.
	/// </summary>
	public void Clear()
	{
		bool hadTags = AssetTypes is { Enabled: true } && (AssetTypes.ActiveTags.Count > 0 || AssetTypes.ExcludedTags.Count > 0);

		if ( hadTags )
		{
			AssetTypes.ActiveTags.Clear();
			AssetTypes.ExcludedTags.Clear();
			AssetTypes.Rebuild();
		}

		if ( !string.IsNullOrEmpty( LineEdit.Text ) )
		{
			LineEdit.Text = string.Empty;
			return;
		}

		if ( !hadTags )
			return;

		ValueChanged?.Invoke();
		Update();
	}

	private void OnTextChanged( string value )
	{
		if ( value.EndsWith( " " ) )
		{
			var split = value.Split( " " ).ToList();
			bool splitChanged = false;

			foreach ( var s in split.ToArray() )
			{
				if ( s.StartsWith( "t:" ) )
				{
					var typeName = s[2..].Trim();
					if ( string.IsNullOrWhiteSpace( typeName ) )
						continue;

					AssetTypes.Toggle( typeName );

					split.Remove( s );
					splitChanged = true;
				}
			}

			if ( splitChanged )
				LineEdit.Text = string.Join( " ", split );
		}

		ClearButton.Visible = !string.IsNullOrEmpty( value );

		ValueChanged?.Invoke();
	}

	public IEnumerable<FileInfo> Filter( LocalAssetBrowser.Location location, CancellationToken token, bool showRecursiveFiles = false )
	{
		if ( token.IsCancellationRequested )
			yield break;

		foreach ( var file in location.GetFiles() )
		{
			if ( token.IsCancellationRequested )
				yield break;

			if ( FilterName( file ) )
				continue;

			if ( FilterAssetTags( file ) )
				continue;

			if ( FilterAssetType( file ) )
				continue;

			yield return file;
		}

		if ( showRecursiveFiles )
		{
			foreach ( var subDir in location.GetDirectories() )
			{
				if ( token.IsCancellationRequested )
					yield break;

				foreach ( var subFile in Filter( subDir, token, showRecursiveFiles ) )
				{
					if ( token.IsCancellationRequested )
						yield break;

					yield return subFile;
				}
			}
		}
	}

	private bool FilterName( FileInfo file )
	{
		if ( IsEmpty )
			return false;

		var asset = AssetSystem.FindByPath( file.FullName );
		var query = Query;

		var splitValues = query.Split( " " );
		bool hasExtension = query.Contains( '.' );
		if ( asset != null && !hasExtension )
		{
			foreach ( var splitValue in splitValues )
			{
				var search = splitValue;
				var negated = false;
				if ( splitValue.StartsWith( "-" ) )
				{
					search = splitValue.Substring( 1 );
					negated = true;
				}

				var assetName = asset.Name;
				if ( AssetLocations.IncludePathNames )
				{
					assetName = asset.RelativePath;
				}
				if ( !negated )
				{
					if ( !(
						assetName.Contains( search, StringComparison.OrdinalIgnoreCase )
						|| asset.AssetType.FriendlyName.Contains( search, StringComparison.OrdinalIgnoreCase )
						|| asset.Tags.Any( x => x.Contains( search, StringComparison.OrdinalIgnoreCase ) )
					) )
					{
						return true;
					}
				}
				else
				{
					if ( assetName.Contains( search, StringComparison.OrdinalIgnoreCase )
						|| asset.AssetType.FriendlyName.Contains( search, StringComparison.OrdinalIgnoreCase )
						|| asset.Tags.Any( x => x.Contains( search, StringComparison.OrdinalIgnoreCase ) ) )
					{
						return true;
					}
				}
			}

			return false;
		}
		else
		{
			// Loose file filtering
			foreach ( var splitValue in splitValues )
			{
				var search = splitValue;
				var negated = false;
				if ( splitValue.StartsWith( "-" ) )
				{
					search = splitValue.Substring( 1 );
					negated = true;
				}

				if ( !negated )
				{
					if ( !file.Name.Contains( search, StringComparison.OrdinalIgnoreCase ) )
					{
						return true;
					}
				}
				else
				{
					if ( file.Name.Contains( search, StringComparison.OrdinalIgnoreCase ) )
					{
						return true;
					}
				}
			}

			return false;
		}
	}

	private bool FilterAssetType( FileInfo file )
	{
		var types = AssetTypes.ActiveTags.Where( x => !x.StartsWith( "tag:" ) );
		if ( types.Count() == 0 )
			return false;
		if ( string.IsNullOrWhiteSpace( file.Extension ) )
			return false;

		var ext = file.Extension.Substring( 1 ); // trim dot

		foreach ( var typeExt in types )
		{
			if ( ext.Equals( typeExt, StringComparison.OrdinalIgnoreCase ) || ext.Equals( $"{typeExt}_c", StringComparison.OrdinalIgnoreCase ) )
			{
				return false;
			}
		}

		return true; // remove
	}

	private bool FilterAssetTags( FileInfo file )
	{
		var tags = AssetTypes.ActiveTags.Where( x => x.StartsWith( "tag:" ) );
		if ( tags.Count() == 0 )
			return false;

		var asset = AssetSystem.FindByPath( file.FullName );
		if ( asset is null )
			return true;

		foreach ( var e in tags )
		{
			string tag = e[4..];
			return !asset?.Tags.Any( x => x == tag ) ?? true;
		}

		return true; // remove
	}

	protected override void OnPaint()
	{
		Paint.ClearBrush();
		Paint.ClearPen();

		var rect = LocalRect;

		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( rect, Theme.ControlRadius );
	}
}
