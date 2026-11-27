using Sandbox.Utility;
using static Sandbox.Component;

namespace Sandbox;

public partial class Scene
{
	/// <summary>
	/// The component tick loop while a profiler capture records, with a scope per component type.
	/// </summary>
	void TickComponentsProfiled( HashSetEx<Component> components, TickPhase phase )
	{
		foreach ( var c in components.EnumerateLocked( phase == TickPhase.Update ) )
		{
			if ( phase == TickPhase.FixedUpdate && !c.IsValid() ) continue;

			using var cpu = c.GetTickProfileScope( phase ).Start();
			switch ( phase )
			{
				case TickPhase.Update: c.InternalUpdate(); break;
				case TickPhase.FixedUpdate: c.InternalFixedUpdate(); break;
				case TickPhase.PreRender: c.OnPreRenderInternal(); break;
			}
		}
	}
}
