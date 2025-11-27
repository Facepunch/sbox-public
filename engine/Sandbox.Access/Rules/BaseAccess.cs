namespace Sandbox;

internal static partial class Rules
{
	internal static string[] BaseAccess = new[]
	{
		"Sandbox.Engine/*",
		"Sandbox.System/*",
		"Sandbox.Filesystem/*",
		"Sandbox.Bind/*",
		"Sandbox.Reflection/*",
		"Sandbox.Mounting/*",
		"Microsoft.AspNetCore.Components/*", // this is our fake razor assembly

		"System.Private.CoreLib/System.IDisposable*",
		"System.Private.CoreLib/System.IAsyncDisposable*",

		"System.Private.CoreLib/System.Collections.*",
		"System.Collections/System.Collections.*",
		"System.Collections.Immutable/System.Collections.Immutable.*",
		"System.Collections.Immutable/System.Linq.ImmutableArrayExtensions.*",
		"System.ObjectModel/System.Collections.ObjectModel.*",
		"System.ObjectModel/System.Collections.Specialized.*",

		"System.Private.CoreLib/System.Math*",
		"System.Private.CoreLib/System.Globalization*",

		"System.Linq/*",

		"System.Private.CoreLib/System.IEquatable*",
		"System.Private.CoreLib/System.IComparable*",
		"System.Private.CoreLib/System.Comparison*",
		"System.Private.CoreLib/System.IO.BinaryWriter*",
		"System.Private.CoreLib/System.IO.BinaryReader*",
		"System.Private.CoreLib/System.Activator.CreateInstance<T>()",
		"System.Private.CoreLib/System.Guid*",
		"System.Private.CoreLib/System.IO.MemoryStream*",
		"System.Private.CoreLib/System.IO.Stream",
		"System.Private.CoreLib/System.IO.Stream.*",
		"System.Private.CoreLib/System.Threading.CancellationToken*",
		"System.Private.CoreLib/System.Threading.CancellationTokenSource*",
		"System.Private.CoreLib/System.Enum*",
		"System.Private.CoreLib/System.Environment.get_CurrentManagedThreadId()",
		"System.Private.CoreLib/System.Environment.get_StackTrace()",
		"System.Private.CoreLib/System.DateTime*",
		"System.Private.CoreLib/System.DayOfWeek*",
		"System.Private.CoreLib/System.HashCode*",
		"System.Private.CoreLib/System.StringSplitOptions*",
		"System.Private.CoreLib/System.ValueTuple*",
		"System.Private.CoreLib/System.Tuple*",
		"System.Private.CoreLib/System.Random*",
		"System.Private.CoreLib/System.MemoryExtensions*",
		"System.Private.CoreLib/System.IFormatProvider",
		"System.Private.CoreLib/System.Version*",
		"System.Private.CoreLib/System.MidpointRounding*",
		"System.Private.CoreLib/System.Lazy*",

		"System.Private.CoreLib/System.Threading.Interlocked*",
		"System.Private.CoreLib/System.Threading.Monitor.Enter(*",
		"System.Private.CoreLib/System.Threading.Monitor.Exit(*",

		"System.Private.CoreLib/System.Threading.SemaphoreSlim*",
		"System.Private.CoreLib/System.Threading.SemaphoreFullException*",

		// Do not whitelist constructors or CreateDelegate
		"System.Private.CoreLib/System.Delegate",
		"System.Private.CoreLib/System.Delegate.Combine(*",
		"System.Private.CoreLib/System.Delegate.Remove(*",
		"System.Private.CoreLib/System.Delegate.GetInvocationList()",
		"System.Private.CoreLib/System.Delegate.DynamicInvoke( System.Object[] )",
		"System.Private.CoreLib/System.Delegate.op_Equality( System.Delegate, System.Delegate )",
		"System.Private.CoreLib/System.Delegate.op_Inequality( System.Delegate, System.Delegate )",

		"System.Private.CoreLib/System.GC.SuppressFinalize(*",

		"System.Private.CoreLib/System.RuntimeFieldHandle",
		"System.Private.CoreLib/System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray( System.Array, System.RuntimeFieldHandle )",
		"System.Private.CoreLib/System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<T>( System.RuntimeFieldHandle )",
		"System.Private.CoreLib/System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack()",

		"System.Private.CoreLib/System.IO.Path.*",
		"!System.Private.CoreLib/System.IO.Path.Exists(*",
		"!System.Private.CoreLib/System.IO.Path.GetFullPath(*",
		"!System.Private.CoreLib/System.IO.Path.GetTempFileName(*",
		"!System.Private.CoreLib/System.IO.Path.GetTempPath(*",

		"System.Private.CoreLib/System.IO.FileMode",
		"System.Private.CoreLib/System.IO.SeekOrigin",

		"System.Private.CoreLib/System.Text.*",
		"System.Text.RegularExpressions/System.Text.RegularExpressions.*",

		"System.Private.CoreLib/System.Buffers.ArrayPool*",

		"System.Private.CoreLib/System.Convert.ToInt32*",

		"System.Private.CoreLib/System.TimeSpan*",

		"System.Private.CoreLib/System.RuntimeTypeHandle",


		"System.Private.CoreLib/System.StringComparison",


		"System.Private.CoreLib/System.Attribute*",
		"System.Private.CoreLib/System.AttributeUsageAttribute*",
		"System.Private.CoreLib/System.FlagsAttribute*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.ITuple*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.TupleElementNamesAttribute*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.IsReadOnlyAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.IsExternalInit",
		"System.Private.CoreLib/System.Runtime.CompilerServices.ExtensionAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.IteratorStateMachineAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.AsyncStateMachineAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.CompilerGeneratedAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.PreserveBaseOverridesAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.IsByRefLikeAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.RequiredMemberAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.RequiresLocationAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.CallerMemberNameAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.CallerFilePathAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.CallerLineNumberAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.IntrinsicAttribute",
		"System.Private.CoreLib/System.Runtime.InteropServices.InAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.DefaultInterpolatedStringHandler*",
		"System.Private.CoreLib/System.ComponentModel.EditorBrowsableAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonPropertyNameAttribute*",
		"System.Private.CoreLib/System.ObsoleteAttribute*",
		"System.Private.CoreLib/System.Diagnostics.DebuggerDisplayAttribute*",
		"System.Private.CoreLib/System.Diagnostics.ConditionalAttribute*",
		"System.Private.CoreLib/System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute",
		"System.Private.CoreLib/System.ThreadStaticAttribute*",

		"System.Private.CoreLib/System.Span*",
		"System.Private.CoreLib/System.ReadOnlySpan*",

		"System.Private.CoreLib/System.Reflection.DefaultMemberAttribute*",

		"System.Private.CoreLib/System.Index*",
		"System.Private.CoreLib/System.Range*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.RuntimeHelpers.GetSubArray*",

		"System.Text.Json/System.Text.Json.JsonNamingPolicy*",
		"System.Text.Json/System.Text.Json.JsonDocument*",
		"System.Text.Json/System.Text.Json.JsonDocumentOptions*",
		"System.Text.Json/System.Text.Json.JsonElement*",
		"System.Text.Json/System.Text.Json.JsonProperty*",
		"System.Text.Json/System.Text.Json.JsonValueKind*",
		"System.Text.Json/System.Text.Json.JsonCommentHandling*",
		"System.Text.Json/System.Text.Json.JsonEncodedText*",
		"System.Text.Json/System.Text.Json.JsonException*",
		"System.Text.Json/System.Text.Json.JsonTokenType*",
		"System.Text.Json/System.Text.Json.JsonReaderOptions*",
		"System.Text.Json/System.Text.Json.Utf8JsonReader*",
		"System.Text.Json/System.Text.Json.JsonSerializer*",
		"System.Text.Json/System.Text.Json.JsonSerializerDefaults*",
		"System.Text.Json/System.Text.Json.JsonSerializerOptions*",
		"System.Text.Json/System.Text.Json.JsonWriterOptions*",
		"System.Text.Json/System.Text.Json.Utf8JsonWriter*",
		"System.Text.Json/System.Text.Json.Nodes.JsonArray*",
		"System.Text.Json/System.Text.Json.Nodes.JsonNode*",
		"System.Text.Json/System.Text.Json.Nodes.JsonNodeOptions*",
		"System.Text.Json/System.Text.Json.Nodes.JsonObject*",
		"System.Text.Json/System.Text.Json.Nodes.JsonValue*",
		"System.Text.Json/System.Text.Json.Serialization.JsonAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonIgnoreCondition*",
		"System.Text.Json/System.Text.Json.Serialization.JsonKnownNamingPolicy*",
		"System.Text.Json/System.Text.Json.Serialization.JsonNumberHandling*",
		"System.Text.Json/System.Text.Json.Serialization.JsonSerializableAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonConstructorAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonConverterAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonDerivedTypeAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonExtensionDataAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonIgnoreAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonIncludeAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonNumberHandlingAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonPolymorphicAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonPropertyNameAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonRequiredAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.JsonPropertyOrderAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.IJsonOnDeserialized*",
		"System.Text.Json/System.Text.Json.Serialization.IJsonOnDeserializing*",
		"System.Text.Json/System.Text.Json.Serialization.IJsonOnSerialized*",
		"System.Text.Json/System.Text.Json.Serialization.IJsonOnSerializing*",
		"System.Text.Json/System.Text.Json.Serialization.JsonUnknownDerivedTypeHandling*",
		"System.Text.Json/System.Text.Json.Serialization.JsonConverterFactory*",
		"System.Text.Json/System.Text.Json.Serialization.JsonConverter*",
		"System.Text.Json/System.Text.Json.Serialization.JsonStringEnumConverter*",
		"System.Text.Json/System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute*",
		"System.Text.Json/System.Text.Json.Serialization.ReferenceHandler*",
		"System.Text.Json/System.Text.Json.Serialization.ReferenceResolver*",

		"System.Private.CoreLib/System.BitConverter*",
		"System.Private.CoreLib/System.Convert*",

		"System.IO.Compression/System.IO.Compression.DeflateStream*",
		"System.IO.Compression/System.IO.Compression.GZipStream*",
		"System.IO.Compression/System.IO.Compression.CompressionMode",
		"System.IO.Compression/System.IO.Compression.CompressionLevel",
		"System.IO.Compression/System.IO.Compression.ZipArchive*",
		"System.IO.Compression/System.IO.Compression.ZipArchiveEntry*",
		"System.IO.Compression/System.IO.Compression.ZipArchiveMode",

		"System.Private.CoreLib/System.Net.WebUtility*",
		"System.Private.Uri/System.Uri*",

		"System.Threading.Channels/System.Threading.Channels.*",
		"System.ComponentModel.Primitives/System.ComponentModel.*",

		// avoid DataAnnotations.CompareAttribute - it has magic sauce that allows a property access
		"System.ComponentModel.Annotations/System.ComponentModel.DataAnnotations.DisplayAttribute",
		"System.ComponentModel.Annotations/System.ComponentModel.DataAnnotations.ValidationAttribute",
		"System.ComponentModel.Annotations/System.ComponentModel.DataAnnotations.RequiredAttribute",
		"System.ComponentModel.Annotations/System.ComponentModel.DataAnnotations.RegularExpressionAttribute",
		"System.ComponentModel.Annotations/System.ComponentModel.DataAnnotations.RangeAttribute",
		"System.ComponentModel.Annotations/System.ComponentModel.DataAnnotations.MaxLengthAttribute",

		"System.Private.CoreLib/System.EventArgs*",
		"System.Private.CoreLib/System.EventHandler*",


		"System.Web.HttpUtility/System.Web.HttpUtility*",
		"System.Collections.Specialized/System.Collections.Specialized.*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.FormattableStringFactory*",
		"System.Private.CoreLib/System.FormattableString*",

		"System.Private.CoreLib/System.IO.StreamReader",
		"System.Private.CoreLib/System.IO.StreamReader..ctor( System.IO.Stream*", // explicitly not the string constructor
		"System.Private.CoreLib/System.IO.StreamReader.Close()",
		"System.Private.CoreLib/System.IO.StreamReader.Peek*",
		"System.Private.CoreLib/System.IO.StreamReader.Read*",
		"System.Private.CoreLib/System.IO.StreamReader.get_*",

		"System.Private.CoreLib/System.IO.TextReader*",

		"System.Private.CoreLib/System.IO.TextWriter*",

		"System.Private.CoreLib/System.IO.StringWriter*",

		"System.Private.CoreLib/System.Buffers.Binary.BinaryPrimitives*",

		"System.Private.CoreLib/System.Buffer.BlockCopy( System.Array, System.Int32, System.Array, System.Int32, System.Int32 )",
		"System.Private.CoreLib/System.Buffer.ByteLength( System.Array )",
		"System.Private.CoreLib/System.Buffer.GetByte( System.Array, System.Int32 )",
		"System.Private.CoreLib/System.Buffer.SetByte( System.Array, System.Int32, System.Byte )",

		"System.Private.CoreLib/System.WeakReference*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.ConditionalWeakTable*",

		"System.Private.CoreLib/System.ValueType*",
		"System.Private.CoreLib/System.IConvertible*",
		"System.Private.CoreLib/System.IParsable*",

		"System.Private.CoreLib/System.TimeZoneInfo*",

		// Being a bit selective since there's some per machine stuff dotted around, this is plenty
		"System.Security.Cryptography/System.Security.Cryptography.HashAlgorithm*",
		"System.Security.Cryptography/System.Security.Cryptography.MD5*",
		"System.Security.Cryptography/System.Security.Cryptography.SHA1*",
		"System.Security.Cryptography/System.Security.Cryptography.SHA256*",
		"System.Security.Cryptography/System.Security.Cryptography.SHA512*",

		// Types required for HTTP usage
		"System.Net.Http/System.Net.Http.HttpRequestException*",
		"System.Net.Http/System.Net.Http.HttpResponseMessage*",
		"System.Net.Http/System.Net.Http.HttpContent*",
		"System.Net.Http/System.Net.Http.ByteArrayContent*",
		"System.Net.Http/System.Net.Http.StringContent*",
		"System.Net.Http/System.Net.Http.FormUrlEncodedContent*",
		"System.Net.Http/System.Net.Http.StreamContent*",
		"System.Net.Http/System.Net.Http.MultipartContent*",
		"System.Net.Http/System.Net.Http.Headers.HttpHeaders*",
		"System.Net.Http/System.Net.Http.Headers.HttpContentHeaders*",
		"System.Net.Http/System.Net.Http.Headers.HttpResponseHeaders*",
		"System.Net.Http.Json/System.Net.Http.Json.JsonContent*",
		"System.Net.Http.Json/System.Net.Http.Json.HttpContentJsonExtensions.*",
		"System.Net.Primitives/System.Net.HttpStatusCode",

		"System.Collections.Concurrent/System.Collections.Concurrent.ConcurrentBag*",
		"System.Collections.Concurrent/System.Collections.Concurrent.ConcurrentDictionary*",
		"System.Collections.Concurrent/System.Collections.Concurrent.BlockingCollection*",

		"System.ObjectModel/System.ComponentModel.INotifyPropertyChanged*",
		"System.ObjectModel/System.ComponentModel.PropertyChangedEventArgs*",
		"System.ObjectModel/System.ComponentModel.PropertyChangedEventHandler*",

		// These are metadata attributes that get automatically added by the compiler
		// They do nothing at runtime
		"System.Private.CoreLib/System.Reflection.AssemblyFileVersionAttribute",
		"System.Private.CoreLib/System.Reflection.AssemblyMetadataAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
		"System.Private.CoreLib/System.Runtime.Versioning.TargetFrameworkAttribute",
		"System.Private.CoreLib/System.Diagnostics.DebuggableAttribute",
		"System.Private.CoreLib/System.Diagnostics.DebuggableAttribute/DebuggingModes*",

		// Sometimes manually specified in AssemblyInfo.cs - somewhat useful and harmless
		"System.Private.CoreLib/System.Runtime.InteropServices.GuidAttribute",
		"System.Private.CoreLib/System.Runtime.InteropServices.ComVisibleAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InternalsVisibleToAttribute",

		// This is all fine
		"System.Private.CoreLib/System.Diagnostics.CodeAnalysis.*",

		"System.Private.CoreLib/System.Runtime.InteropServices.CollectionsMarshal.AsSpan<T>( System.Collections.Generic.List`1<T> )",
		"System.Private.CoreLib/System.Runtime.InteropServices.CollectionsMarshal.SetCount<T>( System.Collections.Generic.List`1<T>, System.Int32 )",

		"System.Private.CoreLib/System.ParamArrayAttribute",

		"System.Private.CoreLib/System.Base64FormattingOptions*",

		"System.Private.CoreLib/System.Runtime.CompilerServices.ExtensionMarkerAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.ParamCollectionAttribute"
	};
}
