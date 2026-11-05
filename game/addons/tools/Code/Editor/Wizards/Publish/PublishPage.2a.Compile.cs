namespace Editor.Wizards;

partial class PublishWizard
{
	enum CompileState
	{
		/// <summary>No code, nothing to compile</summary>
		None,
		Compiling,
		Compiled,
		Failed
	}

	CompileState compileState;

	/// <summary>
	/// What the compiler said, as HTML - shown when the badge is clicked
	/// </summary>
	readonly System.Text.StringBuilder compileLog = new();

	/// <summary>
	/// Which compile is the latest - an older one finishing after it doesn't count
	/// </summary>
	int compileVersion;

	CompileBadge badge;

	/// <summary>
	/// Can't go on from any page until the code's compiled - the upload needs it
	/// </summary>
	protected override bool CanGoOn => compileState is CompileState.None or CompileState.Compiled;

	protected override void BuildHeaderRight( Layout layout )
	{
		// The base wizard builds its header before we've got a project - the constructor builds this again after
		if ( Project is null || !Project.HasCodePath() )
			return;

		badge = layout.Add( new CompileBadge( this ) );
	}

	/// <summary>
	/// Compile the project's code in the background while the pages are filled in
	/// </summary>
	async Task CompileAsync()
	{
		if ( !Project.HasCodePath() )
			return;

		var version = ++compileVersion;

		Config.AssemblyFiles = null;
		Config.CompilerOutput = null;
		Config.Publisher = null;
		Config.CodePackages.Clear();

		compileLog.Clear();
		compileState = CompileState.Compiling;
		badge?.Update();

		try
		{
			var generated = await EditorUtility.Projects.Compile( Project, log =>
			{
				// Each one's a line of its own
				if ( version == compileVersion )
					compileLog.Append( System.Net.WebUtility.HtmlEncode( log.TrimEnd() ).Replace( "\n", "<br>" ) ).Append( "<br>" );
			} );

			if ( version != compileVersion )
				return;

			if ( generated is null )
				throw new System.Exception( "No code was generated" );

			Config.CompilerOutput = generated;
			Config.AssemblyFiles = CompiledFiles( generated );
			compileState = CompileState.Compiled;
		}
		catch ( System.Exception e )
		{
			if ( version != compileVersion )
				return;

			Log.Warning( e );
			compileLog.Append( $"<span style=\"color: #f44;\">{System.Net.WebUtility.HtmlEncode( e.Message )}</span><br>" );
			compileState = CompileState.Failed;
		}

		if ( badge.IsValid() )
			badge.Update();
	}

	/// <summary>
	/// The files the compiled code goes up as, and note which packages it references
	/// </summary>
	Dictionary<string, object> CompiledFiles( CompilerOutput[] generated )
	{
		var files = new Dictionary<string, object>();

		var orderedList = generated.Select( x => x.Compiler.AssemblyName ).ToList();
		files[".bin/manifest.json"] = System.Text.Json.JsonSerializer.Serialize( orderedList, new JsonSerializerOptions { WriteIndented = true } );

		foreach ( var assembly in generated )
		{
			files[$".bin/{assembly.Compiler.AssemblyName}.xml"] = assembly.XmlDocumentation;
			files[$".bin/{assembly.Compiler.AssemblyName}.cll"] = assembly.Archive.Serialize();

			PeekAssembly( assembly.AssemblyData );
		}

		return files;
	}

	/// <summary>
	/// Look inside this assembly for anything useful we can fill the manifest with
	/// </summary>
	void PeekAssembly( byte[] contents )
	{
		var assetAttributes = AssemblyMetadata.GetCustomAttributes( contents )
			.Where( x => x.AttributeFullName == "Sandbox.Cloud/AssetAttribute" );

		foreach ( var a in assetAttributes )
		{
			var ident = $"{a.Arguments[0]}";

			if ( !Package.TryParseIdent( ident, out var parts ) )
			{
				Log.Warning( $"Couldn't parse ident {ident}" );
				continue;
			}

			// do we need to add .version here?
			Config.CodePackages.Add( $"{parts.org}.{parts.package}" );
		}
	}

	/// <summary>
	/// What the compiler said, in a popup under the badge
	/// </summary>
	void ShowCompileLog()
	{
		var popup = new PopupWidget( this );
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 16;
		popup.Layout.Spacing = 8;
		popup.MinimumSize = new Vector2( 800, 480 );

		popup.Layout.Add( new Label.Subtitle( compileState == CompileState.Failed ? "Compile Errors" : "Compile Output" ) );

		var log = popup.Layout.Add( new TextEdit( popup ) { ReadOnly = true }, 1 );
		log.AppendHtml( compileLog.Length > 0 ? compileLog.ToString() : "Nothing yet" );

		popup.Visible = true;
		popup.Position = badge.ScreenRect.BottomRight - new Vector2( popup.MinimumWidth, -8 );
		popup.ConstrainToScreen();
	}

	/// <summary>
	/// How the code's compile is going, in the header's top right - red and impossible to miss when it's failed.
	/// Click it for what the compiler said.
	/// </summary>
	class CompileBadge : Widget
	{
		readonly PublishWizard Wizard;

		public CompileBadge( PublishWizard wizard ) : base( null )
		{
			Wizard = wizard;
			FixedSize = new Vector2( 260, 44 );
			Cursor = CursorShape.Finger;
			ToolTip = "Show what the compiler said";
		}

		(string Icon, string Text, Color Color) Look => Wizard.compileState switch
		{
			CompileState.Failed => ("error", "Compile failed - show errors", Theme.Red.Darken( 0.2f )),
			CompileState.Compiled => ("check_circle", "Code compiled", Theme.Green.Darken( 0.55f )),
			_ => ("hourglass_top", "Compiling...", Theme.ControlBackground),
		};

		protected override void OnMouseClick( MouseEvent e )
		{
			Wizard.ShowCompileLog();
		}

		protected override void OnPaint()
		{
			var look = Look;
			var rect = LocalRect;
			var brighten = Paint.HasMouseOver ? 0.15f : 0f;

			Paint.Antialiasing = true;
			Paint.ClearPen();
			Paint.SetBrush( look.Color.Lighten( brighten ) );
			Paint.DrawRect( rect, 6 );

			Paint.SetPen( Color.White );
			Paint.DrawIcon( new Rect( rect.Left + 12, rect.Top, 24, rect.Height ), look.Icon, 20 );
			Paint.SetDefaultFont( 9, 600 );
			Paint.DrawText( rect.Shrink( 44, 0, 12, 0 ), look.Text, TextFlag.LeftCenter );
		}
	}
}
