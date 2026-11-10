using System.Globalization;

namespace Sandbox.PanelGallery;

/// <summary>
/// A chapter-based Painter demonstration. Each chapter holds its endpoints, eases one
/// parameter out and back, then advances. The page owns the clock so pause freezes everything.
/// </summary>
internal sealed partial class ShapeMovie : Panel
{
	internal delegate void DrawFeature( Painter painter, Rect bounds, float u );
	internal sealed record Chapter( string Title, string Description, string Code, DrawFeature Draw );
	readonly List<Chapter> _chapters;
	readonly Func<float> _time;
	readonly Action<float> _seek;
	readonly Sandbox.UI.Label _heading;
	readonly Sandbox.UI.Label _description;
	readonly Sandbox.UI.Label _code;
	readonly Panel _progress;
	readonly List<Sandbox.UI.Button> _buttons = [];
	const float ChapterDuration = 8;
	int _lastChapter = -1;

	internal ShapeMovie( string shape, Func<float> time, Action<float> seek )
	{
		_time = time;
		_seek = seek;
		_chapters = Chapters( shape );
		AddClass( "shape-movie" );
		_heading = Add.Label( "", "movie-heading" );
		_description = Add.Label( "", "movie-description" );
		var canvas = new MovieCanvas( this );
		canvas.AddClass( "movie-canvas" );
		AddChild( canvas );
		_progress = Add.Panel( "movie-track" ).Add.Panel( "movie-progress" );
		_code = Add.Label( "", "movie-code" );
		var chapters = Add.Panel( "movie-chapters" );
		for ( int i = 0; i < _chapters.Count; i++ )
		{
			int index = i;
			var button = new Sandbox.UI.Button( _chapters[i].Title, null, "flatbutton", () => SeekChapter( index ) );
			chapters.AddChild( button );
			_buttons.Add( button );
		}
		var exampleActions = Add.Panel( "demo-actions" );
		var completeCode = UsageExample( shape );
		var completeExample = Add.Panel( "complete-example hidden" );
		completeExample.Add.Label( completeCode, "complete-example-code" );
		exampleActions.AddChild( new Sandbox.UI.Button( "Show example", "code", "flatbutton", () => completeExample.SetClass( "hidden", !completeExample.HasClass( "hidden" ) ) ) );
		exampleActions.AddChild( new Sandbox.UI.Button( "Copy example", "content_copy", "flatbutton", () => EditorUtility.Clipboard.Copy( completeCode ) ) );
		UpdateCaption();
	}

	int Index => (int)(_time() / ChapterDuration) % _chapters.Count;
	float Progress => _time() % ChapterDuration / ChapterDuration;

	void SeekChapter( int index ) => _seek( ((index + _chapters.Count) % _chapters.Count) * ChapterDuration );

	/// <summary>
	/// A cosine out-and-back with a short hold at each end. Adjacent chapters begin and end
	/// at their neutral pose, making each change readable rather than perpetual random motion.
	/// </summary>
	internal static float Motion( float progress )
	{
		float phase = Math.Clamp( (progress - 0.12f) / 0.76f, 0, 1 );
		return (1 - MathF.Cos( phase * MathF.Tau )) * 0.5f;
	}

	/// <summary>
	/// Allows a paused, repeatable chapter capture with -showcase-time seconds.
	/// </summary>
	internal static float InitialTime()
	{
		var args = Environment.GetCommandLineArgs();
		int index = Array.IndexOf( args, "-showcase-time" );
		return index >= 0 && index + 1 < args.Length && float.TryParse( args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var time )
			&& float.IsFinite( time ) ? Math.Max( 0, time ) : 0;
	}

	public override void Tick()
	{
		base.Tick();
		UpdateCaption();
	}

	void UpdateCaption()
	{
		int index = Index;
		_progress.Style.Width = Length.Percent( Progress * 100 );
		if ( index == _lastChapter ) return;
		_lastChapter = index;
		var chapter = _chapters[index];
		_heading.Text = chapter.Title;
		_description.Text = chapter.Description;
		_code.Text = "// u eases from 0 to 1 and back during this chapter.\n" + chapter.Code;
		for ( int i = 0; i < _buttons.Count; i++ ) _buttons[i].Active = i == index;
	}

	/// <summary>
	/// Draws in a fixed 640×320 design space fitted to the canvas. A scope contains every
	/// chapter's transforms and stroke state; guides and parameter motion share that space.
	/// </summary>
	sealed class MovieCanvas( ShapeMovie movie ) : Panel
	{
		public override void OnDraw( Painter painter )
		{
			using var scope = painter.Scope();
			var bounds = Box.RectInner;
			bounds.Position -= Box.Rect.Position;
			painter.Fill = (Color)"#101a29";
			painter.Rect( bounds, 10 );
			float scale = MathF.Min( bounds.Width / 640, bounds.Height / 320 );
			if ( scale <= 0 ) return;
			painter.Translate( bounds.Center - new Vector2( 320, 160 ) * scale );
			painter.Scale( scale );
			painter.Stroke = Stroke.Solid( ((Color)"#ffffff").WithAlpha( 0.045f ), 1 );
			for ( int x = 0; x <= 640; x += 32 ) painter.Line( new Vector2( x, 0 ), new Vector2( x, 320 ) );
			for ( int y = 0; y <= 320; y += 32 ) painter.Line( new Vector2( 0, y ), new Vector2( 640, y ) );
			painter.Fill = Cyan;
			painter.Stroke = Stroke.None;
			movie._chapters[movie.Index].Draw( painter, new Rect( 160, 70, 320, 180 ), Motion( movie.Progress ) );
		}
	}
}
