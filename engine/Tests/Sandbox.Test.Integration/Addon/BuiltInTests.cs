using System;

namespace AddonTests
{
	[TestClass]
	public class BuiltInTest
	{
		/// <summary>
		/// Built-in projects must not have any compile-time warnings.
		/// </summary>
		[TestMethod]
		public async Task NoWarnings()
		{
			await Project.InitializeBuiltIn( false );

			// Configs in the two addon paths must retain all the engine's automatic projects.
			foreach ( var ident in new[] { "local.menu", "local.toolbase", "local.actiongraph", "local.shadergraph", "local.moviemaker", "local.hammer", "local.dooeditor", "local.animgraph" } )
			{
				var project = Project.FindByIdent( ident );
				Assert.IsNotNull( project, $"Built-in project {ident} wasn't discovered" );
				Assert.IsTrue( project.IsBuiltIn && project.Active, $"Built-in project {ident} wasn't activated" );
			}

			await Project.CompileAsync();

			var warnings = Project.All
				.Where( x => x.Compiler is not null )
				.SelectMany( x => x.Compiler.Diagnostics )
				.Where( x => x.Severity > Microsoft.CodeAnalysis.DiagnosticSeverity.Info )
				.ToArray();

			Assert.AreEqual( 0, warnings.Length,
				$"Built-in projects had compile-time warnings.{Environment.NewLine}" +
					string.Join( $"{Environment.NewLine}  ", warnings.Select( x => x.ToString() ) ) );

			// The menu builds to one assembly, its editor code only builds when the menu project is open.
			// The environment-rule tests cover headless game startup separately.
			if ( !Application.IsHeadless )
			{
				var menu = Project.All.FirstOrDefault( x => x.Config.Ident == "menu" );
				Assert.IsNotNull( menu, "Menu project wasn't loaded" );
				Assert.AreEqual( 1, menu.AssemblyFileSystem.FindFile( "/", "*.dll", true ).Count() );
			}
		}
	}
}
