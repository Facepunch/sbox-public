using Sandbox;
using System.Collections.Generic;
using System.Linq;

public class MyToolIsCool : Sandbox.Tools.BaseTool
{
	List<Vector3> points;

	public override void Simulate()
	{
		base.Simulate();

		points ??= new List<Vector3>();

		var point = Owner.EyePosition + Owner.EyeRotation.Forward * 200;

		var tr = Trace.Ray( Owner.EyePosition, Owner.EyePosition + Owner.EyeRotation.Forward * 5000 )
			.UseHitboxes()
			.Ignore( Owner, true )
			.WithAllTags( "solid" )
			.Run();

		points.Add( tr.EndPosition + tr.Normal * 1.0f );

		while ( points.Count > 1000 )
			points.RemoveAt( 0 );

		var prev = points.First();
		foreach ( var p in points.Skip( 1 ) )
		{
			DebugOverlay.Line( prev, p, 0.01f );
			prev = p;
		}
	}
}
