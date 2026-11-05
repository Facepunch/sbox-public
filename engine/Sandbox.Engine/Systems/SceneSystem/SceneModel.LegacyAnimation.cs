using System.Runtime.InteropServices;

namespace Sandbox;

#pragma warning disable CS0618

public sealed partial class SceneModel
{
	private AnimationGraph _legacyAnimationGraph;
	private DelegateFunctionPointer legacyAnimGraphChangedCallback;

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	internal delegate void LegacyAnimGraphChangedCallback( IntPtr p );

	private void RegisterLegacyAnimationCallback()
	{
		if ( legacyAnimGraphChangedCallback == DelegateFunctionPointer.Null )
			legacyAnimGraphChangedCallback = DelegateFunctionPointer.Get<LegacyAnimGraphChangedCallback>( OnLegacyAnimGraphChanged );

		animNative.SetAnimGraphChangedCallback( legacyAnimGraphChangedCallback );
	}

	private void OnLegacyAnimGraphChanged( IntPtr p )
	{
		_legacyAnimationGraph = AnimationGraph.FromNative( animNative.GetAnimGraph() );
	}

	/// <summary>Overrides the legacy animation graph this scene model uses.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Graph instead." )]
	public void SetAnimGraph( string name ) => animNative.SetAnimGraph( name );

	[Hide]
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Graph instead." )]
	public AnimationGraph AnimationGraph
	{
		get => _legacyAnimationGraph;
		set => animNative.SetAnimGraph( value?.native ?? default );
	}

	SceneObjectDirectPlayback _directPlayback;

	/// <summary>
	/// Access this sceneobject's legacy direct playback. Direct playback controls the direct playback node in a legacy animgraph
	/// to play sequences directly in code.
	/// </summary>
	[Obsolete( "AnimGraph1 direct playback is obsolete. Use CurrentSequence instead." )]
	public AnimGraphDirectPlayback DirectPlayback
	{
		get
		{
			// Create on first access
			_directPlayback ??= new SceneObjectDirectPlayback( this );

			return _directPlayback;
		}
	}
}
