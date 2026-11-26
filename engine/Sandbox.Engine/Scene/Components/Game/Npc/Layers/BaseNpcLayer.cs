namespace Sandbox.Npcs.Layers;

/// <summary>
/// A behavior layer provides specific services for tasks to use -- we don't use behavior layers for state, they are services.
/// </summary>
public abstract class BaseNpcLayer : Component
{
	private BaseNpc _npc;

	/// <summary>
	/// The owning NPC on this GameObject, or null if none is available.
	/// </summary>
	protected BaseNpc Npc
	{
		get
		{
			if ( !_npc.IsValid() )
				_npc = GetComponent<BaseNpc>();

			return _npc;
		}
	}

	/// <summary>
	/// Optional debug string shown in the NPC debug overlay. Return null to skip.
	/// </summary>
	internal virtual string GetDebugString() => null;
}
