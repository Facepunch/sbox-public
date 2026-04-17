
using Sandbox.DataModel;
using System;
using System.Collections.Generic;
using System.IO;

namespace Addon
{
	[TestClass]
	[DoNotParallelize]
	public class TemplateTests
	{

		[TestMethod]
		public async Task CompileToolTemplates()
		{
			var templates = FindTemplates( "tool" );
			var result = await CompileTemplates( templates );

			Assert.IsTrue( result );
		}

		//[TestMethod]
		public async Task CompileGameTemplates()
		{
			var templates = FindTemplates( "game" );
			var result = await CompileTemplates( templates );

			Assert.IsTrue( result );
		}

		//[TestMethod]
		public async Task CompileExtensionTemplates()
		{
			var templates = FindTemplates( "extension" );
			var result = await CompileTemplates( templates );

			Assert.IsTrue( result );
		}

		async Task<bool> CompileTemplates( List<ProjectConfig> projects )
		{
			CompileGroup group = new( "Test" );

			var i = 0;
			foreach ( var proj in projects )
			{
				var settings = new Compiler.Configuration();
				settings.Clean();

				var codePath = Path.Combine( proj.Directory.FullName, "Code" );
				var compiler = group.CreateCompiler( $"test{++i}", codePath, settings );

				if ( proj.Type == "tool" )
				{
					compiler.AddReference( "Sandbox.Tools" );
					compiler.GeneratedCode.AppendLine( "global using static Sandbox.Internal.GlobalToolsNamespace;" );
					compiler.GeneratedCode.AppendLine( "global using static Sandbox.Internal.GlobalGameNamespace;" );
				}

				if ( proj.Type == "game" )
				{
					var baseCodePath = Path.Combine( Environment.CurrentDirectory, "addons", "base", "code" );
					compiler.AddSourcePath( baseCodePath );
					compiler.GeneratedCode.AppendLine( $"global using static Sandbox.Internal.GlobalGameNamespace;" );
				}

				if ( proj.Type == "extension" )
				{
					compiler.GeneratedCode.AppendLine( $"global using static Sandbox.Internal.GlobalGameNamespace;" );
				}
			}

			await group.BuildAsync();
			return group.BuildResult.Success;
		}

		List<ProjectConfig> FindTemplates( string type )
		{
			var result = new List<ProjectConfig>();
			var addons = Directory.GetFiles( Path.Combine( Environment.CurrentDirectory, "templates" ), "*.sbproj", SearchOption.AllDirectories );

			foreach ( var addon in addons )
			{
				var json = File.ReadAllText( addon );
				var projectConfig = System.Text.Json.JsonSerializer.Deserialize<ProjectConfig>( json );
				projectConfig.Directory = new DirectoryInfo( Path.GetDirectoryName( addon ) );

				if ( projectConfig.Type != type )
					continue;

				result.Add( projectConfig );
			}

			return result;
		}

	}
}
