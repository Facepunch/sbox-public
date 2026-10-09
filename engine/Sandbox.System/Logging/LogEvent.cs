
namespace Sandbox
{
	public enum LogLevel
	{
		Trace,
		Info,
		Warn,
		Error
	}

	public struct LogEvent
	{
		[Title( "Log Level" )]
		[Category( "Meta Data" )]
		[ReadOnly]
		public LogLevel Level { get; set; }

		[Category( "Meta Data" )]
		[ReadOnly]
		public string Logger { get; set; }

		[ReadOnly]
		public string Message { get; set; }

		[ReadOnly]
		[SkipHotload]
		public Exception Exception { get; set; }

		[ReadOnly]
		public string HtmlMessage { get; set; }

		[Title( "Stack Trace" )]
		[ReadOnly]
		[Category( "Stack Trace" )]
		public string Stack { get; set; }

		[Category( "Meta Data" )]
		[ReadOnly]
		public DateTime Time { get; set; }

		[ReadOnly]
		[SkipHotload]
		public object[] Arguments { get; set; }

		[ReadOnly]
		public int Repeats { get; set; }

		[ReadOnly]
		public bool IsDiagnostic { get; set; }

		/// <summary>
		/// A copy for sinks that keep events around: reference-type arguments are held through
		/// <see cref="WeakReference"/>, so a stored event never keeps a logged object - and whatever it
		/// references, like a closed scene - alive. Read them back with <see cref="GetArgument"/>.
		/// </summary>
		internal readonly LogEvent WithWeakArguments()
		{
			if ( Arguments is not { Length: > 0 } arguments )
				return this;

			var weak = new object[arguments.Length];
			for ( int i = 0; i < arguments.Length; i++ )
			{
				var argument = arguments[i];
				weak[i] = argument is null || argument is string || argument.GetType().IsValueType ? argument : new WeakReference( argument );
			}

			var copy = this;
			copy.Arguments = weak;
			return copy;
		}

		/// <summary>
		/// The argument at <paramref name="index"/>, unwrapping one held by <see cref="WithWeakArguments"/>.
		/// Null if out of range or already collected.
		/// </summary>
		internal readonly object GetArgument( int index )
		{
			if ( Arguments is null || index < 0 || index >= Arguments.Length )
				return null;

			return Arguments[index] is WeakReference weak ? weak.Target : Arguments[index];
		}
	}
}
