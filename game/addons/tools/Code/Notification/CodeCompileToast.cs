using Microsoft.CodeAnalysis;

namespace Editor;

internal class CodeCompileNotice : ToastWidget
{
	CompileGroup CompilerGroup;

	public CodeCompileNotice()
	{

	}

	public CodeCompileNotice( CompileGroup compiler )
	{
		Icon = "build_circle";
		Position = 10;
		CompilerGroup = compiler;
		Reset();
	}

	protected override Vector2 SizeHint()
	{
		return 1000;
	}

	/// <summary>
	/// Called when it's about to be re-used by a new compiler
	/// </summary>
	public override void Reset()
	{
		base.Reset();

		IsRunning = true;
		Tick();
		SetBodyWidget( null );
		FixedWidth = 300;
		FixedHeight = 76;
		Title = $"Compile '{CompilerGroup.Name}'";
		BorderColor = Theme.Primary;
		isErrored = false;
	}

	bool isErrored = false;

	private readonly List<string> files = [];

	public override bool WantsVisible
	{
		get
		{
			if ( EditorPreferences.CompileNotifications == EditorPreferences.NotificationLevel.ShowAlways )
				return true;

			if ( EditorPreferences.CompileNotifications == EditorPreferences.NotificationLevel.ShowOnError )
				return isErrored;

			return false;
		}
	}

	public override void Tick()
	{
		if ( !IsRunning )
			return;

		var buildCount = CompilerGroup.Compilers.Count( x => x.IsBuilding );
		var finishCount = CompilerGroup.Compilers.Count( x => !x.IsBuilding );
		var warnings = CompilerGroup.Compilers.Where( x => x.Diagnostics != null )
						.SelectMany( x => x.Diagnostics )
						.Where( x => x.Severity >= DiagnosticSeverity.Warning )
						.Count();

		IsRunning = CompilerGroup.IsBuilding;
		Subtitle = $"{buildCount} pending, {finishCount} completed";

		if ( warnings > 0 )
			Subtitle += $", {warnings} warnings";

		if ( IsRunning )
			return;

		bool success = CompilerGroup.Compilers.All( x => x.BuildSuccess );

		files.Clear();

		if ( success )
		{
			isErrored = false;
			BorderColor = Theme.Green;
			ToastManager.Remove( this, 1 );

			if ( EditorPreferences.NotificationSounds )
				EditorUtility.PlayRawSound( "sounds/editor/success.wav" );
		}
		else
		{
			isErrored = true;
			BorderColor = Theme.Red;
			Subtitle = "";
			ToastManager.Remove( this, EditorPreferences.ErrorNotificationTimeout );
			AddDiagnostics();

			if ( EditorPreferences.NotificationSounds )
				EditorUtility.PlayRawSound( "sounds/editor/fail.wav" );
		}
	}

	protected override void OnPaint()
	{
		if ( !EditorPreferences.NotificationPopups ) return;

		base.OnPaint();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.LeftMouseButton && e.HasShift )
		{
			foreach ( var file in files )
			{
				CodeEditor.OpenFile( file );
			}

			e.Accepted = true;
			return;
		}
	}

	private void AddDiagnostics()
	{
		var diagnostics = CompilerGroup.Compilers
								.Where( x => x.Diagnostics != null )
								.SelectMany( x => x.Diagnostics )
								.Where( x => x.Severity >= DiagnosticSeverity.Error )
								.OrderByDescending( x => (int)x.Severity )
								.Take( 5 )
								.ToArray();

		if ( diagnostics.Length == 0 ) return;

		var bodyWidget = new Widget( this );
		bodyWidget.Layout = Layout.Column();

		foreach ( var diag in diagnostics )
		{
			var btn = bodyWidget.Layout.Add( new DiagnosticWidget( diag ) );
			files.Add( btn.FilePath );
		}

		SetBodyWidget( bodyWidget );
	}

	[Event( "compile.started" )]
	public static void OnCompileStarted( CompileGroup compiler )
	{
		// find an old notice to replace
		var notice = ToastManager.All.OfType<CodeCompileNotice>().FirstOrDefault( x => x.CompilerGroup.Name == compiler.Name );
		if ( !notice.IsValid() ) notice = new CodeCompileNotice( compiler );

		notice.CompilerGroup = compiler;
		notice.Reset();
	}
}

internal class DiagnosticWidget : Widget
{
	private Diagnostic diag;

	public string FilePath { get; private set; }
	public int LineNumber { get; private set; }
	public int CharNumber { get; private set; }
	public string Message { get; private set; }

	public DiagnosticWidget( Diagnostic diag ) : base( null )
	{
		this.diag = diag;
		FixedHeight = 18;
		MinimumWidth = 400;
		Cursor = CursorShape.Finger;

		ToolTip = $"{diag.GetMessage()}\n{diag.Location}";

		var span = diag.Location.GetLineSpan();
		var mappedSpan = diag.Location.GetMappedLineSpan();

		Message = diag.GetMessage();
		// Path can be null if the spans are not valid (not related to a file)
		FilePath = mappedSpan.HasMappedPath ? mappedSpan.Path : span.Path;
		LineNumber = mappedSpan.Span.Start.Line + 1;
		CharNumber = mappedSpan.Span.Start.Character + 1;
	}

	protected override void DoLayout()
	{
		base.DoLayout();
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		var color = Theme.Red;
		if ( diag.Severity == DiagnosticSeverity.Warning ) color = Theme.Yellow;
		var bareFilename = System.IO.Path.GetFileName( FilePath );
		var rect = LocalRect;

		var textColor = Color.Lerp( Color.White, color, 0.5f );

		if ( IsUnderMouse )
		{
			Paint.ClearPen();
			Paint.SetBrush( color.WithAlpha( 0.1f ) );
			Paint.DrawRect( rect );
		}

		var measureText = $"{diag.Id}: {Message}  {bareFilename}:{LineNumber}";
		var size = Paint.MeasureText( new Rect( 0, 1000 ), measureText, TextFlag.LeftCenter );

		rect = rect.Shrink( 4, 0 );

		Paint.SetPen( textColor.WithAlpha( 0.8f ) );
		var fileRect = Paint.DrawText( rect, $"{bareFilename}:{LineNumber}", TextFlag.RightCenter );

		rect.Right -= fileRect.Width;
		rect.Right -= 8;

		Paint.SetPen( textColor.WithAlpha( 0.5f ) );
		Paint.DrawText( rect.Shrink( 16, 0, 0, 0 ), $"{diag.Id}: {Message}", TextFlag.LeftCenter );

		Paint.ClearPen();
		Paint.SetBrush( color );
		Paint.DrawCircle( new Rect( 0, 16 ).Shrink( 2 ) );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			// Can be null if our diagnostic spans are not valid
			if ( FilePath != null )
			{
				CodeEditor.OpenFile( FilePath, LineNumber, CharNumber );
			}
			e.Accepted = true;
			return;
		}

		base.OnMouseClick( e );
	}
}
