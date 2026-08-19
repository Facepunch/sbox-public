using System.Reflection;

namespace Editor.Mcp;

/// <summary>
/// Inspecting the live UI - the panel tree, what the style system computed for a panel, which rules
/// matched it, what it looks like on screen, what input would hit, and what text measures to. Plus
/// trying css on a scratch panel, which is the quickest way to check a case against a browser.
/// </summary>
[McpToolset( "ui", "Live UI - panel tree, computed styles, matched rules, screenshots, hit testing, text metrics, and probing css without editing a file" )]
public static partial class UiTools
{
	/// <summary>
	/// The panel tree with each panel's screen rect, like a browser's elements pane. Every panel comes back
	/// with a path, which is how the rest of these tools address it. UI only exists while the scene runs, so
	/// play_start first.
	/// </summary>
	/// <param name="panel">Panel path like '0/3/1', or a selector like '.mainmenu button'. Empty starts at the roots.</param>
	/// <param name="depth">How many levels of children to include. ChildCount says what's below the cut.</param>
	/// <param name="styles">Include the computed style. Every property that's set, so ask for one panel.</param>
	/// <param name="styleFilter">Only computed properties whose name contains this, e.g. 'border' or 'font'.</param>
	[McpTool.ReadOnly( "ui_panel_dump" )]
	public static object PanelDump( string panel = "", [Sandbox.Range( 0, 32 )] int depth = 3, bool styles = false,
		string styleFilter = "" )
	{
		if ( string.IsNullOrWhiteSpace( panel ) )
			return RootPanels().Select( ( x, i ) => Describe( x, i.ToString(), depth, styles, styleFilter ) ).ToArray();

		var found = Locate( panel );

		return new[] { Describe( found.Panel, found.Path, depth, styles, styleFilter ) };
	}

	/// <summary>
	/// Every style rule that matched a panel, in the order the style system applied them - so for any
	/// property, the last rule to set it is the one that won. Each rule reports its selector, the file
	/// and line it came from, and its declarations. Anything set on the panel directly comes back as
	/// Inline, which beats all of them. This is how you find out why a panel doesn't look like the css
	/// you wrote.
	/// </summary>
	/// <param name="panel">Panel path from ui_panel_dump, or a selector. Empty uses the first root.</param>
	[McpTool.ReadOnly( "ui_matched_rules" )]
	public static object MatchedRules( string panel = "" )
	{
		var found = Locate( panel );

		return new
		{
			found.Path,
			Panel = Signature( found.Panel ),
			Inline = ComputedStyles( found.Panel.Style, "" ),
			Rules = found.Panel.ActiveStyleBlocks.Select( x => new
			{
				Selector = string.Join( ", ", x.SelectorStrings ),
				Source = x.FileName is null ? null : $"{x.FileName}:{x.FileLine}",
				Declarations = x.GetRawValues().Select( v => $"{v.Name}: {v.Value}" ).ToArray()
			} ).ToArray()
		};
	}

	/// <summary>
	/// A screenshot of one panel, cropped to its screen rect, so you can look at what a panel actually
	/// draws without hunting for it in a full frame. Renders through the running scene's camera.
	/// </summary>
	/// <param name="panel">Panel path from ui_panel_dump, or a selector. Empty uses the first root.</param>
	/// <param name="padding">Extra pixels around the panel, for seeing shadows and outlines.</param>
	[McpTool.ReadOnly( "ui_screenshot" )]
	public static object Screenshot( string panel = "", [Sandbox.Range( 0, 512 )] int padding = 0 )
	{
		var found = Locate( panel );
		var rect = found.Panel.Box.Rect;

		if ( rect.Width < 1 || rect.Height < 1 )
			throw new Exception( $"'{Signature( found.Panel )}' has no size - it isn't laid out, or it's hidden" );

		return McpResult.Image( Capture( rect, padding ) ).WithText( new
		{
			found.Path,
			Panel = Signature( found.Panel ),
			Rect = Describe( rect )
		} );
	}

