using Sandbox.Engine.Resources;
using Sandbox.Navigation;
using System;

namespace Sandbox;

/// <summary>
/// How many agents may cross a <see cref="NavMeshLink"/> at once.
/// </summary>
[Expose]
public enum NavMeshLinkOccupancy
{
	/// <summary>Any number of agents cross at once.</summary>
	Unlimited,

	/// <summary>One agent crosses at a time. Others wait at their end of the link until it is free, first come, first served.</summary>
	OneAtATime
}

/// <summary>
/// NavigationLinks connect navigation mesh polygons for pathfinding and enable shortcuts like ladders, jumps, or teleports.
/// </summary>
[Expose]
[Title( "NavMesh - Link" )]
[Category( "Navigation" )]
[Icon( "link" )]
[EditorHandle( "materials/gizmo/navmeshagent.png" )]
[Alias( "NavAgent" )]
public class NavMeshLink : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Start position relative to the game object's position.
	/// </summary>
	[Property]
	public Vector3 LocalStartPosition { get; set; }

	/// <summary>
	/// End position relative to the game object's position.
	/// </summary>
	[Property]
	public Vector3 LocalEndPosition { get; set; }

	/// <summary>
	/// Start position in world space snapped to the navmesh.
	/// </summary>
	public Vector3? WorldStartPositionOnNavmesh
	{
		get => startOnNavmesh;
	}

	/// <summary>
	/// End position in world space snapped to the navmesh.
	/// </summary>
	public Vector3? WorldEndPositionOnNavmesh
	{
		get => endOnNavmesh;
	}

	// Written by the navmesh in every navigation update
	internal Vector3? startOnNavmesh, endOnNavmesh;

	/// <summary>
	/// Whether this link can be traverse bi-directional or only start towards end.
	/// </summary>
	[Property]
	public bool IsBiDirectional = true;

	/// <summary>
	/// Radius that will be searched at the start and end positions for a connection to the navmesh.
	/// </summary>
	[Property]
	public float ConnectionRadius = 16f;

	/// <summary>
	/// The NavMesh area definition to apply to this link.
	/// </summary>
	[Property]
	public NavMeshAreaDefinition Area { get; set; }

	/// <summary>
	/// What kind of link this is, for agents to allow, for paths to price and for game code to pick how to cross it. Without one it is a plain link that every agent takes.
	/// <see cref="NavMeshLinkType"/> has the ones the engine ships. Fixed on a generated link: setting it there does nothing.
	/// </summary>
	[Property]
	public NavMeshLinkType Type
	{
		get;
		set
		{
			if ( !IsGenerated ) field = value;
		}
	}

	/// <summary>
	/// How many agents may cross at once.
	/// </summary>
	[Property]
	public NavMeshLinkOccupancy Occupancy { get; set; }

	/// <summary>
	/// The agents crossing this link, in the order they entered.
	/// </summary>
	public IReadOnlyList<NavMeshAgent> Occupants => occupants ?? (IReadOnlyList<NavMeshAgent>)Array.Empty<NavMeshAgent>();

	/// <summary>
	/// True while an agent crosses this link.
	/// </summary>
	public bool IsOccupied => occupants is { Count: > 0 };

	// Made when the first agent enters, so a link nobody takes carries no list
	internal List<NavMeshAgent> occupants;

	// Agents waiting at either end
	internal int queued;

	/// <summary>
	/// True for a drop or jump the navmesh generated. The navmesh owns it: it is not saved with the scene and may be destroyed or recreated at any time.
	/// Its type, positions, radius, direction and area are fixed and changes to them are ignored. <see cref="Occupancy"/> and the enter and exit callbacks may be used.
	/// </summary>
	public bool IsGenerated { get; internal set; }

	// What the navmesh reported for a generated link
	internal Nav.NavLink generated;

	/// <summary>
	/// Emitted when an agent enters the link.
	/// </summary>
	public Action<NavMeshAgent> LinkEntered { get; set; }

	/// <summary>
	/// Emitted when an agent exits the link.
	/// </summary>
	public Action<NavMeshAgent> LinkExited { get; set; }

	/// <summary>
	/// Start position in world space.
	/// </summary>
	public Vector3 WorldStartPosition
	{
		get => WorldTransform.PointToWorld( LocalStartPosition );
		set
		{
			LocalStartPosition = WorldTransform.PointToLocal( value );
		}
	}

	/// <summary>
	/// End position in world space.
	/// </summary>
	public Vector3 WorldEndPosition
	{
		get => WorldTransform.PointToWorld( LocalEndPosition );
		set
		{
			LocalEndPosition = WorldTransform.PointToLocal( value );
		}
	}

	/// <summary>
	/// Called when an agent enters the link.
	/// </summary>
	protected virtual void OnLinkEntered( NavMeshAgent agent )
	{
	}

	/// <summary>
	/// Called when an agent exits the link.
	/// </summary>
	protected virtual void OnLinkExited( NavMeshAgent agent )
	{
	}

	internal void TriggetEntered( NavMeshAgent agent )
	{
		if ( !Active )
			return;

		LinkEntered?.Invoke( agent );
		OnLinkEntered( agent );
	}

	internal void TriggetExited( NavMeshAgent agent )
	{
		if ( !Active )
			return;

		LinkExited?.Invoke( agent );
		OnLinkExited( agent );
	}

	internal override void OnEnabledInternal()
	{
		Scene.NavMesh.AddLink( this );
		base.OnEnabledInternal();
	}

	internal override void OnDisabledInternal()
	{
		Scene.NavMesh?.RemoveLink( this );
		base.OnDisabledInternal();
	}

	protected override void DrawGizmos()
	{
		// Generated links are drawn with the navmesh
		if ( !Active || IsGenerated )
			return;

		bool startConnected = startOnNavmesh.HasValue, endConnected = endOnNavmesh.HasValue;

		Gizmo.Draw.Color = startConnected ? NavMesh.debugInnerLineColor.WithAlpha( 1 ) : Color.Red;
		Gizmo.Draw.LineSphere( LocalStartPosition, ConnectionRadius );
		Gizmo.Draw.Color = endConnected ? NavMesh.debugInnerLineColor.WithAlpha( 1 ) : Color.Red;
		Gizmo.Draw.LineSphere( LocalEndPosition, ConnectionRadius );

		var drawStartPosition = LocalStartPosition;
		if ( startConnected )
		{
			drawStartPosition = WorldTransform.PointToLocal( startOnNavmesh.Value ) + NavMesh.debugDrawGroundOffset + new Vector3( 0, 0, 0.01f );
			Gizmo.Draw.Color = NavMesh.debugInnerLineColor.WithAlpha( 1 );
			Gizmo.Draw.SolidSphere( drawStartPosition, 8f );
		}

		var drawEndPosition = LocalEndPosition;
		if ( endConnected )
		{
			drawEndPosition = WorldTransform.PointToLocal( endOnNavmesh.Value ) + NavMesh.debugDrawGroundOffset + new Vector3( 0, 0, 0.01f );
			Gizmo.Draw.Color = NavMesh.debugInnerLineColor.WithAlpha( 1 );
			Gizmo.Draw.SolidSphere( drawEndPosition, 8f );
		}

		Gizmo.Draw.LineThickness = 2f;
		Gizmo.Draw.Color = (Type?.Color ?? NavMesh.debugTileBorderColor).WithAlpha( 1 );

		// Draw arc between start end
		int segments = 8;
		int endToStartArrowSegment = segments / 4;
		int startToEndArrowSegment = segments - endToStartArrowSegment;
		Vector3 previousPoint = drawStartPosition;
		for ( int i = 1; i <= segments; i++ )
		{
			float t = (float)i / segments;
			Vector3 point = Vector3.Lerp( drawStartPosition, drawEndPosition, t );
			point.z += MathF.Sin( t * MathF.PI ) * MathF.Abs( drawStartPosition.z - drawEndPosition.z ); // curve upwards
			if ( i == startToEndArrowSegment )
			{
				Gizmo.Draw.Arrow( previousPoint, point, 8f );
			}
			else if ( i == endToStartArrowSegment + 1 && IsBiDirectional )
			{
				Gizmo.Draw.Arrow( point, previousPoint, 8f );
			}
			else
			{
				Gizmo.Draw.Line( previousPoint, point );

			}
			previousPoint = point;
		}
	}

	internal Nav.NavLink CreateLink()
	{
		return new()
		{
			Start = WorldStartPosition,
			End = WorldEndPosition,
			Radius = ConnectionRadius,
			Bidirectional = IsBiDirectional,
			// A link without a definition has an area of its own, which every agent may cross
			Area = (byte)(Area is null ? NavMesh.LinkArea : Scene.NavMesh.AreaDefinitionToId( Area )),
			Type = (byte)Scene.NavMesh.LinkTypeToId( Type )
		};
	}
}
