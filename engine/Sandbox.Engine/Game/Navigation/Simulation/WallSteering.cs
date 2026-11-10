/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
DotRecast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com
Copyright (c) 2024 Facepunch Studios Ltd

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

// Adapted for cached navmesh boundaries and the scene-independent simulation.
using Sandbox.Navigation.Pathfinding;

namespace Sandbox.Navigation;

/// <summary>Predicts boundary crossings and samples a nearby velocity when steering would hit a wall.</summary>
internal static class WallSteering
{
	private const float Horizon = 0.75f;
	private static readonly Vector3[] Pattern = CreatePattern();
	private readonly record struct Segment( Vector3 Start, Vector3 End );

	internal static Vector3 Steer( SimulationAgent agent, Vector3 desired, Vector3 goal, float distanceToCorner )
	{
		float speed = desired.Length;
		if ( speed < 0.001f ) return desired;
		// The route turns or ends at the next corner; do not predict straight-line
		// collisions beyond it.
		float horizon = MathF.Min( Horizon, distanceToCorner / speed );
		if ( horizon < 0.0001f ) return desired;
		float range = MathF.Max( agent.Options.Radius * 16, agent.Options.MaxSpeed );
		if ( agent.WallRevision != agent.Owner.Revision
			|| MathF.Abs( agent.Position.y - agent.WallCenter.y ) > agent.Options.Height * 0.25f
			|| Geometry.DistanceBetween2DSqr( agent.Position, agent.WallCenter ) > range * range * 0.0625f )
		{
			agent.WallCount = agent.Query.FindLocalWalls( agent.Path[0], agent.Position, range, agent.Options.Height, agent.Options.Filter, agent.Walls );
			agent.WallCenter = agent.Position;
			agent.WallRevision = agent.Owner.Revision;
		}
		Span<Segment> segments = stackalloc Segment[32];
		int count = 0;
		for ( int i = 0; i < agent.WallCount; i++ )
		{
			var wall = agent.Walls[i];
			if ( Geometry.TriArea2D( agent.Position, wall.Start, wall.End ) < 0 ) continue;
			var a = wall.Start - agent.Position;
			var b = wall.End - agent.Position;
			float goalDistance = Geometry.DistancePtSegSqr2D( goal, wall.Start, wall.End, out float t );
			float radiusSquared = agent.Options.Radius * agent.Options.Radius;
			// Destinations and link entrances on an edge must remain reachable.
			if ( goalDistance < radiusSquared )
			{
				float lengthSquared = Geometry.DistanceBetween2DSqr( a, b );
				if ( lengthSquared < 0.0001f ) continue;
				float chord = MathF.Sqrt( (radiusSquared - goalDistance) / lengthSquared );
				if ( t - chord > 0 ) segments[count++] = new( a, Vector3.Lerp( a, b, t - chord ) );
				if ( t + chord < 1 ) segments[count++] = new( Vector3.Lerp( a, b, t + chord ), b );
			}
			else segments[count++] = new( a, b );
		}
		var walls = segments[..count];
		var current = agent.Velocity.WithY( 0 );
		if ( CollisionTime( desired, walls, horizon ) >= horizon && CollisionTime( current, walls, horizon ) >= horizon ) return desired;

		var direction = desired / speed;
		var result = desired * 0.4f;
		float radius = speed * 0.6f;
		for ( int depth = 0; depth < 4; depth++ )
		{
			float bestPenalty = float.MaxValue;
			Vector3 best = default;
			foreach ( var point in Pattern )
			{
				var offset = new Vector3( direction.x * point.x - direction.z * point.z, 0, direction.z * point.x + direction.x * point.z );
				var candidate = result + offset * radius;
				if ( candidate.LengthSquared > (speed + 0.001f) * (speed + 0.001f) ) continue;
				float impact = MathF.Min( horizon, CollisionTime( candidate, walls, horizon ) );
				float penalty = 2 * candidate.Distance( desired ) / speed + 0.75f * candidate.Distance( current ) / speed + 2.5f / (0.1f + impact / horizon);
				if ( penalty < bestPenalty ) { bestPenalty = penalty; best = candidate; }
			}
			result = best;
			radius *= 0.5f;
		}
		return result;
	}

	private static float CollisionTime( Vector3 velocity, Span<Segment> walls, float horizon )
	{
		float earliest = float.PositiveInfinity;
		foreach ( var wall in walls )
		{
			var edge = (wall.End - wall.Start).WithY( 0 );
			var inward = new Vector3( edge.z, 0, -edge.x );
			if ( Vector3.Dot( velocity, inward ) >= 0 ) continue;
			if ( Geometry.DistancePtSegSqr2D( default, wall.Start, wall.End, out float closest ) < 0.0001f )
			{
				// Moving around an endpoint is allowed; another edge may still block it.
				float along = Vector3.Dot( velocity, edge );
				if ( closest <= 0 && along < 0 || closest >= 1 && along > 0 ) continue;
				return 0;
			}
			if ( Geometry.IntersectSegSeg2D( default, velocity * horizon, wall.Start, wall.End, out float time, out float edgePosition )
				&& time >= 0 && time <= 1 && edgePosition >= 0 && edgePosition <= 1 ) earliest = MathF.Min( earliest, time * horizon );
		}
		return earliest;
	}

	private static Vector3[] CreatePattern()
	{
		var pattern = new Vector3[15];
		for ( int ring = 0; ring < 2; ring++ )
			for ( int i = 0; i < 7; i++ )
			{
				float angle = (i + ring * 0.5f) * MathF.Tau / 7;
				pattern[1 + ring * 7 + i] = new Vector3( MathF.Cos( angle ), 0, MathF.Sin( angle ) ) * (1 - ring * 0.5f);
			}
		return pattern;
	}
}
