using Borealis.Source2.Models;
using Sandbox.ContentBuild;
using System.IO;
using System.Linq;

namespace ContentBuild;

/// <summary>
/// Models compiled with materials of shaders only a module describes (standard, which complex, simple and generic are
/// aliases of) are laid out as they were with the .shader: the model compiler lays a mesh's vertices out by its material's
/// vertex inputs (the <c>VSInputSignature</c> the material compiler writes), and a material without them gave models
/// position alone.
/// </summary>
[TestClass, DoNotParallelize]
public class ModuleMaterialLayoutTests
{
	/// <summary>
	/// A box and a skinned mesh (the easter bonnet) with a material naming complex compile with complex.shader's layout:
	/// position, uv and the packed tangent frame, and the skinned mesh's bone indices and weights, in the formats the
	/// shipped models compiled before had.
	/// </summary>
	[TestMethod]
	public void ModelsOfStandardMaterialsKeepTheShadersLayout()
	{
		using var fx = new ContentFixture();
		var root = new ContentRoot( fx.Mod, fx.Root, Path.Combine( fx.Parent, "state" ) );
		fx.CopyShipped( Sources.ImageA, "materials/s_color.tga" );
		fx.Write( "materials/s.vmat", Sources.Material( "shaders/complex.shader", ["TextureColor", "materials/s_color.tga"] ) );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/s.vmat" ) );
		fx.CopyShipped( $"{Sources.BonnetFolder}/easter_bonnet.fbx", "models/bonnet.fbx" );
		fx.Write( "models/bonnet.vmdl", Sources.Mesh( "models/bonnet.fbx", "materials/s.vmat" ) );

		var compiler = CompilerWorkerTests.Compiler( [root] );
		try
		{
			var report = ContentBuilder.Build( new BuildRequest { Roots = [root], MaxJobs = 4 }, compiler );
			Assert.IsTrue( report.Success, report.Log.ToString() );
		}
		finally { compiler.Finished(); }

		var box = ModelFile.Load( fx.Path( "models/box.vmdl_c" ) );
		var boxVertices = box.Meshes.Single().VertexBuffers.Single();
		Assert.AreEqual( "POSITION0:R32G32B32_FLOAT, TEXCOORD0:R16G16_FLOAT, NORMAL0:R10G10B10A2_UNORM", Fields( boxVertices ) );
		Assert.IsTrue( box.Meshes.Single().DrawCalls.Single().CompressedTangentFrame );

		var bonnet = ModelFile.Load( fx.Path( "models/bonnet.vmdl_c" ) );
		Assert.IsTrue( bonnet.IsSkinned );
		foreach ( var vertices in bonnet.Meshes.SelectMany( m => m.VertexBuffers ) )
		{
			Assert.AreEqual( "POSITION0:R32G32B32_FLOAT, TEXCOORD0:R16G16_FLOAT, NORMAL0:R10G10B10A2_UNORM, BLENDINDICES0:R8G8B8A8_UINT, BLENDWEIGHT0:R8G8B8A8_UNORM", Fields( vertices ) );
			Assert.IsTrue( vertices.CompressedTangentFrame );
		}

		var material = System.Text.Encoding.Latin1.GetString( fx.Read( "materials/s.vmat_c" ) );
		StringAssert.Contains( material, "VSInputSignature", "the material records its vertex inputs" );
	}

	static string Fields( MeshVertexBuffer vertices ) => string.Join( ", ", vertices.Fields.Select( f => $"{f.Semantic}{f.SemanticIndex}:{f.Format}" ) );
}