	/// <summary>
	/// Draw a scratch panel with the css you give it, measure it, screenshot it, then throw it away. This is
	/// how you try a declaration without editing a file and waiting for a hotload - state a case, see what it
	/// renders to, compare it against a browser. The sample is a 100x100 block, or a label when you give it
	/// text, both inset in a cell like tools/uiparity draws them.
	/// </summary>
	/// <param name="css">The css to try, e.g. 'border-radius: 20px; background-color: #e8b04a'.</param>
	/// <param name="text">Text to draw, which makes the sample a label instead of a block.</param>
	/// <param name="background">Colour behind the sample, so anything with alpha reads against something known.</param>
	/// <param name="points">Pixels to read, as 'x,y x,y' relative to the sample. Empty reads its centre.</param>
	/// <param name="screenshot">Return an image as well as the numbers.</param>
	[McpTool( "ui_probe" )]
	public static async Task<McpResult> Probe( string css, string text = "", string background = "#101010",
		string points = "", bool screenshot = true )
	{
		if ( string.IsNullOrWhiteSpace( css ) && string.IsNullOrWhiteSpace( text ) )
			throw new Exception( "Give some css to probe" );

		var cell = AddProbeCell( RootPanels()[0], 0, 0, background );

		try
		{
			var sample = AddProbeSample( cell, css, text );

			await NextFrame();

			var frame = CaptureFrame();

			var measured = new
			{
				UiScale = UiScale( cell ),
				Sample = Describe( Relative( sample.Box.Rect, cell.Box.Rect ) ),
				Cell = Describe( cell.Box.Rect ),
				Pixels = ReadPixels( frame, sample.Box.Rect, points ),
				Inline = ComputedStyles( sample.Style, "" )
			};

			return screenshot
				? McpResult.Image( Crop( frame, cell.Box.Rect, 0 ) ).WithText( measured )
				: McpResult.Text( measured );
		}
		finally
		{
			cell.Delete( true );
		}
	}

	/// <summary>
	/// Probe a whole list of css cases at once - one cell each, laid out in a grid, measured together and
	/// captured as a single contact sheet. This is the parity sweep: state the cases, get every measurement
	/// back in one call, then diff them against the same cases in a browser.
	/// </summary>
	/// <param name="cases">One css case per line. Lines starting with # are ignored, so a case file pastes straight in.</param>
	/// <param name="text">Text to draw, which makes every sample a label instead of a block.</param>
	/// <param name="background">Colour behind the samples.</param>
	/// <param name="columns">Cells per row. 0 fits as many as the screen holds.</param>
	/// <param name="points">Pixels to read from every sample, as 'x,y x,y'. Empty reads their centres.</param>
	/// <param name="screenshot">Return the contact sheet as well as the numbers.</param>
	[McpTool( "ui_probe_batch" )]
	public static async Task<McpResult> ProbeBatch( string cases, string text = "", string background = "#101010",
		[Sandbox.Range( 0, 24 )] int columns = 0, string points = "", bool screenshot = true )
	{
		var list = (cases ?? "").Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
			.Where( x => !x.StartsWith( '#' ) )
			.ToArray();

		if ( list.Length == 0 )
			throw new Exception( "Give one css case per line" );

		var screen = Screen.Size;
		var across = columns > 0 ? columns : Math.Max( 1, (int)(screen.x / ProbeCell) );
		var down = (list.Length + across - 1) / across;

		if ( down * ProbeCell > screen.y )
		{
			var fits = Math.Max( 1, (int)(screen.y / ProbeCell) ) * across;
			throw new Exception( $"{list.Length} cases at {across} across won't fit on screen - send {fits} at a time" );
		}

		var grid = RootPanels()[0].AddChild<Panel>( "mcp-probe" );

		try
		{
			var wide = Math.Min( list.Length, across ) * ProbeCell;

			grid.Style.Set( $"position: absolute; left: 0px; top: 0px; width: {wide}px; height: {down * ProbeCell}px" );

			var cells = new List<(Panel Cell, Panel Sample, string Css)>();

			for ( int i = 0; i < list.Length; i++ )
			{
				var cell = AddProbeCell( grid, i % across * ProbeCell, i / across * ProbeCell, background );

				cells.Add( (cell, AddProbeSample( cell, list[i], text ), list[i]) );
			}

			await NextFrame();

			var frame = CaptureFrame();

			var measured = new
			{
				UiScale = UiScale( grid ),
				Cases = cells.Select( ( x, i ) => new
				{
					Index = i,
					Css = x.Css,
					Cell = Describe( x.Cell.Box.Rect ),
					Sample = Describe( Relative( x.Sample.Box.Rect, x.Cell.Box.Rect ) ),
					Pixels = ReadPixels( frame, x.Sample.Box.Rect, points )
				} ).ToArray()
			};

			return screenshot
				? McpResult.Image( Crop( frame, grid.Box.Rect, 0 ) ).WithText( measured )
				: McpResult.Text( measured );
		}
		finally
		{
			grid.Delete( true );
		}
	}

