using Sandbox.Engine;
using SkiaSharp;
using System.IO;
using Topten.RichTextKit;

namespace Sandbox;

/// <summary>
/// Loads the font files under /fonts/ and picks the best face for a text style.
/// </summary>
internal class FontManager : FontMapper, ICharacterMatcher
{
	public static readonly FontManager Instance = new();

	/// <summary>
	/// One loaded font file. Menu fonts survive a game's Clear.
	/// </summary>
	record struct LoadedFont( SKTypeface Typeface, bool IsMenu );

	// Older addons still ask for these families by name.
	static readonly HashSet<string> LegacyIconFamilies = new( StringComparer.OrdinalIgnoreCase )
	{
		"Material Icons",
		"Material Icons Outlined"
	};

	Dictionary<(string family, int weight, int width, SKFontStyleSlant slant), LoadedFont> _loadedFonts = new();
	Dictionary<(string family, int weight, bool italic), SKTypeface> _cache = new();
	List<FileWatch> _watchers = new();

	// Only used under the loaded-fonts lock.
	SKFont _fallbackFont;

	static FontManager()
	{
		// Do a custom glyph matcher for Linux/Mac to custom handle COLRv0 Emojis
		// They dont ship with vector emoji support on a system level, let's make it find
		// A font that does.
		if ( !OperatingSystem.IsWindows() )
			FontFallback.CharacterMatcher = Instance;
	}

	/// <summary>
	/// Every family name we have a face for.
	/// </summary>
	public IEnumerable<string> FontFamilies
	{
		get
		{
			lock ( _loadedFonts )
			{
				return _loadedFonts.Keys.Select( x => x.family ).Distinct().ToList();
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
			Stream stream;
			try
			{
				stream = fileSystem.OpenRead( file );
			}
			catch ( IOException e ) when ( e is FileNotFoundException or DirectoryNotFoundException )
			{
				// A deletion or unmount can leave a queued path with nothing to load.
				continue;
			}

			Load( stream );
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

		Log.Trace( $"Loaded font {key.FamilyName} weight {key.FontWeight} width {key.FontWidth} (IsMenu: {isMenu})" );
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
			// The default mapper also returns a face for missing families, so check availability separately.
			if ( !string.IsNullOrEmpty( style.FontFamily ) )
			{
				using var systemFonts = SKFontManager.Default.GetFontStyles( style.FontFamily );
				if ( systemFonts.Count == 0 )
				{
					Log.Warning( $"FontManager: Font '{style.FontFamily}' not found" );
				}
			}

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
		var family = LegacyIconFamilies.Contains( style.FontFamily ) ? "Material Symbols Outlined" : style.FontFamily;

		lock ( _loadedFonts )
		{
			SKTypeface best = null;
			(bool wrongSlant, int widthDistance, int weightDistance, int width, SKFontStyleSlant slant) bestRank = default;

			foreach ( var (key, font) in _loadedFonts )
			{
				if ( !string.Equals( key.family, family, StringComparison.OrdinalIgnoreCase ) ) continue;

				// Prefer the requested slant, then normal width, then the closest weight. Width and slant break ties.
				bool italic = key.slant != SKFontStyleSlant.Upright;
				int widthDistance = Math.Abs( key.width - (int)SKFontStyleWidth.Normal );
				var rank = (italic != style.FontItalic, widthDistance, WeightDistance( key.weight, style.FontWeight ), key.width, key.slant);
				if ( best is not null && rank.CompareTo( bestRank ) >= 0 ) continue;

				bestRank = rank;
				best = font.Typeface;
			}

			return best;
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
			_fallbackFont?.Dispose();
			_fallbackFont = null;

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

	public SKTypeface MatchCharacter( string familyName, int weight, int width, SKFontStyleSlant slant, string[] bcp47, int character )
	{
		lock ( _loadedFonts )
		{
			var font = _fallbackFont ??= new SKFont();
			foreach ( var loaded in _loadedFonts.Values )
			{
				font.Typeface = loaded.Typeface;
				if ( font.ContainsGlyph( character ) )
					return loaded.Typeface;
			}
		}

		return SKFontManager.Default.MatchCharacter( familyName, weight, width, slant, bcp47, character );
	}
}
