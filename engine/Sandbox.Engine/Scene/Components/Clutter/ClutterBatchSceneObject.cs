using System.Runtime.InteropServices;

namespace Sandbox.Clutter;

/// <summary>
/// Custom scene object for rendering batched clutter models.
/// Groups instances by model type for efficient GPU instanced rendering.
/// </summary>
internal class ClutterBatchSceneObject : SceneCustomObject
{
	/// <summary>
	/// Batches by model
	/// </summary>
	private readonly Dictionary<Model, ClutterModelBatch> _batches = [];

	public ClutterBatchSceneObject( SceneWorld world ) : base( world )
	{
		Flags.IsOpaque = true;
		Flags.IsTranslucent = false;
		Flags.CastShadows = true;
		Flags.WantsPrePass = true;
	}

	/// <summary>
	/// Adds a clutter instance to the appropriate batch.
	/// </summary>
	public void AddInstance( ClutterInstance instance )
	{
		if ( instance.Entry?.Model == null )
			return;

		var model = instance.Entry.Model;

		if ( !_batches.TryGetValue( model, out var batch ) )
		{
			batch = new ClutterModelBatch( model );
			_batches[model] = batch;
		}

		batch.AddInstance( instance.Transform );
	}

	/// <summary>
	/// Clears all batches.
	/// </summary>
	public void Clear()
	{
		foreach ( var batch in _batches.Values )
			batch.Clear();

		_batches.Clear();
	}

	/// <summary>
	/// Called when the batch is deleted. Cleans up resources.
	/// </summary>
	public new void Delete()
	{
		Clear();
		base.Delete();
	}

	/// <summary>
	/// Renders all batched instances using GPU instancing.
	/// </summary>
	public override void RenderSceneObject()
	{
		if ( _batches.Count == 0 )
			return;

		foreach ( var (model, batch) in _batches )
		{
			if ( batch.Transforms.Count == 0 || model == null )
				continue;

			Graphics.DrawModelInstanced( model, CollectionsMarshal.AsSpan( batch.Transforms ) );
		}
	}
}