	/// <summary>
	/// Read exact pixel values out of a panel - the numbers behind ui_screenshot, for when looking at an image
	/// isn't an answer. Points are relative to the panel's top left, which is what makes them comparable to a
	/// browser's readout of the same case. Values are 0-255, straight off the rendered frame.
	/// </summary>
	/// <param name="panel">Panel path from ui_panel_dump, or a selector. Empty uses the first root.</param>
	/// <param name="points">Points to read, as 'x,y x,y x,y'. Empty reads the panel's centre.</param>
	[McpTool.ReadOnly( "ui_pixels" )]
	public static object Pixels( string panel = "", string points = "" )
	{
		var found = Locate( panel );
		var rect = found.Panel.Box.Rect;

		if ( rect.Width < 1 || rect.Height < 1 )
			throw new Exception( $"'{Signature( found.Panel )}' has no size - it isn't laid out, or it's hidden" );

		return new
		{
			found.Path,
			Panel = Signature( found.Panel ),
			Rect = Describe( rect ),
			Pixels = ReadPixels( CaptureFrame(), rect, points )
		};
	}

	/// <summary>
	/// Set css on a live panel, the way a browser's style editor does - change it, look at it, change it again,
	/// with no file edit and no hotload in between. This is an inline style, so it beats every rule that matched
	/// and it stays until that panel rebuilds. There's no way to take a declaration back off a panel - 'revert'
	/// drops it to the initial value, like the web, not back to what the stylesheet said - so put the original
	/// value back yourself, or use ui_probe when you want something that cleans up after itself.
	/// </summary>
	/// <param name="panel">Panel path from ui_panel_dump, or a selector.</param>
	/// <param name="css">The css to set, e.g. 'border-radius: 20px'.</param>
	[McpTool( "ui_set_style" )]
	public static async Task<object> SetStyle( string panel, string css )
	{
		if ( string.IsNullOrWhiteSpace( css ) )
			throw new Exception( "Give some css to set" );

		var found = Locate( panel );

		found.Panel.Style.Set( css );

		await NextFrame();

		return new
		{
			found.Path,
			Panel = Signature( found.Panel ),
			Rect = Describe( found.Panel.Box.Rect ),
			Inline = ComputedStyles( found.Panel.Style, "" )
		};
	}

	/// <summary>
	/// Which panels are under a point on screen, topmost first. This asks the same question input does, corner
	/// radii and all, so it answers both "would a click land here" and "what is eating my clicks" - the first
	/// hit whose pointer-events isn't None is the one that takes it.
	/// </summary>
	/// <param name="x">Screen x in pixels.</param>
	/// <param name="y">Screen y in pixels.</param>
	[McpTool.ReadOnly( "ui_hit_test" )]
	public static object HitTest( float x, float y )
	{
		var position = new Vector2( x, y );
		var roots = RootPanels();
		var hits = new List<object>();

		for ( int i = 0; i < roots.Count; i++ )
		{
			foreach ( var panel in roots[i].Descendants.Prepend( roots[i] ) )
			{
				if ( !panel.IsInside( position ) ) continue;

				hits.Add( new
				{
					Path = PathOf( panel, roots[i], i ),
					Panel = Signature( panel ),
					Rect = Describe( panel.Box.Rect ),
					PointerEvents = panel.ComputedStyle?.PointerEvents.ToString()
				} );
			}
		}

		hits.Reverse();

		return new { X = x, Y = y, Hits = hits };
	}

