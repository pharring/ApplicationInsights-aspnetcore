﻿namespace Microsoft.ApplicationInsights.AspNetCore.Tests.Helpers
{
    using Microsoft.ApplicationInsights.AspNetCore.DiagnosticListeners;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Primitives;
    using System;

    public static class HttpContextAccessorHelper
    {
        public static HttpContextAccessor CreateHttpContextAccessor(RequestTelemetry requestTelemetry = null, ActionContext actionContext = null, string sourceInstrumentationKey = null)
        {
            var services = new ServiceCollection();

            var request = new DefaultHttpContext().Request;
            request.Method = "GET";
            request.Path = new PathString("/Test");
            if (sourceInstrumentationKey != null)
            {
                request.Headers.Add(RequestResponseHeaders.SourceInstrumentationKeyHeader, new StringValues(sourceInstrumentationKey));
            }

            var contextAccessor = new HttpContextAccessor { HttpContext = request.HttpContext };

            services.AddSingleton<IHttpContextAccessor>(contextAccessor);

            if (actionContext != null)
            {
                var si = new ActionContextAccessor();
                si.ActionContext = actionContext;
                services.AddSingleton<IActionContextAccessor>(si);
            }

            if (requestTelemetry != null)
            {
                request.HttpContext.Features.Set(requestTelemetry);
            }

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            contextAccessor.HttpContext.RequestServices = serviceProvider;

            return contextAccessor;
        }

        public static HttpContextAccessor CreateHttpContextAccessorWithoutRequest(HttpContextStub httpContextStub, RequestTelemetry requestTelemetry = null)
        {
            var services = new ServiceCollection();

            var contextAccessor = new HttpContextAccessor { HttpContext = httpContextStub };

            services.AddSingleton<IHttpContextAccessor>(contextAccessor);

            if (requestTelemetry != null)
            {
                services.AddSingleton<RequestTelemetry>(requestTelemetry);
            }

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            contextAccessor.HttpContext.RequestServices = serviceProvider;

            return contextAccessor;
        }
    }
}
