using System.Net;
using System.Net.Http.Headers;
using FinancialCopilot.Application.FinancialData.Providers;

namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

public sealed class NadpcoApiAuthHandler(INadpcoApiTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (IsAnonymousCompanyCatalogRequest(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var retryRequest = await CloneAsync(request, cancellationToken);
        var token = await tokenProvider.GetTokenAsync(forceRefresh: false, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            retryRequest.Dispose();
            return response;
        }

        response.Dispose();
        tokenProvider.Invalidate();

        var refreshedToken = await tokenProvider.GetTokenAsync(forceRefresh: true, cancellationToken);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshedToken);
        var retryResponse = await base.SendAsync(retryRequest, cancellationToken);

        if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            retryResponse.Dispose();
            throw new FinancialProviderException(
                FinancialProviderErrorCode.Unauthorized,
                "NADPCO re-authentication failed after 401 response.");
        }

        return retryResponse;
    }

    private static bool IsAnonymousCompanyCatalogRequest(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Get)
        {
            return false;
        }

        var path = request.RequestUri?.IsAbsoluteUri == true
            ? request.RequestUri.AbsolutePath
            : request.RequestUri?.OriginalString;

        return path is not null &&
            path.TrimStart('/').Equals(
                "api/v3/BaseInfo/Companies",
                StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var content = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(content);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