	/// <summary>
	/// Force a pseudo class on a panel - hover, active, focus - so its styles can be looked at and screenshotted
	/// without a mouse. Clears again with on false.
	/// </summary>
	/// <param name="panel">Panel path from ui_panel_dump, or a selector.</param>
	/// <param name="pseudo">Which one, e.g. 'Hover'.</param>
	/// <param name="on">Set it, or clear it.</param>
	[McpTool( "ui_set_pseudo" )]
	public static async Task<object> SetPseudo( string panel, string pseudo, bool on = true )
	{
		if ( !Enum.TryParse<PseudoClass>( pseudo, true, out var flag ) )
			throw new Exception( $"'{pseudo}' isn't a pseudo class - it's one of {string.Join( ", ", Enum.GetNames<PseudoClass>() )}" );

		var found = Locate( panel );

		found.Panel.PseudoClass = on ? found.Panel.PseudoClass | flag : found.Panel.PseudoClass & ~flag;

		await NextFrame();

		return new
		{
			found.Path,
			Panel = Signature( found.Panel ),
			PseudoClass = found.Panel.PseudoClass.ToString(),
			Rect = Describe( found.Panel.Box.Rect )
		};
	}

	/// <summary>
	/// What a piece of text measures to, through the same text stack the UI draws with. Give a wrap width
	/// to measure it wrapped - Lines is how many lines it came out as.
	/// </summary>
	/// <param name="text">The text to measure.</param>
	/// <param name="font">Font family name, e.g. 'Poppins'. Unknown fonts fall back silently.</param>
	/// <param name="size">Font size in pixels.</param>
	/// <param name="weight">Font weight, 100 to 900.</param>
	/// <param name="italic">Measure the italic face.</param>
	/// <param name="lineHeight">Line height as a multiple of the font size.</param>
	/// <param name="letterSpacing">Extra pixels between letters.</param>
	/// <param name="wordSpacing">Extra pixels between words.</param>
	/// <param name="wrapWidth">Width to wrap at, in pixels. 0 measures a single line.</param>
	[McpTool.ReadOnly( "text_measure" )]
	public static object TextMeasure( string text, string font = "Poppins", float size = 16, int weight = 400,
		bool italic = false, float lineHeight = 1, float letterSpacing = 0, float wordSpacing = 0,
		[Sandbox.Range( 0, 8192 )] int wrapWidth = 0 )
	{
		if ( string.IsNullOrEmpty( text ) )
			throw new Exception( "Give some text to measure" );

		var scope = TextRendering.Scope.Default;

		scope.Text = text;
		scope.FontName = font;
		scope.FontSize = size;
		scope.FontWeight = weight;
		scope.FontItalic = italic;
		scope.LineHeight = lineHeight;
		scope.LetterSpacing = letterSpacing;
		scope.WordSpacing = wordSpacing;

		var single = scope.Measure();

		if ( single.y <= 0 )
			throw new Exception( "That measured to nothing - is the editor rendering?" );

		var measured = wrapWidth > 0
			? TextRendering.GetOrCreateTexture( scope, new Vector2( wrapWidth, 8096 ) ).Size
			: single;

		return new
		{
			Width = Round( measured.x ),
			Height = Round( measured.y ),
			SingleLineWidth = Round( single.x ),
			SingleLineHeight = Round( single.y ),
			Lines = (int)MathF.Round( measured.y / single.y )
		};
	}

	static object Describe( Panel panel, string path, int depth, bool styles, string styleFilter )
	{
		return new
		{
			Path = path,
			Panel = Signature( panel ),
			Type = panel.GetType().Name,
			Rect = Describe( panel.Box.Rect ),
			Text = (panel as Sandbox.UI.Label)?.Text,
			Styles = styles ? ComputedStyles( panel.ComputedStyle, styleFilter ) : null,
			ChildCount = panel.Children.Count(),
			Children = depth <= 0
				? null
				: panel.Children.Select( ( x, i ) => Describe( x, $"{path}/{i}", depth - 1, styles, styleFilter ) ).ToArray()
		};
	}

	/// <summary>The cell a probe draws in, the same size tools/uiparity uses.</summary>
	const int ProbeCell = 160;

	/// <summary>Styles and layout land on a later frame, so wait for a few and come back to the main thread.</summary>
	static async Task NextFrame()
	{
		await Task.Delay( 50 );
		await MainThread.Wait();
	}

