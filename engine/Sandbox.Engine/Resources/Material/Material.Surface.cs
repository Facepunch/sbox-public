namespace Sandbox;

public partial class Material
{
	internal Surface Surface
	{
		get
		{
			var index = native.GetPhysicsSurfaceProperties();
			if ( index < 0 ) return null;

			return Surface.FindByIndex( index );
		}
	}
}
