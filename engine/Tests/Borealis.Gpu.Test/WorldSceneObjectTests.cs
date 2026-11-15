using Borealis.Source2.World;

namespace BorealisGpuTests;

/// <summary>
/// World node scene objects' mesh group masks, as native's world node applies them.
/// </summary>
[TestClass]
public class WorldSceneObjectTests
{
	/// <summary>
	/// A 0 mask (<c>MESH_GROUP_NOT_FOUND</c>) or the compiler's fill pattern keeps the model's default groups; any other mask
	/// is the object's. rp_downtown_3t writes 0 for 2,122 of its world meshes, which drew nothing before.
	/// </summary>
	[TestMethod]
	[DataRow( 0ul, 0xFFul )]
	[DataRow( WorldSceneObject.UnsetMeshGroupMask, 0xFFul )]
	[DataRow( 2ul, 2ul )]
	[DataRow( ulong.MaxValue, ulong.MaxValue )]
	[DataRow( 0xC00000000ul, 0xC00000000ul )]
	public void UnsetMasksKeepTheModelsGroups( ulong written, ulong drawn )
	{
		var o = new WorldSceneObject { MeshGroupMask = written };
		Assert.AreEqual( drawn, o.MeshGroups( 0xFF ) );
	}
}
