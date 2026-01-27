namespace Sandbox;

internal static partial class Rules
{
	internal static string[] Async = new[]
	{
		"System.Private.CoreLib/System.IAsyncResult",
		"System.Private.CoreLib/System.AsyncCallback",
		"System.Private.CoreLib/System.Threading.Tasks.Task",
		"System.Private.CoreLib/System.Threading.Tasks.Task.Yield()",
		"System.Private.CoreLib/System.Threading.Tasks.Task`1",
		"System.Private.CoreLib/System.Threading.Tasks.Task`1.*",
		"System.Private.CoreLib/System.Threading.Tasks.ValueTask*",
		"System.Private.CoreLib/System.Threading.Tasks.TaskCanceledException*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.TaskAwaiter*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.AsyncTaskMethodBuilder*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.YieldAwaitable*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.IAsyncStateMachine",
		"System.Private.CoreLib/System.Runtime.CompilerServices.AsyncVoidMethodBuilder*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.ValueTaskAwaiter*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.NullableAttribute*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.RefSafetyRulesAttribute*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.NullableContextAttribute*",

		"System.Private.CoreLib/System.Threading.Tasks.Task.Delay*",
		"System.Private.CoreLib/System.Threading.Tasks.Task.FromCanceled*",
		"System.Private.CoreLib/System.Threading.Tasks.Task.FromException*",
		"System.Private.CoreLib/System.Threading.Tasks.Task.FromResult*",
		"System.Private.CoreLib/System.Threading.Tasks.Task.GetAwaiter*",
		"System.Private.CoreLib/System.Threading.Tasks.Task.get_IsCanceled()",
		"System.Private.CoreLib/System.Threading.Tasks.Task.get_IsFaulted()",
		"System.Private.CoreLib/System.Threading.Tasks.Task.get_Exception()",
		"System.Private.CoreLib/System.Threading.Tasks.Task.Wait()",
		"System.Private.CoreLib/System.Threading.Tasks.Task.RunSynchronously()",

		"System.Private.CoreLib/System.Threading.Tasks.Task.ContinueWith*",
		"System.Private.CoreLib/System.Threading.Tasks.Task.TaskContinuationOptions",

		"System.Private.CoreLib/System.Threading.Tasks.TaskCompletionSource",
		"System.Private.CoreLib/System.Threading.Tasks.TaskCompletionSource.*",
		"System.Private.CoreLib/System.Threading.Tasks.TaskCompletionSource`1",
		"System.Private.CoreLib/System.Threading.Tasks.TaskCompletionSource`1.*",

		"System.Private.CoreLib/System.Threading.Tasks.Task.get_CompletedTask()",
		"System.Private.CoreLib/System.Threading.Tasks.Task.get_IsCompleted()",
		"System.Private.CoreLib/System.Threading.Tasks.Task.get_IsCompletedSuccessfully()",
		"System.Private.CoreLib/System.Threading.Tasks.Task.get_Status()",

		"System.Private.CoreLib/System.Threading.Tasks.TaskExtensions.Unwrap<TResult>( System.Threading.Tasks.Task`1<System.Threading.Tasks.Task`1<TResult>> )", // https://github.com/Facepunch/sbox-public/issues/4905

		"System.Private.CoreLib/System.Threading.Tasks.TaskStatus",
	};
}
