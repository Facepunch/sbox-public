using Sandbox;
using System;
using System.Linq;

namespace SceneTests.GameObjects;

[TestClass]
public class CameraTest
{
	[TestMethod]
	public void MainCamera()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		Assert.IsNull( scene.Camera );

		var go = scene.CreateObject();
		var cam = go.Components.Create<CameraComponent>();

		Assert.IsNotNull( scene.Camera );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.IsNull( scene.Camera );
	}
}
