namespace Editor.Wizards;

partial class PublishWizard
{
	/// <summary>
	/// Compile
	/// </summary>
	class CompileWizardPage : PublishWizardPage
	{
		public override string PageTitle => "Compiling";
		public override string PageSubtitle => "We need to make sure your code works before we can expose the rest of the world to it";

		bool CompileSuccessful = false;

		TextEdit logOutput;

		public override bool IsAutoStep => true;

		public override async Task OpenAsync()
		{
			BodyLayout?.Clear( true );

			logOutput = new TextEdit( this );
			BodyLayout.Add( logOutput, 1 );

			Enabled = false;
			Visible = true;

			PublishConfig.AssemblyFiles = null;

			await Refresh();
		}

		public async Task Refresh()
		{
			if ( !Project.HasCodePath() )
				return;

			try
			{
				logOutput.AppendPlainText( "Compiling..\n" );
				await DoProjectCompile();
				CompileSuccessful = true;
			}
			catch ( Exception ex )
			{
				logOutput.AppendHtml( "<span style=\"color: red;\">Compile Failed!</span><br>" );
				logOutput.AppendHtml( $"<span style=\"color: red;\">{ex.Message}</span><br>" );
				logOutput.AppendHtml( $"<span style=\"color: red;\">{ex.StackTrace}</span><br>" );

				// Show error in the console for now
				Log.Warning( ex );
				// todo show compile errors
				//token.ThrowIfCancellationRequested();
				//StatusBar.ShowMessage( $"Failed to compile addon: {ex.Message}", 100.0f );
				return;
			}
		}

		public override bool CanProceed()
		{
			if ( CompileSuccessful == false ) return false;
			return true;
		}

		void CompileOutput( string log )
		{
			logOutput.AppendHtml( $"{log.Replace( "\n", "<br>" )}" );
		}

		async Task DoProjectCompile()
		{
			var generated = await EditorUtility.Projects.Compile( Project, CompileOutput );
			PublishConfig.CompilerOutput = generated;

			if ( generated == null )
			{
				throw new System.Exception( "No code was generated" );
			}

			Dictionary<string, object> extrafiles = new();

			var orderedList = generated.Select( x => x.Compiler.AssemblyName ).ToList();
			var json = System.Text.Json.JsonSerializer.Serialize( orderedList, new JsonSerializerOptions { WriteIndented = true } );

			logOutput.AppendPlainText( $"Adding: manifest.json" );

			extrafiles[".bin/manifest.json"] = json;

			foreach ( var assembly in generated )
			{
				extrafiles[$".bin/{assembly.Compiler.AssemblyName}.xml"] = assembly.XmlDocumentation;
				extrafiles[$".bin/{assembly.Compiler.AssemblyName}.cll"] = assembly.Archive.Serialize();
				logOutput.AppendPlainText( $"Adding: {assembly.Compiler.AssemblyName}.dll" );

				PeekAssembly( assembly.Compiler.AssemblyName, assembly.AssemblyData );
			}

			//
			// only games should actually ship with a package.base.dll
			// because even though extensions/libraries/etc can reference them
			// they should be referencing from game - not their own
			//
			if ( Project.Config.Type != "game" )
			{
				extrafiles.Remove( ".bin/package.base.xml" );
				extrafiles.Remove( ".bin/package.base.cll" );
			}

			PublishConfig.AssemblyFiles = extrafiles;
		}

		/// <summary>
		/// Look inside this assembly for anything useful we can fill the manifest with
		/// </summary>
		private void PeekAssembly( string title, byte[] contents )
		{
			var attr = AssemblyMetadata.GetCustomAttributes( contents );

			var assetAttributes = attr
									.Where( x => x.AttributeFullName == "Sandbox.Cloud/AssetAttribute" )
									.ToArray();

			foreach ( var a in assetAttributes )
			{
				var ident = $"{a.Arguments[0]}";

				if ( !Package.TryParseIdent( ident, out var parts ) )
				{
					Log.Warning( $"Couldn't parse ident {ident}" );
					continue;
				}

				// do we need to add .version here?
				PublishConfig.CodePackages.Add( $"{parts.org}.{parts.package}" );
			}
		}
	}
}

