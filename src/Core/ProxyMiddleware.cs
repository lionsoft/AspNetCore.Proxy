using System;
using System.Threading.Tasks;
using AspNetCore.Proxy.Builders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace AspNetCore.Proxy
{
    class ProxyMiddleware
    {
        private readonly ILogger _logger;
        private readonly RequestDelegate _next;
        private Proxies _proxies;

        public ProxyMiddleware(RequestDelegate next, ILoggerFactory loggerFactory, IChangeToken changeToken, Action<IProxiesBuilder> builderAction)
        {
            _next = next;
            _logger = loggerFactory.CreateLogger<ProxyMiddleware>();
            changeToken.RegisterChangeCallback(_ => RebuildProxies(builderAction), null);
            RebuildProxies(builderAction);
        }

        private void RebuildProxies(Action<IProxiesBuilder> builderAction)
        {
            var proxiesBuilder = ProxiesBuilder.Instance;
            builderAction(proxiesBuilder);
            _proxies = proxiesBuilder.Build();
        }

        public async Task Invoke(HttpContext context)
        {
            foreach(var proxy in _proxies)
            {
                var isMath = context.Request.Path.StartsWithSegments(proxy.RouteWithoutRest.TrimEnd('/'));
                if (isMath)
                {
                    var rest = proxy.GetRest(context.Request.Path);
                    if (rest != null)
                    {
                        var routingFeature = (IRoutingFeature)(context.Features[typeof(IRoutingFeature)] ??= new RoutingFeature { RouteData = new RouteData() });
                        routingFeature.RouteData.Values["rest"] = rest;
                    }
                    var proxyUri = await context.TryExecuteProxyOperationAsync(proxy);
                    if (proxyUri != null)
                    {
                        _logger.LogDebug($"[PROXY] {context.Request.Path} -> {proxyUri}");
                        return;
                    }
                }
            }
            await _next.Invoke(context);
        }
    }
}
