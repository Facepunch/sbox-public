using Sandbox.Engine;
using SkiaSharp;
using Topten.RichTextKit;

namespace Sandbox;

/// <summary>
/// Loads the font files under /fonts/ and picks the best face for a text style.
/// </summary>
internal class FontManager : FontMapper
{
	public static readonly FontManager Instance = new();

	/// <summary>
	/// One loaded font file. Menu fonts survive a game's Clear.
	/// </summary>
	record struct LoadedFont( SKTypeface Typeface, bool IsMenu );

	Dictionary<(string family, int weight, int width, SKFontStyleSlant slant), LoadedFont> _loadedFonts = new();
	Dictionary<(string family, int weight, bool italic), SKTypeface> _cache = new();
	List<FileWatch> _watchers = new();

	/// <summary>
	/// Every family name we have a face for.
	/// </summary>
	public IEnumerable<string> FontFamilies
	{
		get
		{
			lock ( _loadedFonts )
			{
				return _loadedFonts.Values.Select( x => x.Typeface.FamilyName ).Distinct().ToList();
			}
		}
	}

	/// <summary>
	/// Load every .ttf and .otf under /fonts/ and keep watching the folder for new ones.
	/// </summary>
	public void LoadAll( BaseFileSystem fileSystem )
	{
		lock ( _cache )
		{
			// new fonts can replace fallbacks and best-matches
			_cache.Clear();
		}

		var fontFiles = fileSystem.FindFile( "/fonts/", "*.ttf", true )
			.Union( fileSystem.FindFile( "/fonts/", "*.otf", true ) );

		Parallel.ForEach( fontFiles, font => Load( fileSystem.OpenRead( $"/fonts/{font}" ) ) );

		foreach ( var pattern in new[] { "*.ttf", "*.otf" } )
		{
			var watch = fileSystem.Watch( pattern );
			watch.OnChanges += w => OnFontFilesChanged( w, fileSystem );
			_watchers.Add( watch );
		}
	}

	private void OnFontFilesChanged( FileWatch watch, BaseFileSystem fileSystem )
	{
		lock ( _cache )
		{
			_cache.Clear();
		}

		foreach ( var file in watch.Changes )
		{
			Load( fileSystem.OpenRead( file ) );
		}
	}

	private void Load( System.IO.Stream stream )
	{
		if ( stream is null ) return;

		var face = SKTypeface.FromStream( stream );
		if ( face is null ) return;

		bool isMenu = GlobalContext.Current == GlobalContext.Menu;
		var key = (face.FamilyName, face.FontWeight, face.FontWidth, face.FontSlant);

		lock ( _loadedFonts )
		{
			// the first file in wins, later copies of the same face are dropped
			if ( !_loadedFonts.TryAdd( key, new LoadedFont( face, isMenu ) ) )
			{
				face.Dispose();
				return;
			}
		}

		Log.Trace( $"Loaded font {face.FamilyName} weight {face.FontWeight} width {face.FontWidth} (IsMenu: {isMenu})" );
	}

	/// <summary>
	/// The face to draw this style with. Falls back to a system font when we don't have the family.
	/// </summary>
	public override SKTypeface TypefaceFromStyle( IStyle style, bool ignoreFontVariants )
	{
		var key = (style.FontFamily, style.FontWeight, style.FontItalic);

		lock ( _cache )
		{
			if ( _cache.TryGetValue( key, out var cached ) ) return cached;
		}

		var face = GetBestTypeface( style );
		if ( face is null )
		{
			Log.Warning( $"FontManager: Font '{style.FontFamily}' not found, falling back to system font" );
			face = Default.TypefaceFromStyle( style, ignoreFontVariants );
		}

		lock ( _cache )
		{
			_cache[key] = face;
		}

		return face;
	}

	/// <summary>
	/// The closest face we have in the style's family: matching slant if there is one, then the
	/// closest weight. Ties are settled by the faces themselves, never by the order they loaded in.
	/// </summary>
	private SKTypeface GetBestTypeface( IStyle style )
	{
		lock ( _loadedFonts )
		{
			var familyFonts = _loadedFonts.Values.Select( x => x.Typeface )
				.Where( x => string.Equals( x.FamilyName, style.FontFamily, StringComparison.OrdinalIgnoreCase ) );
			if ( !familyFonts.Any() ) return null;

			var slantFonts = familyFonts.Where( x => x.IsItalic == style.FontItalic );
			if ( slantFonts.Any() ) familyFonts = slantFonts;

			// Normal width beats condensed or expanded (Skia files those under the plain family name), then the closest weight.
			// The last two only make the order total.
			return familyFonts
				.OrderBy( x => Math.Abs( x.FontWidth - (int)SKFontStyleWidth.Normal ) )
				.ThenBy( x => WeightDistance( x.FontWeight, style.FontWeight ) )
				.ThenBy( x => x.FontWidth )
				.ThenBy( x => x.FontSlant )
				.First();
		}
	}

	/// <summary>
	/// How far a face's weight is from the one asked for. Ties go the way CSS settles them:
	/// heavier for 400 and anything above 500, lighter for the rest.
	/// </summary>
	static int WeightDistance( int weight, int wanted )
	{
		bool preferHeavier = wanted == 400 || wanted > 500;
		bool preferredSide = (weight > wanted) == preferHeavier;
		return Math.Abs( weight - wanted ) * 2 + (preferredSide ? 0 : 1);
	}

	/// <summary>
	/// Drop every loaded font and stop watching for new ones. Menu fonts stay unless removeMenu is set.
	/// </summary>
	public void Clear( bool removeMenu )
	{
		foreach ( var watcher in _watchers )
		{
			watcher.Dispose();
		}
		_watchers.Clear();

		lock ( _loadedFonts )
		{
			foreach ( var (key, font) in _loadedFonts.ToArray() )
			{
				if ( !removeMenu && font.IsMenu )
					continue;

				_loadedFonts.Remove( key );
				font.Typeface.Dispose();
			}
		}

		lock ( _cache )
		{
			_cache.Clear();
		}
	}
}