	/// <summary>One cell of a probe - a known colour behind the sample, positioned in the root.</summary>
	static Panel AddProbeCell( Panel parent, float x, float y, string background )
	{
		var cell = parent.AddChild<Panel>( "mcp-probe" );

		cell.Style.Set( $"position: absolute; left: {x}px; top: {y}px; width: {ProbeCell}px; height: {ProbeCell}px; background-color: {background}" );

		return cell;
	}

	/// <summary>
	/// The sample inside a probe cell - a 100x100 block, or a label when there's text. These defaults are
	/// the .box and .text rules tools/uiparity gives the browser side, so a case that sets neither colour
	/// nor size draws the same in both.
	/// </summary>
	static Panel AddProbeSample( Panel cell, string css, string text )
	{
		var sample = string.IsNullOrEmpty( text ) ? cell.AddChild<Panel>() : cell.AddChild<Sandbox.UI.Label>();

		if ( sample is Sandbox.UI.Label label ) label.Text = text;

		sample.Style.Set( string.IsNullOrEmpty( text )
			? "position: absolute; left: 30px; top: 30px; width: 100px; height: 100px; background-color: #e8b04a"
			: "position: absolute; left: 10px; top: 10px; width: 140px; font-family: Poppins; font-size: 16px; color: #ffffff" );

		if ( !string.IsNullOrWhiteSpace( css ) )
			sample.Style.Set( css );

		return sample;
	}

	static IEnumerable<Vector2> ParsePoints( string points )
	{
		foreach ( var point in points.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var parts = point.Split( ',' );

			if ( parts.Length != 2 || !float.TryParse( parts[0], out var x ) || !float.TryParse( parts[1], out var y ) )
				throw new Exception( $"'{point}' isn't a point - they go 'x,y x,y'" );

			yield return new Vector2( x, y );
		}
	}

	/// <summary>
	/// Pixel values at points inside a rect, read straight out of the frame rather than a crop, so a panel
	/// on a fractional boundary doesn't get resampled on the way. No points means the centre.
	/// </summary>
	static object[] ReadPixels( Bitmap frame, Rect rect, string points )
	{
		var wanted = string.IsNullOrWhiteSpace( points )
			? [new Vector2( MathF.Floor( rect.Width / 2 ), MathF.Floor( rect.Height / 2 ) )]
			: ParsePoints( points );

		var pixels = frame.GetPixels();
		var origin = new Vector2( MathF.Round( rect.Left ), MathF.Round( rect.Top ) );

		return wanted.Select( point =>
		{
			var x = (int)(origin.x + point.x);
			var y = (int)(origin.y + point.y);

			if ( x < 0 || y < 0 || x >= frame.Width || y >= frame.Height )
				throw new Exception( $"{point.x},{point.y} isn't on screen - the rect it's measured from is at {Round( rect.Left )},{Round( rect.Top )}" );

			return Describe( point, pixels[y * frame.Width + x] );
		} ).ToArray();
	}

	/// <summary>
	/// What the root scales its px by. Anything but 1 means a probe's 160px cell isn't 160 screen pixels,
	/// so its measurements can't be held against a browser's until they're divided through by this.
	/// </summary>
	static float UiScale( Panel panel )
	{
		for ( var p = panel; p is not null; p = p.Parent )
		{
			if ( p is RootPanel root ) return Round( root.Scale );
		}

		return 1f;
	}

	static Rect Relative( Rect rect, Rect origin ) =>
		new( rect.Left - origin.Left, rect.Top - origin.Top, rect.Width, rect.Height );

	static Bitmap CaptureFrame()
	{
		var camera = Game.ActiveScene?.Camera
			?? throw new Exception( "The running scene has no camera to render through" );

		var screen = Screen.Size;
		var frame = new Bitmap( (int)screen.x, (int)screen.y );
		camera.RenderToBitmap( frame, true );

		return frame;
	}

	static Bitmap Capture( Rect rect, int padding ) => Crop( CaptureFrame(), rect, padding );

