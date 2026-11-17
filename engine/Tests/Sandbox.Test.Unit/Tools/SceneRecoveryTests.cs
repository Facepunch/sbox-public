using System.IO;

namespace Tools;

/// <summary>Recovery copies of unsaved scenes go beside the scene, never over it or another file.</summary>
[TestClass]
public class SceneRecoveryTests
{
	[TestMethod]
	public void RecoveryCopiesGoBesideTheSceneAndNeverOverAFile()
	{
		var folder = Path.Combine( Path.GetTempPath(), $"scene-recovery-{System.Guid.NewGuid():N}" );
		Directory.CreateDirectory( folder );
		try
		{
			var original = Path.Combine( folder, "avatar.scene" );
			File.WriteAllText( original, "{}" );
			var when = new System.DateTime( 2026, 11, 17, 15, 4, 5 );

			var first = Editor.SceneRecovery.RecoveryPath( original, when );
			Assert.AreEqual( Path.Combine( folder, "avatar.recovered-20261117-150405.scene" ), first );

			File.WriteAllText( first, "{}" );
			var second = Editor.SceneRecovery.RecoveryPath( original, when );
			Assert.AreEqual( Path.Combine( folder, "avatar.recovered-20261117-150405-2.scene" ), second );
			Assert.AreEqual( "{}", File.ReadAllText( original ) );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}
}
