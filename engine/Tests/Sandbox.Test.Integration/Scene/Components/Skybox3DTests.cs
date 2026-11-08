using System.Collections.Generic;
using System.Reflection;
using Sandbox.Diagnostics;
using Sandbox.Internal;
using Sandbox.Rendering;

namespace SceneTests.Components;

[TestClass]
[DoNotParallelize]
public class Skybox3DTests
{
	TypeLibrary previousTypeLibrary;
	Surface previousSurface;
	bool installedSurface;
	Scene source;
	Scene parent;
	readonly List<SceneFile> files = [];

	public sealed class AwakeProbe : Component, Component.ExecuteInEditor
	{
		public static int Count;
		public static int EnabledCount;
		protected override void OnAwake() => Count++;
		protected override void OnEnabled() => EnabledCount++;
	}

	[TestInitialize]
	public void Initialize()
	{
		previousTypeLibrary = Game.TypeLibrary;
		Game.TypeLibrary = SharedTypeLibrary.EngineAndTests;
		Assert.IsNotNull( Game.TypeLibrary.GetType<AwakeProbe>() );

		if ( Surface.FindByName( "default" ) is null )
		{
			Surface.All.TryGetValue( 0, out previousSurface );
			var surface = new Surface();
			surface.RegisterWeakResourceId( "surfaces/default.surface" );
			Surface.All[0] = surface;
			installedSurface = true;
		}

		source = Scene.CreateEditorScene();
		using var scope = source.Push();
		var mesh = new PolygonMesh();
		var a = mesh.AddVertex( new Vector3( 0, 0, 0 ) );
		var b = mesh.AddVertex( new Vector3( 64, 0, 0 ) );
		var c = mesh.AddVertex( new Vector3( 64, 64, 0 ) );
		var d = mesh.AddVertex( new Vector3( 0, 64, 0 ) );
		mesh.AddFace( a, b, c, d );
		var component = source.CreateObject().AddComponent<MeshComponent>( false );
		component.Collision = MeshComponent.CollisionType.None;
		component.Mesh = mesh;
		component.Enabled = true;
		Assert.IsNotNull( component.Model );
		Assert.IsTrue( component.Model.MeshCount > 0 );
		source.CreateObject().AddComponent<AwakeProbe>();
		AwakeProbe.Count = 0;
		AwakeProbe.EnabledCount = 0;
	}

	[TestCleanup]
	public void Cleanup()
	{
		parent?.Destroy();
		source?.Destroy();

		foreach ( var file in files )
			Game.Resources.Unregister( file );

		Game.TypeLibrary = previousTypeLibrary;

		if ( !installedSurface ) return;
		if ( previousSurface is not null )
			Surface.All[0] = previousSurface;
		else
			Surface.All.Remove( 0 );
	}

	SceneFile SaveSource()
	{
		var file = source.CreateSceneFile();
		files.Add( file );
		Assert.IsTrue( file.BinaryData.Length > 0, "The source mesh must use binary blob data." );
		return file;
	}

	Skybox3D LoadSkybox( SceneFile file, bool editor, bool batched )
	{
		parent = editor ? Scene.CreateEditorScene() : new Scene();
		using var scope = parent.Push();
		Skybox3D skybox;

		using ( batched ? CallbackBatch.Batch() : null )
		{
			skybox = parent.CreateObject().AddComponent<Skybox3D>( false );
			skybox.SkyboxScene = file;
			skybox.Enabled = true;
		}

		return skybox;
	}

