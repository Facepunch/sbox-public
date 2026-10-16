namespace Sandbox.Rendering;

/// <summary>
/// Deferred metadata for a view recorded into a command list. Bind these references after its draw command
/// in the same command-list execution. Values are resolved anew on each playback, not while recording.
/// Keep the target textures alive and unchanged until their consumers have drawn.
/// </summary>
public readonly ref struct ViewHandle
{
	internal long Id { get; }

	internal ViewHandle( long id )
	{
		Id = id;
	}

	/// <summary>
	/// Inverse reverse-Z projection of the rendered view. Unproject normalized reverse-Z depth without applying 1 - depth.
	/// </summary>
	public ProjectionToViewRef ProjectionToView => new( Id );

	/// <summary>
	/// A deferred Matrix attribute reference for <see cref="ProjectionToView"/>.
	/// </summary>
	public readonly ref struct ProjectionToViewRef
	{
		internal long Id { get; }

		internal ProjectionToViewRef( long id )
		{
			Id = id;
		}
	}

	/// <summary>
	/// Scale (xy) and bias (zw) mapping final colour UVs to the unflipped depth texture.
	/// </summary>
	public DepthUvScaleBiasRef DepthUvScaleBias => new( Id );

	/// <summary>
	/// A deferred Vector4 attribute reference for <see cref="DepthUvScaleBias"/>.
	/// </summary>
	public readonly ref struct DepthUvScaleBiasRef
	{
		internal long Id { get; }

		internal DepthUvScaleBiasRef( long id )
		{
			Id = id;
		}
	}

	/// <summary>
	/// Main-world viewport origin (xy) and size (zw) in unflipped depth texture UVs.
	/// </summary>
	public ViewportRef Viewport => new( Id );

	/// <summary>
	/// A deferred Vector4 attribute reference for <see cref="Viewport"/>.
	/// </summary>
	public readonly ref struct ViewportRef
	{
		internal long Id { get; }

		internal ViewportRef( long id )
		{
			Id = id;
		}
	}

	/// <summary>
	/// Minimum and maximum main-world depth values. Normalize sampled depth to this range before unprojection.
	/// </summary>
	public DepthRangeRef DepthRange => new( Id );

	/// <summary>
	/// A deferred Vector2 attribute reference for <see cref="DepthRange"/>.
	/// </summary>
	public readonly ref struct DepthRangeRef
	{
		internal long Id { get; }

		internal DepthRangeRef( long id )
		{
			Id = id;
		}
	}

	/// <summary>
	/// Whether this command submitted a view during the current playback. False for skipped views;
	/// this does not indicate whether a depth target was supplied.
	/// </summary>
	public IsValidRef IsValid => new( Id );

	/// <summary>
	/// A deferred bool attribute reference for <see cref="IsValid"/>.
	/// </summary>
	public readonly ref struct IsValidRef
	{
		internal long Id { get; }

		internal IsValidRef( long id )
		{
			Id = id;
		}
	}
}

/// <summary>
/// A value copy of a submitted view's reconstruction data, scoped to one command-list playback.
/// </summary>
internal struct ViewData
{
	internal Matrix ProjectionToView;
	internal Vector4 DepthUvScaleBias;
	internal Vector4 Viewport;
	internal Vector2 DepthRange;
	internal bool IsValid;
}
