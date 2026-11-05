using Editor.AnimGraph.Nodes;

namespace Editor.AnimGraph;

internal static class GraphLayout
{
	public static void Arrange( Data graph, bool recursive = false )
	{
		var visited = new HashSet<Data>();
		ArrangeGraph( graph );

		void ArrangeGraph( Data current )
		{
			if ( !visited.Add( current ) )
				return;

			ArrangeNodes( current );
			if ( !recursive )
				return;

			foreach ( var node in current.Nodes.OfType<Node>() )
			{
				if ( node is StateMachineNode machine )
				{
					for ( var i = 0; i < machine.States.Count; i++ )
						machine.States[i].Position = new Vector2( i % 3 * 400, i / 3 * 180 );
				}
				foreach ( var child in node.SubGraphs )
					ArrangeGraph( child );
			}
			foreach ( var parameter in current.VirtualParameters )
				ArrangeGraph( parameter.Graph );
		}
	}

	private static void ArrangeNodes( Data graph )
	{
		var nodes = graph.Nodes.OfType<Node>().Where( node => node is not CommentNode ).ToHashSet();
		var comments = graph.Nodes.OfType<CommentNode>().ToDictionary( comment => comment,
			comment => nodes.Where( node => new Rect( comment.Position, comment.Size ).IsInside( node.Position ) ).ToArray() );
		var ranks = new Dictionary<Node, int>();
		var visiting = new HashSet<Node>();

		int Rank( Node node )
		{
			if ( ranks.TryGetValue( node, out var rank ) )
				return rank;
			if ( !visiting.Add( node ) )
				throw new InvalidOperationException( "Cannot arrange a graph containing a connection cycle" );
			var sources = node.Inputs.Select( input => input.ConnectedOutput?.Node ).OfType<Node>().Where( nodes.Contains ).ToArray();
			rank = sources.Length == 0 ? 0 : sources.Max( Rank ) + 1;
			visiting.Remove( node );
			return ranks[node] = rank;
		}

		static float Height( Node node ) => Math.Max( 100, 65 + Math.Max( node.Inputs.Count(), node.Outputs.Count() ) * 26 + node.ExpandSize.y );

		foreach ( var column in nodes.GroupBy( Rank ).OrderBy( column => column.Key ) )
		{
			var y = 0f;
			foreach ( var node in column.OrderBy( node => node.Position.y ).ThenBy( node => node.Position.x ) )
			{
				node.Position = new Vector2( column.Key * 320, y );
				node.HandleOffsets.Clear();
				y += Height( node ) + 30;
			}
		}

		var noteY = -180f;
		foreach ( var (comment, members) in comments )
		{
			if ( members.Length == 0 )
			{
				comment.Position = new Vector2( 0, noteY );
				comment.Size = new Vector2( 600, 140 );
				noteY -= 180;
				continue;
			}
			var min = new Vector2( members.Min( node => node.Position.x ) - 30, members.Min( node => node.Position.y ) - 55 );
			var max = new Vector2( members.Max( node => node.Position.x ) + 290, members.Max( node => node.Position.y + Height( node ) ) + 30 );
			comment.Position = min;
			comment.Size = max - min;
		}
	}
}