	static Scene ContentScene( Skybox3D skybox ) =>
		(Scene)typeof( Skybox3D ).GetField( "skyboxScene", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( skybox );

	void AssertMeshLoaded( Skybox3D skybox )
	{
		var content = ContentScene( skybox );
		Assert.IsTrue( content.IsValid() );
		Assert.IsTrue( content.Enabled );
		var mesh = content.GetAllComponents<MeshComponent>().Single();
		Assert.IsTrue( mesh.Active );
		Assert.AreEqual( 4, mesh.Mesh.VertexHandles.Count() );
		Assert.AreEqual( 1, mesh.Mesh.FaceHandles.Count() );
		Assert.IsNotNull( mesh.Model );
		Assert.IsTrue( mesh.Model.MeshCount > 0 );

		var renderObject = (SceneObject)typeof( MeshComponent )
			.GetField( "_sceneObject", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( mesh );
		Assert.IsTrue( renderObject.IsValid() );
		Assert.AreSame( content.SceneWorld, renderObject.World );
		Assert.AreSame( content.SceneWorld, parent.SceneWorld.InternalSkyboxWorlds.Single().SkyboxWorld );
	}

	[TestMethod]
	[DataRow( true, false )]
	[DataRow( true, true )]
	[DataRow( false, false )]
	[DataRow( false, true )]
	public void LoadPreservesMeshGeometry( bool editor, bool batched )
	{
		var skybox = LoadSkybox( SaveSource(), editor, batched );

		Assert.AreEqual( 1, AwakeProbe.Count );
		Assert.AreEqual( 1, AwakeProbe.EnabledCount );
		AssertMeshLoaded( skybox );

		using var scope = parent.Push();
		var camera = parent.CreateObject().Components.Create<CameraComponent>();
		camera.SceneCamera.World = parent.SceneWorld;
		var fog = parent.CreateObject().Components.Create<Fog>( false );
		fog.ColorSource = FogColorSource.Constant;
		fog.Color = Color.Red;
		fog.DistanceRange = new Vector2( -160, 1600 );
		fog.Mode = FogMode.Distance;
		skybox.FogOverride = fog;
		var settings = parent.SceneWorld.GetRenderingSkybox().ResolveFog( camera.SceneCamera ).Value;
		Assert.AreEqual( fog.GetSettings( camera ), settings, "Disabled fog components still supply independent skybox settings" );
		Assert.AreEqual( -160f, settings.StartDistance );
		Assert.AreEqual( 1600f, settings.EndDistance );
		camera.SceneCamera.Fog = new FogSetup { Color = Color.Green };
		Assert.AreEqual( Color.Green, camera.SceneCamera.ResolveFog( default ).Value.Color );
		Assert.AreEqual( settings, camera.SceneCamera.ResolveFogForSkybox( default ).Value );
		Assert.AreEqual( Color.Yellow, camera.SceneCamera.ResolveFogForSkybox( new ViewSetup { Fog = new FogSetup { Color = Color.Yellow } } ).Value.Color );
		Assert.IsNull( camera.SceneCamera.ResolveFogForSkybox( new ViewSetup { GradientFog = new GradientFogSetup { Enabled = false } } ) );
		camera.SceneCamera.EnablePostProcessing = false;
		Assert.IsNull( camera.SceneCamera.ResolveFogForSkybox( default ) );
		camera.SceneCamera.EnablePostProcessing = true;

		fog.Color = Color.Blue;
		Assert.AreEqual( Color.Blue, parent.SceneWorld.GetRenderingSkybox().ResolveFog( camera.SceneCamera ).Value.Color );
		skybox.FogOverride = null;
		Assert.IsNull( parent.SceneWorld.GetRenderingSkybox().ResolveFog( camera.SceneCamera ) );
		Assert.AreEqual( camera.SceneCamera.Fog, camera.SceneCamera.ResolveFogForSkybox( default ) );

		skybox.FogOverride = fog;
		skybox.Enabled = false;
		skybox.Enabled = true;
		Assert.AreEqual( fog.GetSettings( camera ), parent.SceneWorld.GetRenderingSkybox().ResolveFog( camera.SceneCamera ).Value );
		fog.GameObject.DestroyImmediate();
		Assert.IsNull( parent.SceneWorld.GetRenderingSkybox().ResolveFog( camera.SceneCamera ), "Destroyed overrides restore inherited fog" );
	}

	[TestMethod]
	[DataRow( true, false, false )]
	[DataRow( true, false, true )]
	[DataRow( true, true, false )]
	[DataRow( true, true, true )]
	[DataRow( false, false, false )]
	[DataRow( false, false, true )]
	[DataRow( false, true, false )]
	[DataRow( false, true, true )]
	public void RejectNestedSkyboxWithoutInvokingCallbacks( bool editor, bool batched, bool nestedEnabled )
	{
		var nested = source.CreateObject();
		nested.AddComponent<Skybox3D>( nestedEnabled );
		var invalid = SaveSource();
		nested.DestroyImmediate();
		var valid = SaveSource();
		var messages = new List<string>();
		void OnMessage( LogEvent e ) => messages.Add( e.Message );

		Logging.OnMessage += OnMessage;
		try
		{
			var skybox = LoadSkybox( invalid, editor, batched );

			Assert.IsTrue( messages.Any( x => x.Contains( "contains a nested Skybox3D" ) ), string.Join( "\n", messages ) );
			Assert.IsFalse( messages.Any( x => x.Contains( "failed from" ) || x.Contains( "Calling awake" ) ),
				string.Join( "\n", messages ) );
			Assert.AreEqual( 0, AwakeProbe.Count, "Rejected content must not invoke Awake." );
			Assert.AreEqual( 0, AwakeProbe.EnabledCount, "Rejected content must not invoke OnEnabled." );
			Assert.AreEqual( 0, parent.SceneWorld.InternalSkyboxWorlds.Count );
			Assert.IsNull( ContentScene( skybox ) );

			messages.Clear();
			skybox.SkyboxScene = valid;

			Assert.AreEqual( 1, AwakeProbe.Count );
			Assert.AreEqual( 1, AwakeProbe.EnabledCount );
			AssertMeshLoaded( skybox );
			Assert.IsFalse( messages.Any( x => x.Contains( "failed from" ) ), string.Join( "\n", messages ) );
		}
		finally
		{
			Logging.OnMessage -= OnMessage;
		}
	}
}
