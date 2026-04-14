
namespace OllamaToolCallingExample;

/// <summary>HTTP handler that throws with response body when status is not success (e.g. 500).</summary>
sealed class DetailedHttpFailureHandler : DelegatingHandler
{
	public DetailedHttpFailureHandler() : base(new HttpClientHandler()) { }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var response = await base.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new HttpRequestException(
				$"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}). Response body: {body}");
		}
		return response;
	}
}
