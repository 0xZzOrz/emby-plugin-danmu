using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugin.Danmu.Core.Http
{
    public class HttpRetryMessageHandler : DelegatingHandler
    {
        public HttpRetryMessageHandler(HttpMessageHandler innerHandler) : base(innerHandler)
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            base.SendAsync(request, cancellationToken);
    }
}
