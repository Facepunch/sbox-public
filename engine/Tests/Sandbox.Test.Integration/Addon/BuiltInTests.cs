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
			await Project.CompileAsync();

			var warnings = Project.All
				.Where( x => x.Compiler is not null )
				.SelectMany( x => x.Compiler.Diagnostics )
				.Where( x => x.Severity > Microsoft.CodeAnalysis.DiagnosticSeverity.Info )
				.ToArray();

			Assert.AreEqual( 0, warnings.Length,
				$"Built-in projects had compile-time warnings.{Environment.NewLine}" +
					string.Join( $"{Environment.NewLine}  ", warnings.Select( x => x.ToString() ) ) );
		}
	}
}
