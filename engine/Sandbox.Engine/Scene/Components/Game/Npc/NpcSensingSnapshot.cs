using Sandbox.Npcs.Layers;
using System.Runtime.InteropServices;

namespace Sandbox.Npcs;

internal sealed class NpcSensingSnapshot
{
	internal struct Target
	{
		public GameObject Object;
		public BaseNpc SourceNpc;
		public Component Damageable;
		public Vector3 Position, Velocity;
		public float Speed;
		public SensesLayer.SenseTargets Kind;
		public bool IsPlayer;
		Vector3 _eyePosition;
		bool _hasEyePosition;

		public readonly bool IsLive => Object.IsValid() && (Kind != SensesLayer.SenseTargets.Npcs || (SourceNpc.IsValid() && SourceNpc.Active))
			&& (Damageable.IsValid() && Damageable.Active
			&& (Damageable.GameObject == Object || Damageable.GameObject.IsAncestor( Object )) || BaseNpc.IsLiveTarget( Object ));

		public Vector3 CalculateEyePosition()
		{
			if ( !_hasEyePosition )
			{
				_eyePosition = Object.GetComponent<BaseNpc>() is { } npc ? npc.EyePosition : Object.GetBounds().Center;
				_hasEyePosition = true;
			}
			return _eyePosition;
		}
	}

	readonly List<Target> _targets = new();
	internal Span<Target> Targets => CollectionsMarshal.AsSpan( _targets );

	internal void Capture( Scene scene, List<BaseNpc> npcs, SensesLayer.SenseTargets kinds )
	{
		_targets.Clear();
		if ( (kinds & SensesLayer.SenseTargets.Npcs) != 0 )
		{
			foreach ( var npc in npcs )
				if ( npc.IsValid() && npc.Active ) CaptureTarget( npc.GameObject, SensesLayer.SenseTargets.Npcs, npc );
		}
		if ( !NpcConVars.NoTarget && (kinds & SensesLayer.SenseTargets.Players) != 0 )
		{
			foreach ( var player in scene.Query<PlayerController>() ) CaptureTarget( player.GameObject, SensesLayer.SenseTargets.Players );
		}
	}

	void CaptureTarget( GameObject obj, SensesLayer.SenseTargets kind, BaseNpc sourceNpc = null )
	{
		if ( !obj.IsValid() || obj.GetComponentInChildren<Component.IDamageable>() is not Component { Active: true } damageable ) return;
		var velocity = BaseNpc.GetVelocity( obj ).WithZ( 0 );
		_targets.Add( new Target
		{
			Object = obj,
			SourceNpc = sourceNpc,
			Damageable = damageable,
			Position = obj.WorldPosition,
			Velocity = velocity,
			Speed = velocity.Length,
			IsPlayer = obj.GetComponent<PlayerController>().IsValid(),
			Kind = kind
		} );
	}

	internal void Clear() => _targets.Clear();
}
