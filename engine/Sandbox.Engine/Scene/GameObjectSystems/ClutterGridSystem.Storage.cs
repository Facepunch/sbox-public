using System;
using System.Collections.Generic;

namespace Sandbox.Clutter;

public sealed partial class ClutterGridSystem
{
	/// <summary>
	/// Manages storage and serialization of painted clutter instances.
	/// Uses binary serialization via BlobData for efficient storage.
	/// </summary>
	public sealed class ClutterStorage : BlobData
	{
		public override int Version => 1;

		public record struct Instance( Vector3 Position, Rotation Rotation, float Scale = 1f );

		private Dictionary<string, List<Instance>> _instances = [];

		public ClutterStorage() { }

		/// <summary>
		/// Gets the total number of instances across all models.
		/// </summary>
		public int TotalCount
		{
			get
			{
				var count = 0;
				foreach ( var list in _instances.Values )
					count += list.Count;
				return count;
			}
		}

		/// <summary>
		/// Gets all model paths that have instances.
		/// </summary>
		public IEnumerable<string> ModelPaths => _instances.Keys;

		/// <summary>
		/// Gets instances for a specific model path.
		/// </summary>
		public IReadOnlyList<Instance> GetInstances( string modelPath )
		{
			if ( _instances.TryGetValue( modelPath, out var list ) )
				return list;

			return [];
		}

		/// <summary>
		/// Gets all instances grouped by model path.
		/// </summary>
		public IReadOnlyDictionary<string, List<Instance>> GetAllInstances() => _instances;

		/// <summary>
		/// Adds a single instance for a model.
		/// </summary>
		public void AddInstance( string modelPath, Vector3 position, Rotation rotation, float scale = 1f )
		{
			if ( string.IsNullOrEmpty( modelPath ) ) return;

			if ( !_instances.TryGetValue( modelPath, out var list ) )
			{
				list = [];
				_instances[modelPath] = list;
			}

			list.Add( new Instance( position, rotation, scale ) );
		}

		/// <summary>
		/// Adds multiple instances for a model.
		/// </summary>
		public void AddInstances( string modelPath, IEnumerable<Instance> instances )
		{
			if ( string.IsNullOrEmpty( modelPath ) ) return;

			if ( !_instances.TryGetValue( modelPath, out var list ) )
			{
				list = [];
				_instances[modelPath] = list;
			}

			list.AddRange( instances );
		}

		/// <summary>
		/// Erases all instances within a radius of a position.
		/// </summary>
		public int Erase( Vector3 position, float radius )
		{
			if ( _instances.Count == 0 ) return 0;

			var radiusSquared = radius * radius;
			var totalRemoved = 0;

			foreach ( var list in _instances.Values )
			{
				var removed = list.RemoveAll( i => i.Position.DistanceSquared( position ) <= radiusSquared );
				totalRemoved += removed;
			}

			return totalRemoved;
		}

		/// <summary>
		/// Clears all instances for a specific model.
		/// </summary>
		public bool ClearModel( string modelPath )
		{
			return _instances.Remove( modelPath );
		}

		/// <summary>
		/// Clears all instances.
		/// </summary>
		public void ClearAll()
		{
			_instances.Clear();
		}

		/// <summary>
		/// Serialize to binary format.
		/// </summary>
		public override void Serialize( ref Writer writer )
		{
			// Write model count
			writer.Stream.Write( _instances.Count );

			foreach ( var (modelPath, instances) in _instances )
			{
				// Write model path
				writer.Stream.Write( modelPath );

				// Write instance count
				writer.Stream.Write( instances.Count );

				// Write each instance
				foreach ( var instance in instances )
				{
					writer.Stream.Write( instance.Position );
					writer.Stream.Write( instance.Rotation );
					writer.Stream.Write( instance.Scale );
				}
			}
		}

		/// <summary>
		/// Deserialize from binary format.
		/// </summary>
		public override void Deserialize( ref Reader reader )
		{
			_instances.Clear();

			// Read model count
			var modelCount = reader.Stream.Read<int>();

			for ( int m = 0; m < modelCount; m++ )
			{
				// Read model path
				var modelPath = reader.Stream.Read<string>();

				// Read instance count
				var instanceCount = reader.Stream.Read<int>();

				var instances = new List<Instance>( instanceCount );

				// Read each instance
				for ( int i = 0; i < instanceCount; i++ )
				{
					var position = reader.Stream.Read<Vector3>();
					var rotation = reader.Stream.Read<Rotation>();
					var scale = reader.Stream.Read<float>();

					instances.Add( new Instance( position, rotation, scale ) );
				}

				if ( !string.IsNullOrEmpty( modelPath ) && instances.Count > 0 )
				{
					_instances[modelPath] = instances;
				}
			}
		}
	}
}
