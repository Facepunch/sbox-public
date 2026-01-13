using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Twitch
{
	internal partial class TwitchAPI
	{
		internal const string ApiUrl = "https://api.twitch.tv/helix";
		internal const string ClientId = "lyo7ge5md65toi0f3bjpkbn4u8hwol";

		internal HttpClient Http
		{
			get
			{
				var token = Engine.Streamer.ServiceToken;
				var client = new HttpClient();
				client.DefaultRequestHeaders.Add( "Client-ID", ClientId );
				client.DefaultRequestHeaders.Add( "Authorization", $"BeaArer {token.Token}" );

				return client;
			}
		}

		internal Task<T> Get<T>( string request )
		{
			var url = $"{ApiUrl}{request}";

			try
			{
				return Http.GetFromJsonAsync<T>( url );
			}
			catch
			{
				return default;
			}
		}

		internal async Task<HttpResponseMessage> Post( string request, string json )
		{
			var url = $"{ApiUrl}{request}";

			try
			{
				using var content = new StringContent( json, Encoding.UTF8, "application/json" );
				return await Http.PostAsync( url, content );
			}
			catch
			{
				return default;
			}
		}

		internal async Task<HttpResponseMessage> Put( string request, string json )
		{
			var url = $"{ApiUrl}{request}";

			try
			{
				using var content = new StringContent( json, Encoding.UTF8, "application/json" );
				return await Http.PutAsync( url, content );
			}
			catch
			{
				return default;
			}
		}

		internal async Task<HttpResponseMessage> Patch( string request, string json )
		{
			var url = $"{ApiUrl}{request}";

			try
			{
				using var content = new StringContent( json, Encoding.UTF8, "application/json" );
				return await Http.PatchAsync( url, content );
			}
			catch
			{
				return default;
			}
		}
	}
}