	static Bitmap Crop( Bitmap frame, Rect rect, int padding )
	{
		var screen = Screen.Size;

		var left = Math.Clamp( rect.Left - padding, 0, screen.x );
		var top = Math.Clamp( rect.Top - padding, 0, screen.y );
		var right = Math.Clamp( rect.Right + padding, left, screen.x );
		var bottom = Math.Clamp( rect.Bottom + padding, top, screen.y );

		if ( right - left < 1 || bottom - top < 1 )
			throw new Exception( $"Nothing to capture - that rect is off screen at {rect.Left},{rect.Top}" );

		return frame.Crop( new Rect( left, top, right - left, bottom - top ) );
	}

	static object Describe( Rect rect ) => new
	{
		X = Round( rect.Left ),
		Y = Round( rect.Top ),
		W = Round( rect.Width ),
		H = Round( rect.Height )
	};

	static object Describe( Vector2 point, Color color ) => new
	{
		X = Round( point.x ),
		Y = Round( point.y ),
		Hex = $"#{Byte( color.r ):X2}{Byte( color.g ):X2}{Byte( color.b ):X2}",
		R = Byte( color.r ),
		G = Byte( color.g ),
		B = Byte( color.b ),
		A = Byte( color.a )
	};

	static int Byte( float channel ) => (int)MathF.Round( channel.Clamp( 0f, 1f ) * 255f );

	/// <summary>How a panel reads as a selector, like "div#ok.button.primary".</summary>
	static string Signature( Panel panel )
	{
		var element = string.IsNullOrEmpty( panel.ElementName ) ? panel.GetType().Name : panel.ElementName;
		var id = string.IsNullOrEmpty( panel.Id ) ? "" : $"#{panel.Id}";

		return $"{element}{id}{string.Concat( panel.Class.Select( x => $".{x}" ) )}";
	}

	/// <summary>Shorthands, which only repeat what the longhands say.</summary>
	static readonly string[] NotStyleProperties = ["Padding", "Margin", "BorderWidth", "BorderColor", "Transitions"];

	/// <summary>Has* and Is* flags are questions about the style, not properties of it.</summary>
	static bool IsStyleFlag( PropertyInfo property )
	{
		return property.PropertyType == typeof( bool )
			&& (property.Name.StartsWith( "Has" ) || property.Name.StartsWith( "Is" ));
	}

	static Dictionary<string, string> ComputedStyles( Styles style, string filter )
	{
		if ( style is null ) return null;

		var result = new Dictionary<string, string>();

		foreach ( var property in typeof( Styles ).GetProperties() )
		{
			if ( property.GetMethod is null || property.GetMethod.IsStatic ) continue;
			if ( property.GetIndexParameters().Length > 0 ) continue;
			if ( NotStyleProperties.Contains( property.Name ) || IsStyleFlag( property ) ) continue;

			var name = CssName( property.Name );

			if ( !string.IsNullOrWhiteSpace( filter ) && !name.Contains( filter, StringComparison.OrdinalIgnoreCase ) )
				continue;

			if ( Format( property.GetValue( style ) ) is not { } text ) continue;

			result[name] = text;
		}

		return result.OrderBy( x => x.Key ).ToDictionary( x => x.Key, x => x.Value );
	}

	static string Format( object value )
	{
		if ( value is null ) return null;

		if ( value is PanelTransform transform )
		{
			return transform.IsEmpty() ? null : string.Join( ", ", transform.List.Select( Format ) );
		}

		if ( value is System.Collections.IEnumerable list and not string )
		{
			var items = list.Cast<object>().Select( x => x?.ToString() ).ToArray();

			return items.Length == 0 ? null : string.Join( ", ", items );
		}

		return value.ToString();
	}

	static string Format( PanelTransform.Entry entry )
	{
		return entry.Type == PanelTransform.EntryType.Translate
			? $"Translate( {entry.X}, {entry.Y}, {entry.Z} )"
			: $"{entry.Type}( {entry.Data} )";
	}

	/// <summary>MinWidth becomes min-width.</summary>
	static string CssName( string property )
	{
		var name = new System.Text.StringBuilder();

		foreach ( var c in property )
		{
			if ( char.IsUpper( c ) && name.Length > 0 ) name.Append( '-' );
			name.Append( char.ToLowerInvariant( c ) );
		}

		return name.ToString();
	}

	static float Round( float value ) => MathF.Round( value, 2 );

