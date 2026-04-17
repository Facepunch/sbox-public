using Sandbox;
using System;
using System.Linq;

namespace GameObjects;

[TestClass]
[DoNotParallelize]
public class Camera
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
