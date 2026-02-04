namespace Editor.MeshEditor;

static class SelectionFrameUtil
{
	public static void FramePoints( IEnumerable<Vector3> points )
	{
		var list = points.ToList();
		if ( list.Count == 0 )
			return;

		var bounds = new BBox( list[0], list[0] );
		foreach ( var p in list )
			bounds = bounds.AddPoint( p );

		const float minPadding = 5f;

		if ( bounds.Size.Length < minPadding )
		{
			var center = bounds.Center;
			var halfPadding = minPadding / 2f;

			bounds = new BBox(
				center - new Vector3( halfPadding ),
				center + new Vector3( halfPadding )
			);
		}

		SceneEditorSession.Active.FrameTo( bounds );
	}
}
