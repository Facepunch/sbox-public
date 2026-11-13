using Sandbox.Internal;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Existing inspector properties and actions receive updated metadata and invoke the new CLR members.
	/// </summary>
	[TestMethod]
	public void RetainedInspectorMembersRefreshMetadataAndInvocation()
	{
		const string source = """
			class InspectorActionComponent : Component
			{
				[Property, Title("Before value")] public int Value { get; set; }
				[Property] public int Removed { get; set; }
				/// <summary>
				/// Original action.
				/// </summary>
				[Title("Before action")] public void Apply() { Value++; }
			}
			""";
		LoadSource( "action.csx", source );
		scene = new Scene();
		using var scope = scene.Push();
		var type = Game.TypeLibrary.GetType( "InspectorActionComponent" );
		var component = scene.CreateObject().Components.Create( type );
		var inspector = TypeLibrary.Editor.GetSerializedObject( component );
		var value = inspector.GetProperty( "Value" );
		var action = inspector.GetProperty( "Apply" );
		Assert.IsTrue( action.IsMethod );
		Assert.AreEqual( "Before action", action.DisplayName );
		action.Invoke();
		Assert.AreEqual( 1, type.GetValue( component, "Value" ) );
		reloadRoots = [component, action, value, inspector];

		LoadSource( "action.csx", source.Replace( "Before action", "After action" )
			.Replace( "Before value", "After value" ).Replace( "Original action.", "Updated action." )
			.Replace( "Removed", "Added" ).Replace( "Value++;", "Value += 10;" ) );
		action = (SerializedProperty)reloadRoots[1];
		value = (SerializedProperty)reloadRoots[2];
		inspector = (SerializedObject)reloadRoots[3];
		Assert.IsNull( inspector.GetProperty( "Removed" ) );
		Assert.IsNotNull( inspector.GetProperty( "Added" ) );
		Assert.AreEqual( "After value", value.DisplayName );
		value.SetValue( 5 );
		Assert.IsNull( Game.Scripting.LastError, Game.Scripting.LastError?.ToString() );
		Assert.AreEqual( "After action", action.DisplayName );
		Assert.AreEqual( "Updated action.", action.Description );
		Assert.AreEqual( "action.csx", action.SourceFile );
		Assert.AreEqual( 8, action.SourceLine );
		action.Invoke();
		Assert.AreEqual( 15, value.GetValue<int>() );
	}

}