	static List<Panel> RootPanels()
	{
		var scene = Game.ActiveScene ?? throw new Exception( "No scene is running - play_start first" );

		var panels = new List<Panel>();

		foreach ( var screen in scene.GetAllComponents<ScreenPanel>() )
		{
			if ( screen.GetPanel() is { } root ) panels.Add( root );
		}

		foreach ( var world in scene.GetAllComponents<Sandbox.WorldPanel>() )
		{
			if ( world.GetPanel() is { } root ) panels.Add( root );
		}

		if ( panels.Count == 0 )
			throw new Exception( "The scene has no UI - a ScreenPanel or WorldPanel builds its panel when the scene runs" );

		return panels;
	}

	static (Panel Panel, string Path) Locate( string query )
	{
		var roots = RootPanels();

		if ( string.IsNullOrWhiteSpace( query ) )
			return (roots[0], "0");

		if ( query.All( x => char.IsDigit( x ) || x == '/' ) )
			return (WalkPath( roots, query ), query);

		for ( int i = 0; i < roots.Count; i++ )
		{
			foreach ( var panel in roots[i].Descendants.Prepend( roots[i] ) )
			{
				if ( Matches( panel, query ) )
					return (panel, PathOf( panel, roots[i], i ));
			}
		}

		throw new Exception( $"No panel matches '{query}' - ui_panel_dump lists what's there" );
	}

	static Panel WalkPath( List<Panel> roots, string path )
	{
		var parts = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );

		if ( parts.Length == 0 || !int.TryParse( parts[0], out var index ) || index < 0 || index >= roots.Count )
			throw new Exception( $"There are {roots.Count} root panels, so '{path}' doesn't start anywhere" );

		var panel = roots[index];

		for ( int i = 1; i < parts.Length; i++ )
		{
			var children = panel.Children.ToList();

			if ( !int.TryParse( parts[i], out var child ) || child < 0 || child >= children.Count )
				throw new Exception( $"'{Signature( panel )}' has {children.Count} children, so '{path}' doesn't exist" );

			panel = children[child];
		}

		return panel;
	}

	static string PathOf( Panel panel, Panel root, int rootIndex )
	{
		var parts = new List<string> { rootIndex.ToString() };

		for ( var p = panel; p is not null && p != root && p.Parent is not null; p = p.Parent )
		{
			parts.Insert( 1, p.Parent.Children.ToList().IndexOf( p ).ToString() );
		}

		return string.Join( "/", parts );
	}

	/// <summary>
	/// A small slice of css selector - element, .class and #id, chained by spaces for descendants. The
	/// element part also matches the panel's C# type name, which is how razor panels are named.
	/// </summary>
	static bool Matches( Panel panel, string selector )
	{
		var parts = selector.Split( ' ', StringSplitOptions.RemoveEmptyEntries );

		if ( parts.Length == 0 || !MatchesPart( panel, parts[^1] ) ) return false;

		var ancestor = panel.Parent;

		for ( int i = parts.Length - 2; i >= 0; i-- )
		{
			while ( ancestor is not null && !MatchesPart( ancestor, parts[i] ) )
			{
				ancestor = ancestor.Parent;
			}

			if ( ancestor is null ) return false;

			ancestor = ancestor.Parent;
		}

		return true;
	}

	static bool MatchesPart( Panel panel, string part )
	{
		foreach ( var token in Tokens( part ) )
		{
			var name = token[1..];

			if ( token[0] == '#' && !string.Equals( panel.Id, name, StringComparison.OrdinalIgnoreCase ) )
				return false;

			if ( token[0] == '.' && !panel.Class.Contains( name, StringComparer.OrdinalIgnoreCase ) )
				return false;

			if ( token[0] != '#' && token[0] != '.'
				&& !string.Equals( panel.ElementName, token, StringComparison.OrdinalIgnoreCase )
				&& !string.Equals( panel.GetType().Name, token, StringComparison.OrdinalIgnoreCase ) )
				return false;
		}

		return true;
	}

	static IEnumerable<string> Tokens( string part )
	{
		var start = 0;

		for ( int i = 1; i <= part.Length; i++ )
		{
			if ( i < part.Length && part[i] != '.' && part[i] != '#' ) continue;

			yield return part[start..i];

			start = i;
		}
	}
}
