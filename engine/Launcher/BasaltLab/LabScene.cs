using Basalt;

namespace Sandbox.BasaltLab;

/// <summary>
/// A world and the camera looking at it.
/// </summary>
internal sealed class LabScene
{
	public RenderWorld World { get; } = new();
	public RenderView View { get; } = new() { Name = "Basalt Lab", ClearColor = new Color( 0.08f, 0.09f, 0.11f ) };

	/// <summary>
	/// SceneLab's Sun Shadows preset, built the same way and seen from the same place: a 6x6 grid of boxes on a floor,
	/// lit from the side by the sun.
	/// </summary>
	public static LabScene SunShadows( Source2Backend backend )
	{
		const int grid = 6;

		var scene = new LabScene();
		var world = scene.World;

		var box = backend.LoadMesh( "models/dev/box.vmdl" );
		var spacing = MathF.Max( box.Bounds.Size.x, box.Bounds.Size.y ) * 1.5f;
		var offset = (grid - 1) * spacing * 0.5f;

		for ( int x = 0; x < grid; x++ )
		{
			for ( int y = 0; y < grid; y++ )
			{
				world.Add( new MeshObject( box, new Transform( new Vector3( x * spacing - offset, y * spacing - offset, 0 ) ) ) );
			}
		}

		world.Lighting.SunDirection = new Vector3( -0.4f, 0.7f, -0.55f ).Normal;

		var extent = MathF.Max( grid * spacing, spacing * 2 );
		var floor = backend.LoadMesh( "models/dev/plane.vmdl" );
		var floorSize = MathF.Max( floor.Bounds.Size.x, 1 );
		var floorZ = box.Bounds.Mins.z - floor.Bounds.Maxs.z;
		world.Add( new MeshObject( floor, new Transform( new Vector3( 0, 0, floorZ ), Rotation.Identity, extent * 1.6f / floorSize ) ) );

		var distance = MathF.Max( box.Bounds.Size.Length * 1.6f, grid * spacing * 1.2f );
		var view = scene.View;
		view.Rotation = Rotation.From( 25, 35, 0 );
		view.Position = box.Bounds.Center - view.Rotation.Forward * distance;
		view.ZNear = MathF.Max( 0.1f, distance * 0.01f );
		view.ZFar = distance * 10.0f;

		return scene;
	}
}
