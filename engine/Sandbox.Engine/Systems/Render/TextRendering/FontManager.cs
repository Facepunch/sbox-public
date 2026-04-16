using Sandbox.Engine;
using SkiaSharp;
using System.Diagnostics.CodeAnalysis;
using Topten.RichTextKit;

namespace Sandbox;

file class SKTypefaceEqualityComparer : IEqualityComparer<SKTypeface>
{
	public bool Equals( SKTypeface x, SKTypeface y )
	{
		return x.FamilyName == y.FamilyName
			&& x.FontWeight == y.FontWeight
			&& x.FontSlant == y.FontSlant;
	}

	public int GetHashCode( [DisallowNull] SKTypeface obj )
	{
		return HashCode.Combine( obj.FamilyName, obj.FontWeight, obj.FontSlant );
	}
}

internal class FontManager : FontMapper
{
	public static FontManager Instance = new FontManager();

	public IEnumerable<string> FontFamilies
	{
		get
		{
			lock ( LoadedFonts )
			{
				return LoadedFonts.Values.Select( x => x.Typeface.FamilyName ).Distinct().ToList();
			}
		}
	}

	struct LoadedTypeface
	{
		public SKTypeface Typeface;
		public bool IsMenu;
	}
	Dictionary<int, LoadedTypeface> LoadedFonts = new();
	Dictionary<int, SKTypeface> Cache = new();

	private void Load( System.IO.Stream stream )
	{
		if ( stream == null ) return;

		var face = SKTypeface.FromStream( stream );
		if ( face is null ) return;

		bool isMenu = GlobalContext.Current == GlobalContext.Menu;

		var hash = HashCode.Combine( face.FamilyName, face.FontWeight, face.FontSlant );
		lock ( LoadedFonts )
		{
			if ( LoadedFonts.ContainsKey( hash ) )
			{
				face.Dispose();
				return;
			}

			LoadedFonts[hash] = new()
			{
				Typeface = face,
				IsMenu = isMenu
			};
		}

		Log.Trace( $"Loaded font {face.FamilyName} weight {face.FontWeight} (IsMenu: {isMenu})" );
	}

	List<FileWatch> watchers = new();

	public void LoadAll( BaseFileSystem fileSystem )
	{
		lock ( Cache )
		{
			// empty cache new fonts can replace fallbacks and best-matches
			Cache.Clear();
		}

		var fontFiles = fileSystem.FindFile( "/fonts/", "*.ttf", true )
			.Union( fileSystem.FindFile( "/fonts/", "*.otf", true ) );

		Parallel.ForEach( fontFiles, ( string font ) =>
		{
			Load( fileSystem.OpenRead( $"/fonts/{font}" ) );
		} );

		// Load any new fonts
		var ttfWatch = fileSystem.Watch( $"*.ttf" );
		ttfWatch.OnChanges += ( w ) => OnFontFilesChanged( w, fileSystem );
		var otfWatch = fileSystem.Watch( $"*.otf" );
		otfWatch.OnChanges += ( w ) => OnFontFilesChanged( w, fileSystem );

		watchers.Add( ttfWatch );
		watchers.Add( otfWatch );
	}

	private void OnFontFilesChanged( FileWatch w, BaseFileSystem fs )
	{
		lock ( Cache )
		{
			Cache.Clear();
		}

		foreach ( var file in w.Changes )
		{
			Load( fs.OpenRead( file ) );
		}
	}

	/// <summary>
	/// Tries to get the best matching font for the given style.
	/// Will return a matching font family with the closest font weight and optionally slant.
	/// </summary>
	private SKTypeface GetBestTypeface( IStyle style )
	{
		lock ( LoadedFonts )
		{
			// Must be of same family
			var familyFonts = LoadedFonts.Values.Select( x => x.Typeface )
			.Where( x => string.Equals( x.FamilyName, style.FontFamily, StringComparison.OrdinalIgnoreCase ) );
			if ( !familyFonts.Any() ) return null;

			// Get matching slants, if no matching fallback to regular
			var slantFonts = familyFonts.Where( x => x.IsItalic == style.FontItalic );
			if ( slantFonts.Any() ) familyFonts = slantFonts;

			// Finally get the closest font weight
			return familyFonts.Select( x => new { x, distance = Math.Abs( x.FontWeight - style.FontWeight ) } )
				.OrderBy( x => x.distance )
				.First().x;
		}
	}

	public override SKTypeface TypefaceFromStyle( IStyle style, bool ignoreFontVariants )
	{
		var hash = HashCode.Combine( style.FontFamily, style.FontWeight, style.FontItalic );

		lock ( Cache )
		{
			if ( Cache.TryGetValue( hash, out var cachedFace ) ) return cachedFace;
		}

		var f = GetBestTypeface( style );

		// Fallback on system font
		f ??= Default.TypefaceFromStyle( style, ignoreFontVariants );

		lock ( Cache )
		{
			Cache[hash] = f;
		}

		return f;
	}

	public void Clear( bool removeMenu )
	{
		foreach ( var watcher in watchers )
		{
			watcher.Dispose();
		}
		watchers.Clear();

		lock ( LoadedFonts )
		{
			foreach ( var (hash, font) in LoadedFonts.ToArray() )
			{
				if ( !removeMenu && font.IsMenu )
					continue;

				LoadedFonts.Remove( hash );
				font.Typeface?.Dispose();
			}
		}

		lock ( Cache )
		{
			Cache.Clear();
		}
	}
}

