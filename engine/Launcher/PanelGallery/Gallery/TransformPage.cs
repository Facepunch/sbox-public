namespace Sandbox.PanelGallery;

/// <summary>
/// CSS transforms on real panels, shown one operation at a time.
/// </summary>
public sealed class TransformPage : GalleryPage
{
	readonly Panel subject;
	readonly Panel progress;
	readonly Sandbox.UI.Label heading, description, code;
	readonly List<Sandbox.UI.Button> buttons = [];
	float time = ShapeMovie.InitialTime();
	readonly bool animate = !Environment.GetCommandLineArgs().Contains( "-pause-animation" );
	static readonly string[] Titles = ["Translate", "Rotate", "Scale", "Stretch", "Skew", "Origin", "Combine"];
	static readonly string[] Descriptions = [
		"Move a panel without moving its neighbours. The outline marks its original position.",
		"Rotate the entire panel, including its text and children, around its centre.",
		"Uniform scale keeps the panel's proportions while changing its apparent size.",
		"Scale the horizontal and vertical axes independently.",
		"Skew the panel and everything inside it.",
		"Move the transform origin to the top-left corner, then rotate around that point.",
		"Combine translation, rotation and scale in a single transform." ];

	public TransformPage() : base( "transform", "A looping tour of CSS transforms on a real panel. Choose any chapter below." )
	{
		AddClass( "transform-showcase shape-movie" );
		heading = Add.Label( "", "movie-heading" );
		description = Add.Label( "", "movie-description" );
		var stage = Add.Panel( "transform-stage" );
		stage.Add.Panel( "transform-guide" );
		subject = stage.Add.Panel( "transform-subject" );
		subject.Add.Label( "Panels", "transform-title" );
		subject.Add.Label( "Text, shapes & layout", "transform-subtitle" );
		var dots = subject.Add.Panel( "transform-dots" );
		for ( int i = 0; i < 3; i++ ) dots.Add.Panel( $"dot dot-{i}" );
		progress = Add.Panel( "movie-track" ).Add.Panel( "movie-progress" );
		code = Add.Label( "", "movie-code" );
		var chapters = Add.Panel( "movie-chapters" );
		for ( int i = 0; i < Titles.Length; i++ )
		{
			int index = i;
			var button = new Sandbox.UI.Button( Titles[i], null, "flatbutton", () => time = index * 8 );
			chapters.AddChild( button );
			buttons.Add( button );
		}
	}

	/// <summary>
	/// Keeps full float precision for rendering; shorter numbers are only used in the code caption.
	/// </summary>
	static string FormatTransform( int chapter, float u, string format )
	{
		string F( float value ) => value.ToString( format, System.Globalization.CultureInfo.InvariantCulture );
		return chapter switch
		{
			0 => $"translate({F( 120 * u )}px, {F( -45 * u )}px)",
			1 => $"rotate({F( 180 * u )}deg)",
			2 => $"scale({F( 1 + 0.45f * u )})",
			3 => $"scale({F( 1 + 0.5f * u )}, {F( 1 - 0.4f * u )})",
			4 => $"skew({F( 25 * u )}deg, 0deg)",
			5 => $"rotate({F( 40 * u )}deg)",
			_ => $"translate({F( 85 * u )}px, 0px) rotate({F( 35 * u )}deg) scale({F( 1 + 0.2f * u )})"
		};
	}

	public override void Tick()
	{
		base.Tick();
		if ( animate ) time += RealTime.Delta;
		int chapter = (int)(time / 8) % Titles.Length;
		float u = ShapeMovie.Motion( time % 8 / 8 );
		string transform = FormatTransform( chapter, u, "G9" );
		string displayedTransform = FormatTransform( chapter, u, "0.##" );
		subject.Style.Set( "transform-origin", chapter == 5 ? "0% 0%" : "50% 50%" );
		subject.Style.Set( "transform", transform );
		heading.Text = Titles[chapter];
		description.Text = Descriptions[chapter];
		code.Text = $"transform-origin: {(chapter == 5 ? "0% 0%" : "50% 50%")};\ntransform: {displayedTransform};";
		progress.Style.Width = Length.Percent( time % 8 / 8 * 100 );
		for ( int i = 0; i < buttons.Count; i++ ) buttons[i].Active = i == chapter;
	}
}
