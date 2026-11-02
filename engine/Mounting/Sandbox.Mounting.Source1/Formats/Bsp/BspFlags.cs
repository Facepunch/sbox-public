namespace Sandbox.Mounting.Source1;

internal static class BspWorldLightType
{
	public const int Surface = 0;
	public const int Point = 1;
	public const int Spotlight = 2;
	public const int SkyLight = 3;
	public const int QuakeLight = 4;
	public const int SkyAmbient = 5;
}

internal static class BspWorldLightFlags
{
	public const int InAmbientCube = 0x1;
}

internal static class BspContents
{
	public const int Solid = 0x1;
	public const int Opaque = 0x80;
	public const int Moveable = 0x4000;
	public const int OpaqueMask = Solid | Opaque | Moveable;
}

internal static class BspSurfaceFlags
{
	public const int Sky2D = 0x2;
	public const int Sky = 0x4;
	public const int SkyMask = Sky2D | Sky;
	public const int Warp = 0x8;
	public const int NoDraw = 0x80;
	public const int BumpLight = 0x800;
}
