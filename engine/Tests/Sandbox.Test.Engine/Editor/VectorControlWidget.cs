using Editor;
using Sandbox.Internal;

namespace EditorTests;

[TestClass]
public class VectorControlWidgetTest
{
	public class Holder
	{
		public Vector3 Position { get; set; }
		public Vector2 Size { get; set; }
	}

	/// <summary>
	/// The uniform proxy should fan a single value out to every component of the vector.
	/// </summary>
	[TestMethod]
	public void UniformPropertyFansOutToAllComponents()
	{
		var holder = new Holder { Position = new Vector3( 1, 2, 3 ) };
		var sobj = GlobalToolsNamespace.EditorTypeLibrary.GetSerializedObject( holder );

		Assert.IsTrue( sobj.GetProperty( nameof( Holder.Position ) ).TryGetAsObject( out var vector ) );

		var uniform = new UniformVectorProperty( vector );
		uniform.SetValue( 5f );

		Assert.AreEqual( new Vector3( 5, 5, 5 ), holder.Position );
	}

	/// <summary>
	/// The uniform proxy reads through to the x component.
	/// </summary>
	[TestMethod]
	public void UniformPropertyReadsX()
	{
		var holder = new Holder { Position = new Vector3( 7, 8, 9 ) };
		var sobj = GlobalToolsNamespace.EditorTypeLibrary.GetSerializedObject( holder );

		Assert.IsTrue( sobj.GetProperty( nameof( Holder.Position ) ).TryGetAsObject( out var vector ) );

		var uniform = new UniformVectorProperty( vector );

		Assert.AreEqual( 7f, uniform.GetValue<float>() );
	}

	/// <summary>
	/// Vectors without all four components should still work - missing components are
	/// skipped rather than throwing.
	/// </summary>
	[TestMethod]
	public void UniformPropertyHandlesSmallerVectors()
	{
		var holder = new Holder { Size = new Vector2( 1, 2 ) };
		var sobj = GlobalToolsNamespace.EditorTypeLibrary.GetSerializedObject( holder );

		Assert.IsTrue( sobj.GetProperty( nameof( Holder.Size ) ).TryGetAsObject( out var vector ) );

		var uniform = new UniformVectorProperty( vector );
		uniform.SetValue( 4f );

		Assert.AreEqual( new Vector2( 4, 4 ), holder.Size );
	}
}
