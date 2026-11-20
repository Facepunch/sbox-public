namespace Sandbox.Npcs.Layers;

/// <summary>
/// A behavior layer provides specific services for tasks to use -- we don't use behavior layers for state, they are services.
/// </summary>
public abstract class BaseNpcLayer : Component
{
	private Npc _npc;

	/// <summary>
	/// The owning NPC
	/// </summary>
	protected Npc Npc
	{
		get
		{
			_npc ??= GetComponent<Npc>();
			return _npc;
		}
	}

	/// <summary>
	/// Optional debug string shown in the NPC debug overlay. Return null to skip.
	/// </summary>
	public virtual string GetDebugString() => null;

	/// <summary>
	/// Reset any runtime state on this layer.
	/// </summary>
	public virtual void ResetLayer() { }
}
