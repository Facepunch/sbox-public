using NativeEngine;

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

		private bool _enabled;
		private uint _context;
		private float[] _visemes;

		internal LipSyncAccessor()
		{
		}

		private void EnableLipSync()
		{
			if ( _enabled )
				return;

			OVRLipSyncGlobal.ovrLipSync_CreateContextEx(
				out _context,
				OVRLipSync.ContextProvider.Enhanced_with_Laughter,
				VoiceManager.SampleRate,
				true );

			_visemes = new float[(int)OVRLipSync.Viseme.Count];
			_enabled = true;
		}

		internal void DisableLipSync()
		{
			if ( !_enabled )
				return;

			OVRLipSyncGlobal.ovrLipSync_DestroyContext( _context );

			FrameNumber = 0;
			FrameDelay = 0;
			LaughterScore = 0;

			_visemes = null;
			_enabled = false;
		}

		internal unsafe void ProcessLipSync( Audio.MixBuffer buffer )
		{
			if ( !Enabled )
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
