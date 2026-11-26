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

	/// <summary>
	/// Captures the current search on the editor thread. The returned files can be enumerated on a worker.
	/// </summary>
	public IEnumerable<FileInfo> Filter( LocalAssetBrowser.Location location, CancellationToken token, bool showRecursiveFiles = false )
	{
		if ( token.IsCancellationRequested )
			return [];

		var query = Query;
		var terms = string.IsNullOrEmpty( query ) ? Array.Empty<string>() : query.Split( ' ' );
		var hasExtension = query.Contains( '.' );
		var includePathNames = AssetLocations.IncludePathNames;
		var active = AssetTypes?.ActiveTags;
		var types = active?.Where( x => !x.StartsWith( "tag:" ) ).ToArray() ?? [];
		var tag = active?.FirstOrDefault( x => x.StartsWith( "tag:" ) )?[4..];

		return EnumerateFiles( location, token, showRecursiveFiles ).Where( file =>
			!FilterName( file, terms, hasExtension, includePathNames )
			&& !FilterAssetTags( file, tag )
			&& !FilterAssetType( file, types ) );
	}

	private static IEnumerable<FileInfo> EnumerateFiles( LocalAssetBrowser.Location location, CancellationToken token, bool showRecursiveFiles )
	{
		if ( token.IsCancellationRequested )
			yield break;

		foreach ( var file in location.GetFiles() )
		{
			if ( token.IsCancellationRequested )
				yield break;

			yield return file;
		}

		if ( showRecursiveFiles )
		{
			foreach ( var subDir in location.GetDirectories() )
			{
				if ( token.IsCancellationRequested )
					yield break;

				foreach ( var subFile in EnumerateFiles( subDir, token, showRecursiveFiles ) )
				{
					if ( token.IsCancellationRequested )
						yield break;

					yield return subFile;
				}
			}
		}
	}

	private static bool FilterName( FileInfo file, string[] terms, bool hasExtension, bool includePathNames )
	{
		if ( terms.Length == 0 )
			return false;

		var asset = AssetSystem.FindByPath( file.FullName );
		foreach ( var splitValue in terms )
		{
			var negated = splitValue.StartsWith( "-" );
			var search = negated ? splitValue[1..] : splitValue;
			bool matches;

			if ( search.Contains( '*' ) || search.Contains( '?' ) )
			{
				// Use the source asset path so compiled-only assets also match their source extension.
				var path = (asset?.Path ?? file.Name).Replace( '\\', '/' );
				search = search.Replace( '\\', '/' );
				var name = search.Contains( '/' ) ? path : Path.GetFileName( path );
				matches = name.WildcardMatch( search );
			}
			else if ( asset != null && !hasExtension )
			{
				var assetName = includePathNames ? asset.RelativePath : asset.Name;
				matches = assetName.Contains( search, StringComparison.OrdinalIgnoreCase )
					|| asset.AssetType.FriendlyName.Contains( search, StringComparison.OrdinalIgnoreCase )
					|| asset.Tags.Any( x => x.Contains( search, StringComparison.OrdinalIgnoreCase ) );
			}
			else
			{
				matches = file.Name.Contains( search, StringComparison.OrdinalIgnoreCase );
			}

			if ( matches == negated )
				return true;
		}

		return false;
	}

	private static bool FilterAssetType( FileInfo file, string[] types )
	{
		if ( types.Length == 0 )
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

	private static bool FilterAssetTags( FileInfo file, string tag )
	{
		if ( tag is null )
			return false;

		var asset = AssetSystem.FindByPath( file.FullName );
		return asset is null || !asset.Tags.Any( x => x == tag );
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
