using System.Runtime.InteropServices;

namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoDraw
	{
		readonly List<VertexSceneObject> _batches = new();
		int _batchCursor;
		int _batchFrame;
		int _visibleBatches;
		int _vertexStart;
		Transform _vertexTransform;
		bool _worldSpace;
		bool _logicalValid;
		string _logicalPath;
		Transform _logicalTransform;
		VertexSceneObject.RenderState _logicalState;
		Color _logicalColor;

		// Finish the previous submission before its scope or transform changes. Sprites already contain world positions.
		void BakeVertices()
		{
			if ( _vertexObject is null || _vertexObject.PrimitiveType == Graphics.PrimitiveType.Points ) return;
			var vertices = CollectionsMarshal.AsSpan( _vertexObject.Vertices );
			if ( _worldSpace && _vertexTransform != Transform.Zero )
			{
				// These shaders are unshaded; only positions need a world transform.
				var matrix = Matrix.CreateScale( _vertexTransform.Scale ) * Matrix.CreateRotation( _vertexTransform.Rotation ) * Matrix.CreateTranslation( _vertexTransform.Position );
				for ( var i = _vertexStart; i < vertices.Length; ++i )
				{
					vertices[i].Position = matrix.Transform( vertices[i].Position );
				}
			}
			if ( _worldSpace ) _vertexStart = vertices.Length;
		}

		internal void EndFrame()
		{
			Flush();
			HideUnused();
			// Used batches form a prefix, so the oldest dormant batches form a suffix.
			while ( _batches.Count > _batchCursor && _batchFrame - _batches[^1].LastUsedFrame >= 64 )
			{
				_batches[^1].Delete();
				_batches.RemoveAt( _batches.Count - 1 );
				Active.ObjectsDestroyed++;
			}
		}

		void HideUnused()
		{
			for ( var i = _batchCursor; i < _visibleBatches; ++i ) _batches[i].SetVisible( false );
			_visibleBatches = _batchCursor;
		}


		internal void Clear()
		{
			foreach ( var batch in _batches ) batch.Delete();
			_batches.Clear();
			_visibleBatches = 0;
			_batchCursor = 0;
			_vertexObject = null;
		}

		VertexSceneObject VertexObject( Graphics.PrimitiveType type, Material material, Texture texture = null, bool isolated = false, bool worldSpace = false )
			=> Active.Renderer.GetVertexObject( type, material, texture, isolated, worldSpace );

		VertexSceneObject GetVertexObject( Graphics.PrimitiveType type, Material material, Texture texture, bool isolated, bool worldSpace )
		{
			BakeVertices();
			var transform = worldSpace || type == Graphics.PrimitiveType.Points ? Transform.Zero : Transform;

			// Sprites read vertex colors. Keep line/solid tint boundaries to preserve overlay ordering.
			// Only adjacent submissions are merged, preserving draw order.
			var state = new VertexSceneObject.RenderState( type, material, texture, IgnoreDepth, CullBackfaces, LineThickness, type == Graphics.PrimitiveType.Points ? Color.White : Color );
			// Batching must not change the numbering of scopes and pooled objects after this draw.
			var logicalState = state with { Color = Color.White };
			if ( !_logicalValid || _logicalPath != Path || _logicalTransform != Transform || _logicalState != logicalState
				|| type == Graphics.PrimitiveType.Points && _logicalColor != Color ) Active.scope.Create++;
			_logicalValid = true;
			_logicalPath = Path;
			_logicalTransform = Transform;
			_logicalState = logicalState;
			_logicalColor = Color;
			if ( !isolated && _vertexObject is not null && _vertexObject.State == state )
			{
				// Keep a native transform until different object transforms actually share a batch.
				if ( !_worldSpace && _vertexObject.Transform != transform )
				{
					_worldSpace = true;
					_vertexTransform = _vertexObject.Transform;
					BakeVertices();
					_vertexObject.Transform = Transform.Zero;
				}
				_vertexTransform = transform;
				return _vertexObject;
			}

			// Switching to a different object, so flush the batch we just finished.
			_vertexObject?.Write();

			if ( _batchCursor == _batches.Count )
			{
				_batches.Add( new VertexSceneObject( World ) );
				Active.ObjectsCreated++;
			}
			var so = _batches[_batchCursor++];
			so.LastUsedFrame = _batchFrame;
			_vertexObject = so;

			so.State = state;
			_worldSpace = worldSpace || type == Graphics.PrimitiveType.Points;
			so.Transform = transform;
			_vertexTransform = transform;
			so.Vertices.Clear();
			_vertexStart = 0;

			return _vertexObject;
		}

	}



}
