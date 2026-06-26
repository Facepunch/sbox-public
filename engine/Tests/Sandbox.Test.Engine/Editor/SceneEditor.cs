namespace EditorTests;

/// <summary>
/// A component type for <see cref="SceneEditorTest"/> to find by name in the type library.
/// Never instantiated.
/// </summary>
public class ClipboardTestComponent : Component
{
}

[TestClass]
public class SceneEditorTest
{
	/// <summary>
	/// Clipboard text is component json when it's a json object whose __type resolves
	/// to a known component type.
	/// </summary>
	[TestMethod]
	public void RecognisesComponentJson()
	{
		Assert.IsTrue( global::Editor.SceneEditor.IsComponentJson( $$"""{ "__type": "{{nameof( ClipboardTestComponent )}}" }""" ) );
	}

	/// <summary>
	/// Anything that isn't a json object with a known component __type should be
	/// rejected without throwing - the clipboard can contain absolutely anything.
	/// </summary>
	[TestMethod]
	[DataRow( null )]
	[DataRow( "" )]
	[DataRow( "      " )]
	[DataRow( "not json at all" )]
	[DataRow( "[ 1, 2, 3 ]" )]
	[DataRow( "{}" )]
	[DataRow( """{ "__type": 5 }""" )]
	[DataRow( """{ "__type": "NotARealComponentType" }""" )]
	[DataRow( """{ "sometext": "ClipboardTestComponent" }""" )]
	public void RejectsEverythingElse( string text )
	{
		Assert.IsFalse( global::Editor.SceneEditor.IsComponentJson( text ) );
	}
}
