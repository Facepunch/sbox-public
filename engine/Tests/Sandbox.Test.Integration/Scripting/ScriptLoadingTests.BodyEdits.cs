using Sandbox.Engine;
using Sandbox.Internal;
using System.Collections.Generic;
using System.Reflection;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Method-only edits update real component callbacks while retaining CLR identities, field values and host roots.
	/// Moving a declaration still replaces its assembly so source-location metadata and native migration stay correct.
	/// </summary>
	[TestMethod]
	public void BodyEditsSkipMigrationButSourceLocationChangesDoNot()
	{
		const string source = "class BodyEditComponent : Component { [Property] public int Value { get; set; } = 4; protected override void OnUpdate() { Value += 1; } }";
		var sources = new Dictionary<string, string> { ["body-edit.csx"] = source };
		int replacements = 0;
		var replace = context.ReplaceScriptAssembly;
		context.ReplaceScriptAssembly = ( previous, incoming, register ) =>
		{
			replacements++;
			replace( previous, incoming, register );
		};
		try
		{
			Assert.IsTrue( Game.Scripting.LoadFiles( sources ).Success );
			scene = new Scene();
			using var scope = scene.Push();
			var type = Game.TypeLibrary.GetType( "BodyEditComponent" );
			var component = scene.CreateObject().Components.Create( type );
			reloadRoots = [component];
			component.InternalUpdate();
			Assert.AreEqual( 5, type.GetProperty( "Value" ).GetValue( component ) );

			sources["body-edit.csx"] = source.Replace( "= 4", "= 9" ).Replace( "+= 1", "+= 2" );
			var updated = Game.Scripting.LoadFiles( sources );
			Assert.IsTrue( updated.Success, string.Join( "; ", updated.Diagnostics ) );
			Assert.IsTrue( updated.Published );
			Assert.AreEqual( 1, replacements );
			Assert.AreSame( component, reloadRoots[0] );
			Assert.AreSame( type.TargetType, Game.TypeLibrary.GetType( "BodyEditComponent" ).TargetType );
			component.InternalUpdate();
			Assert.AreEqual( 7, type.GetProperty( "Value" ).GetValue( component ) );
			var fresh = scene.CreateObject().Components.Create( type );
			fresh.InternalUpdate();
			Assert.AreEqual( 11, type.GetProperty( "Value" ).GetValue( fresh ) );

			var previousType = type.TargetType;
			sources["body-edit.csx"] = "\n" + sources["body-edit.csx"];
			updated = Game.Scripting.LoadFiles( sources );
			Assert.IsTrue( updated.Success, string.Join( "; ", updated.Diagnostics ) );
			Assert.AreEqual( 2, replacements );
			type = Game.TypeLibrary.GetType( "BodyEditComponent" );
			Assert.AreNotSame( previousType, type.TargetType );
			Assert.AreEqual( 2, type.TargetType.GetCustomAttribute<SourceLocationAttribute>().Line );
			Assert.AreEqual( 7, type.GetProperty( "Value" ).GetValue( reloadRoots[0] ) );
			((Component)reloadRoots[0]).InternalUpdate();
			Assert.AreEqual( 9, type.GetProperty( "Value" ).GetValue( reloadRoots[0] ) );
		}
		finally
		{
			context.ReplaceScriptAssembly = replace;
		}
	}
}
