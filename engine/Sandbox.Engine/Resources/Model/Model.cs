using NativeEngine;
using Sandbox.Engine;
using Sandbox.Engine.Utility.RayTrace;

namespace Sandbox;

/// <summary>
/// A model.
/// </summary>
public sealed partial class Model : Resource
{
	internal IModel native;
	internal bool procedural;

	public override bool IsValid => native.IsValid;

	/// <summary>
	/// Private constructor, use <see cref="FromNative(IModel, bool, string)"/>
	/// </summary>
	private Model( IModel native, string name, bool procedural )
	{
		if ( native.IsNull ) throw new Exception( "Model pointer cannot be null!" );

		this.native = native;
		this.Name = name;
		this.procedural = procedural;

		SetIdFromResourcePath( Name );
	}

	internal void Dispose()
	{
		if ( !native.IsNull )
		{
			var n = native;
			native = default;

			MainThread.Queue( () => n.DestroyStrongHandle() );
		}
	}

	~Model()
	{
		Dispose();
	}

	/// <summary>
	/// Called when the resource is reloaded. We should clear any cached values.
	/// </summary>
	internal override void OnReloaded()
	{
		_data?.Dispose();
		_data = null;

		_physics?.Dispose();
		_physics = default;

		_morphs?.Dispose();
		_morphs = null;

		_attachments?.Dispose();
		_attachments = default;

		_animationNames?.Clear();
		_animationNames = default;

		_sequenceNames?.Clear();
		_sequenceNames = default;

		_materials = default;

		_bones = default;
		_hitboxset = default;

		_parts?.Dispose();
		_parts = default;

		DataCache?.Clear();

		IToolsDll.Current?.RunEvent( "model.reload", this );

		foreach ( var scene in Scene.All )
		{
			using var scope = scene.Push();

			var components = scene.GetAllComponents<IHasModel>();
			foreach ( var c in components )
			{
				if ( c.Model != this ) continue;

				c.OnModelReloaded();
			}
		}
	}

	/// <summary>
	/// Whether this model is an error model or invalid or not.
	/// </summary>
	public bool IsError => native.IsNull || !native.IsStrongHandleValid() || native.IsError();

	/// <summary>
	/// Name of the model, usually being its file path.
	/// </summary>
	public string Name { get; internal set; }

	/// <summary>
	/// Whether this model is procedural, i.e. it was created at runtime via <see cref="ModelBuilder.Create"/>.
	/// </summary>
	public bool IsProcedural => procedural;

	/// <summary>
	/// Total number of meshes this model is made out of.
	/// </summary>
	public int MeshCount => native.GetNumMeshes();

	/// <summary>
	/// Trace against the triangles in this mesh
	/// </summary>
	public MeshTraceRequest Trace => new() { targetModel = this };


}

internal interface IHasModel
{
	/// <summary>
	/// The <see cref="Model"/> associated with this object.
	/// </summary>
	Model Model { get; }

	/// <summary>
	/// Called by the engine when the associated <see cref="Model"/> is reloaded.
	/// Implementing classes should clear or update any cached data that depends on the model.
	/// </summary>
	void OnModelReloaded();
}
