#if WINDOWS
using System;
using System.Speech.Recognition;

namespace Sandbox.Speech;

/// <summary>
/// A result from speech recognition.
/// </summary>
public struct SpeechRecognitionResult
{
	/// <summary>
	/// From 0-1 how confident are we that this is the correct result?
	/// </summary>
	public float Confidence { get; init; }

	/// <summary>
	/// The text result from speech recognition.
	/// </summary>
	public string Text { get; init; }

	/// <summary>
	/// Did we successfully find a match?
	/// </summary>
	public bool Success { get; init; }
}

public static class Recognition
{
	/// <summary>
	/// Called when we have a result from speech recognition.
	/// </summary>
	/// <param name="result"></param>
	public delegate void OnSpeechResult( SpeechRecognitionResult result );

	/// <summary>
	/// Whether or not we are currently listening for speech.
	/// </summary>
	public static bool IsListening { get; private set; }

	/// <summary>
	/// Whether or not speech recognition is supported and a language is available.
	/// </summary>
	public static bool IsSupported => GetRecognizerInfo() != null;

	private static SpeechRecognitionEngine Engine { get; set; }
	private static RecognizerInfo RecognizerInfo { get; set; }

	/// <summary>
	/// Start listening for speech to recognize as text. When speech has been recognized the callback
	/// will be invoked, the callback will also be invoked if recognition fails.
	/// </summary>
	/// <param name="callback">
	/// A callback that will be invoked when recognition has finished.
	/// </param>
	/// <param name="choices">
	/// An array of possible choices. If specified, the closest match will be chosen and passed to
	/// the callback.
	/// </param>
	public static void Start( OnSpeechResult callback, IEnumerable<string> choices = null )
	{
		Stop();

		var ri = GetRecognizerInfo();

		if ( ri == null )
			throw new Exception( "Unable to find any installed languages" );

		Grammar grammar;

		if ( choices != null && choices.Any() )
		{
			var builder = new GrammarBuilder( new Choices( choices.ToArray() ) );
			builder.Culture = ri.Culture;
			grammar = new Grammar( builder );
		}
		else
		{
			grammar = new DictationGrammar();
		}

		Engine = new SpeechRecognitionEngine( ri );
		Engine.LoadGrammarAsync( grammar );
		Engine.EndSilenceTimeout = TimeSpan.FromSeconds( 1f );
		Engine.InitialSilenceTimeout = TimeSpan.FromSeconds( 3f );
		Engine.SetInputToDefaultAudioDevice();

		Engine.LoadGrammarCompleted += ( sender, e ) =>
		{
			Engine.RecognizeAsync( RecognizeMode.Multiple );
		};

		Engine.RecognizeCompleted += ( sender, e ) =>
		{
			Stop();
			throw e.Error;
		};

		Engine.SpeechRecognized += ( sender, e ) =>
		{
			Stop();

			var result = new SpeechRecognitionResult
			{
				Confidence = e.Result.Confidence,
				Success = true,
				Text = e.Result.Text
			};

			callback?.Invoke( result );
		};

		Engine.SpeechRecognitionRejected += ( sender, e ) =>
		{
			Stop();

			var result = new SpeechRecognitionResult
			{
				Success = false,
				Text = string.Empty
			};

			callback?.Invoke( result );
		};

		IsListening = true;
	}

	/// <summary>
	/// Stop any active listening for speech.
	/// </summary>
	public static void Stop()
	{
		if ( Engine == null ) return;

		IsListening = false;

		Engine.RecognizeAsyncCancel();
		Engine.Dispose();
		Engine = null;
	}

	private static RecognizerInfo GetRecognizerInfo()
	{
		if ( RecognizerInfo != null )
			return RecognizerInfo;

		var recognizerList = SpeechRecognitionEngine.InstalledRecognizers();

		foreach ( var ri in recognizerList )
		{
			if ( ri.Culture.TwoLetterISOLanguageName.Equals( "en" ) )
			{
				RecognizerInfo = ri;
				break;
			}
		}

		if ( RecognizerInfo == null )
		{
			RecognizerInfo = recognizerList.FirstOrDefault();
		}

		return RecognizerInfo;
	}

	internal static void Reset()
	{
		RecognizerInfo = null;
		IsListening = false;
	}
}
#endif
