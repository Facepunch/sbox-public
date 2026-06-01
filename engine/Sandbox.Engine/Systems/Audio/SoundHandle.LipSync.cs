using NativeEngine;
using System.Collections.Concurrent;

namespace Sandbox;

public partial class SoundHandle
{
	/// <summary>
	/// Access lipsync processing.
	/// </summary>
	public LipSyncAccessor LipSync { get; private init; } = new();

	public class LipSyncAccessor
	{
		/// <summary>
		/// A list of 15 lipsync viseme weights. Requires <see cref="Enabled"/> to be true.
		/// </summary>
		public IReadOnlyList<float> Visemes => _visemes is null ?
			Array.Empty<float>() : Array.AsReadOnly( _visemes );

		/// <summary>
		/// Count from start of recognition.
		/// </summary>
		public int FrameNumber { get; private set; }

		/// <summary>
		/// Frame delay in milliseconds.
		/// </summary>
		public int FrameDelay { get; private set; }

		/// <summary>
		/// Laughter score for the current audio frame.
		/// </summary>
		public float LaughterScore { get; private set; }

		/// <summary>
		/// Enables lipsync processing.
		/// </summary>
		public bool Enabled
		{
			get => _enabled;
			set
			{
				if ( _enabled == value )
					return;

				if ( value )
				{
					EnableLipSync();
				}
				else
				{
					DisableLipSync();
				}
			}
		}

		// Volatile so the mix thread's ProcessLipSync guard fires without a lock.
		volatile bool _enabled;
		private uint _context;
		private float[] _visemes;

		// Contexts waiting to be destroyed at the start of the next MixOneBuffer().
		static readonly ConcurrentQueue<uint> _destructionQueue = new();

		/// <summary>
		/// Drain OVR contexts queued by DisableLipSync(). Called by MixingThread at the start
		/// of each MixOneBuffer(), guaranteeing no context is destroyed while ProcessLipSync uses it.
		/// </summary>
		internal static void DrainDestructionQueue()
		{
			while ( _destructionQueue.TryDequeue( out var ctx ) )
				OVRLipSyncGlobal.ovrLipSync_DestroyContext( ctx );
		}

		internal LipSyncAccessor()
		{
		}

		private void EnableLipSync()
		{
			if ( _enabled ) return;

			// Create resources first, then set _enabled = true (volatile release).
			// The mix thread sees _enabled = true only after _context and _visemes are ready.
			OVRLipSyncGlobal.ovrLipSync_CreateContextEx(
				out _context,
				OVRLipSync.ContextProvider.Enhanced_with_Laughter,
				VoiceManager.SampleRate,
				true );

			_visemes = new float[(int)OVRLipSync.Viseme.Count];
			_enabled = true; // volatile write
		}

		internal void DisableLipSync()
		{
			if ( !_enabled ) return;

			// Set _enabled = false (volatile write) FIRST so the mix thread's ProcessLipSync
			// guard fires before any context is destroyed.
			_enabled = false;

			// Queue the context for destruction at the start of the next MixOneBuffer().
			// We never destroy it here because the mix thread might still be in ProcessLipSync
			// having read _enabled = true just before we set it to false.
			_destructionQueue.Enqueue( _context );
			_context = 0;

			// Leave _visemes; ProcessLipSync's !_enabled guard prevents it from touching
			// the array, and it will be replaced on re-enable or collected by GC.
			FrameNumber = 0;
			FrameDelay = 0;
			LaughterScore = 0;
		}

		internal unsafe void ProcessLipSync( Audio.MixBuffer buffer )
		{
			// Volatile read: if DisableLipSync() has set _enabled = false, we bail here
			// before touching _context or _visemes, both of which may have been cleared.
			if ( !_enabled )
				return;

			if ( buffer is null )
				return;

			if ( buffer._native.IsNull )
				return;

			var pData = buffer._native.GetDataPointer();
			if ( pData == IntPtr.Zero )
				return;

			if ( _visemes is null )
				return;

			fixed ( float* pVisemes = _visemes )
			{
				var frame = new OVRLipSync.Frame
				{
					Visemes = (IntPtr)pVisemes,
					VisemesLength = (uint)_visemes.Length,
				};

				var r = OVRLipSyncGlobal.ovrLipSync_ProcessFrameEx(
					_context,
					pData,
					Audio.AudioEngine.MixBufferSize,
					OVRLipSync.AudioDataType.F32_Mono,
					ref frame );

				if ( r == OVRLipSync.Result.Success )
				{
					FrameNumber = frame.FrameNumber;
					FrameDelay = frame.FrameDelay;
					LaughterScore = frame.LaughterScore;
				}
				else
				{
					Log.Warning( r );
				}
			}
		}
	}
}

