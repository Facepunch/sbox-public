namespace EngineTests;

/// <summary>
/// Material copies must keep their own resource identities without replacing the source.
/// </summary>
[TestClass]
public class MaterialTest
{
	/// <summary>
	/// Shader-name reads reuse their string until the shader changes or native reload replaces it.
	/// </summary>
	[TestMethod]
	public void ShaderNameTracksShaderChangesAndReloads()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var material = Material.Load( "materials/water/blue.water.vmat" ).CreateCopy();
		try
		{
			var original = material.ShaderName;
			Assert.AreSame( original, material.ShaderName );
			var replacement = Material.Load( "materials/default/default_line.vmat" );
			material.Shader = replacement.Shader;
			Assert.AreEqual( replacement.ShaderName, material.ShaderName );
			Assert.AreNotEqual( original, material.ShaderName );

			material.native.Set( "shader", original );
			material.OnReloaded();
			Assert.AreEqual( original, material.ShaderName );
		}
		finally
		{
			material.Destroy();
		}
	}

	/// <summary>
	/// Anonymous copies have distinct paths and leave the source lookup intact, including after destruction.
	/// </summary>
	[TestMethod]
	[DataRow( "materials/default/default_line.vmat" )]
	[DataRow( "materials/water/blue.water.vmat" )]
	public void CopiesPreserveSourceLookup( string path )
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var source = Material.Load( path );
		Assert.IsTrue( source.IsValid && !source.IsError );
		Assert.AreSame( source, ResourceLibrary.Get<Material>( path ) );

		Material first = null;
		Material second = null;
		try
		{
			first = source.CreateCopy();
			Assert.IsNotNull( first );
			Assert.AreSame( source, ResourceLibrary.Get<Material>( path ), "A copy replaced the source's resource lookup." );

			second = source.CreateCopy();
			Assert.IsNotNull( second );
			Assert.AreNotEqual( first.ResourcePath, second.ResourcePath );
			Assert.AreSame( first, ResourceLibrary.Get<Material>( first.ResourcePath ) );
			Assert.AreSame( second, ResourceLibrary.Get<Material>( second.ResourcePath ) );
			Assert.AreSame( source, ResourceLibrary.Get<Material>( path ) );

			first.Destroy();
			first = null;
			second.Destroy();
			second = null;
			Assert.AreSame( source, ResourceLibrary.Get<Material>( path ), "Destroying copies removed the source's resource lookup." );
		}
		finally
		{
			first?.Destroy();
			second?.Destroy();
		}
	}
}
