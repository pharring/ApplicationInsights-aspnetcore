﻿namespace Microsoft.ApplicationInsights.AspNetCore.DiagnosticListeners
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http.Headers;
    using System.Text;
    using Extensibility.Implementation.Tracing;
    using Microsoft.ApplicationInsights.AspNetCore.DiagnosticListeners.Implementation;
    using Microsoft.ApplicationInsights.AspNetCore.Extensions;
    using Microsoft.ApplicationInsights.Common;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;
    using Microsoft.ApplicationInsights.Extensibility.W3C;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Primitives;

    /// <summary>
    /// <see cref="IApplicationInsightDiagnosticListener"/> implementation that listens for events specific to AspNetCore hosting layer.
    /// </summary>
    internal class HostingDiagnosticListener : IApplicationInsightDiagnosticListener
    {
        /// <summary>
        /// Determine whether the running AspNetCore Hosting version is 2.0 or higher. This will affect what DiagnosticSource events we receive.
        /// To support AspNetCore 1.0 and 2.0, we listen to both old and new events.
        /// If the running AspNetCore version is 2.0, both old and new events will be sent. In this case, we will ignore the old events.
        /// </summary>
        private readonly bool enableNewDiagnosticEvents;

        private readonly TelemetryClient client;
        private readonly IApplicationIdProvider applicationIdProvider;
        private readonly string sdkVersion = SdkVersionUtils.GetVersion();
        private readonly bool injectResponseHeaders;
        private readonly bool trackExceptions;
        private readonly bool enableW3CHeaders;
        private static readonly ActiveSubsciptionManager SubscriptionManager = new ActiveSubsciptionManager();
        private const string ActivityCreatedByHostingDiagnosticListener = "ActivityCreatedByHostingDiagnosticListener";

        #region fetchers

        // fetch is unique per event and per property
        private readonly PropertyFetcher httpContextFetcherStart = new PropertyFetcher("HttpContext");
        private readonly PropertyFetcher httpContextFetcherStop = new PropertyFetcher("HttpContext");
        private readonly PropertyFetcher httpContextFetcherBeginRequest = new PropertyFetcher("httpContext");
        private readonly PropertyFetcher httpContextFetcherEndRequest = new PropertyFetcher("httpContext");
        private readonly PropertyFetcher httpContextFetcherDiagExceptionUnhandled = new PropertyFetcher("httpContext");
        private readonly PropertyFetcher httpContextFetcherDiagExceptionHandled = new PropertyFetcher("httpContext");
        private readonly PropertyFetcher httpContextFetcherHostingExceptionUnhandled = new PropertyFetcher("httpContext");
        private readonly PropertyFetcher exceptionFetcherDiagExceptionUnhandled = new PropertyFetcher("exception");
        private readonly PropertyFetcher exceptionFetcherDiagExceptionHandled = new PropertyFetcher("exception");
        private readonly PropertyFetcher exceptionFetcherHostingExceptionUnhandled = new PropertyFetcher("exception");

        private readonly PropertyFetcher timestampFetcherBeginRequest = new PropertyFetcher("timestamp");
        private readonly PropertyFetcher timestampFetcherEndRequest = new PropertyFetcher("timestamp");
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="T:HostingDiagnosticListener"/> class.
        /// </summary>
        /// <param name="client"><see cref="TelemetryClient"/> to post traces to.</param>
        /// <param name="applicationIdProvider">Provider for resolving application Id to be used in multiple instruemntation keys scenarios.</param>
        /// <param name="injectResponseHeaders">Flag that indicates that response headers should be injected.</param>
        /// <param name="trackExceptions">Flag that indicates that exceptions should be tracked.</param>
        /// <param name="enableW3CHeaders">Flag that indicates that W3C header parsing should be enabled.</param>
        /// <param name="enableNewDiagnosticEvents">Flag that indicates that new diagnostic events are supported by AspNetCore</param>
        public HostingDiagnosticListener(
            TelemetryClient client,
            IApplicationIdProvider applicationIdProvider,
            bool injectResponseHeaders,
            bool trackExceptions,
            bool enableW3CHeaders,
            bool enableNewDiagnosticEvents = true)
        {
            this.enableNewDiagnosticEvents = enableNewDiagnosticEvents;
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.applicationIdProvider = applicationIdProvider;
            this.injectResponseHeaders = injectResponseHeaders;
            this.trackExceptions = trackExceptions;
            this.enableW3CHeaders = enableW3CHeaders;
        }

        /// <inheritdoc />
        public void OnSubscribe()
        {
            SubscriptionManager.Attach(this);
        }

        /// <inheritdoc/>
        public string ListenerName { get; } = "Microsoft.AspNetCore";

        /// <summary>
        /// Diagnostic event handler method for 'Microsoft.AspNetCore.Hosting.HttpRequestIn.Start' event.
        /// </summary>
        public void OnHttpRequestInStart(HttpContext httpContext)
        {
            if (this.client.IsEnabled())
            {
                // It's possible to host multiple apps (ASP.NET Core or generic hosts) in the same process
                // Each of this apps has it's own HostingDiagnosticListener and corresponding Http listener.
                // We should ignore events for all of them except one
                if (!SubscriptionManager.IsActive(this))
                {
                    AspNetCoreEventSource.Instance.NotActiveListenerNoTracking("Microsoft.AspNetCore.Hosting.HttpRequestIn.Start", Activity.Current?.Id);
                    return;
                }

                if (Activity.Current == null)
                {
                    AspNetCoreEventSource.Instance.LogHostingDiagnosticListenerOnHttpRequestInStartActivityNull();
                    return;
                }

                var currentActivity = Activity.Current;
                var isActivityCreatedFromRequestIdHeader = true;
                string sourceAppId = null;
                string originalParentId = currentActivity.ParentId;

                Activity newActivity = null;
                // W3C
                if (this.enableW3CHeaders)
                {
                    isActivityCreatedFromRequestIdHeader = false;
                    SetW3CContext(httpContext.Request.Headers, currentActivity, out sourceAppId);

                    var parentSpanId = currentActivity.GetParentSpanId();
                    if (parentSpanId != null)
                    {
                        originalParentId = $"|{currentActivity.GetTraceId()}.{parentSpanId}.";
                    }
                }
                
                // x-ms-*
                if (originalParentId == null &&
                         httpContext.Request.Headers.TryGetValue(RequestResponseHeaders.StandardRootIdHeader, out StringValues alternativeRootIdValues) &&
                         alternativeRootIdValues != StringValues.Empty)
                {
                    isActivityCreatedFromRequestIdHeader = false;
                    newActivity = new Activity(ActivityCreatedByHostingDiagnosticListener)
                        .SetParentId(StringUtilities.EnforceMaxLength(alternativeRootIdValues.First(),
                        InjectionGuardConstants.RequestHeaderMaxLength));

                    if (httpContext.Request.Headers.TryGetValue(RequestResponseHeaders.StandardParentIdHeader, out StringValues parentId))
                    {
                        originalParentId = StringUtilities.EnforceMaxLength(parentId.First(),
                            InjectionGuardConstants.RequestHeaderMaxLength);
                    }
                }

                // no headers
                else if (originalParentId == null)
                {
                    // As a first step in supporting W3C protocol in ApplicationInsights,
                    // we want to generate Activity Ids in the W3C compatible format.
                    // While .NET changes to Activity are pending, we want to ensure trace starts with W3C compatible Id
                    // as early as possible, so that everyone has a chance to upgrade and have compatibility with W3C systems once they arrive.
                    // So if there is no current Activity (i.e. there were no Request-Id header in the incoming request), we'll override ParentId on 
                    // the current Activity by the properly formatted one. This workaround should go away
                    // with W3C support on .NET https://github.com/dotnet/corefx/issues/30331

                    newActivity = new Activity(ActivityCreatedByHostingDiagnosticListener);
                    newActivity.SetParentId(StringUtilities.GenerateTraceId());
                    // end of workaround
                }

                if (newActivity != null)
                {
                    newActivity.Start();
                    currentActivity = newActivity;
                }

                var requestTelemetry = InitializeRequestTelemetry(httpContext, currentActivity, isActivityCreatedFromRequestIdHeader, Stopwatch.GetTimestamp());
                if (this.enableW3CHeaders && sourceAppId != null)
                {
                    requestTelemetry.Source = sourceAppId;
                }

                requestTelemetry.Context.Operation.ParentId = originalParentId;
                SetAppIdInResponseHeader(httpContext, requestTelemetry);
            }
        }

        /// <summary>
        /// Diagnostic event handler method for 'Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop' event.
        /// </summary>
        public void OnHttpRequestInStop(HttpContext httpContext)
        {
            EndRequest(httpContext, Stopwatch.GetTimestamp());
        }

        /// <summary>
        /// Diagnostic event handler method for 'Microsoft.AspNetCore.Hosting.BeginRequest' event.
        /// </summary>
        public void OnBeginRequest(HttpContext httpContext, long timestamp)
        {
            if (this.client.IsEnabled() && !this.enableNewDiagnosticEvents)
            {
                // It's possible to host multiple apps (ASP.NET Core or generic hosts) in the same process
                // Each of this apps has it's own HostingDiagnosticListener and corresponding Http listener.
                // We should ignore events for all of them except one
                if (!SubscriptionManager.IsActive(this))
                {
                    AspNetCoreEventSource.Instance.NotActiveListenerNoTracking(
                        "Microsoft.AspNetCore.Hosting.BeginRequest", Activity.Current?.Id);
                    return;
                }

                var activity = new Activity(ActivityCreatedByHostingDiagnosticListener);
                var isActivityCreatedFromRequestIdHeader = false;

                string sourceAppId = null;

                IHeaderDictionary requestHeaders = httpContext.Request.Headers;

                string originalParentId = null;
                // W3C
                if (this.enableW3CHeaders)
                {
                    SetW3CContext(httpContext.Request.Headers, activity, out sourceAppId);
                    var parentSpanId = activity.GetParentSpanId();
                    if (parentSpanId != null)
                    {
                        originalParentId = $"|{activity.GetTraceId()}.{parentSpanId}.";
                    }

                    // length enforced in SetW3CContext
                }

                // Request-Id
                if (requestHeaders.TryGetValue(RequestResponseHeaders.RequestIdHeader, out StringValues requestIdValues) &&
                    requestIdValues != StringValues.Empty)
                {
                    var requestId = StringUtilities.EnforceMaxLength(requestIdValues.First(), InjectionGuardConstants.RequestHeaderMaxLength);
                    isActivityCreatedFromRequestIdHeader = true;
                    activity.SetParentId(requestId);

                    ReadCorrelationContext(requestHeaders, activity);

                    if (originalParentId == null)
                    {
                        originalParentId = requestId;
                    }
                }

                // x-ms-request-id
                else if (requestHeaders.TryGetValue(RequestResponseHeaders.StandardRootIdHeader, out StringValues alternativeRootIdValues) &&
                         alternativeRootIdValues != StringValues.Empty)
                {
                    string alternativeRootId = StringUtilities.EnforceMaxLength(alternativeRootIdValues.First(), InjectionGuardConstants.RequestHeaderMaxLength);
                    activity.SetParentId(alternativeRootId);

                    if (originalParentId == null && requestHeaders.TryGetValue(RequestResponseHeaders.StandardParentIdHeader, out StringValues parentId))
                    {
                        originalParentId = StringUtilities.EnforceMaxLength(parentId.First(),
                            InjectionGuardConstants.RequestHeaderMaxLength);
                    }
                }
                else if(!activity.IsW3CActivity())
                {
                    // As a first step in supporting W3C protocol in ApplicationInsights,
                    // we want to generate Activity Ids in the W3C compatible format.
                    // While .NET changes to Activity are pending, we want to ensure trace starts with W3C compatible Id
                    // as early as possible, so that everyone has a chance to upgrade and have compatibility with W3C systems once they arrive.
                    // So if there is no current Activity (i.e. there were no Request-Id header in the incoming request), we'll override ParentId on 
                    // the current Activity by the properly formatted one. This workaround should go away
                    // with W3C support on .NET https://github.com/dotnet/corefx/issues/30331
                    
                    activity.SetParentId(StringUtilities.GenerateTraceId());

                    // end of workaround
                }

                activity.Start();

                var requestTelemetry = InitializeRequestTelemetry(httpContext, activity, isActivityCreatedFromRequestIdHeader, timestamp);
                if (this.enableW3CHeaders && sourceAppId != null)
                {
                    requestTelemetry.Source = sourceAppId;
                }

                // fix parent that may be modified by non-W3C operation correlation
                requestTelemetry.Context.Operation.ParentId = originalParentId;
                SetAppIdInResponseHeader(httpContext, requestTelemetry);
            }
        }

        /// <summary>
        /// Diagnostic event handler method for 'Microsoft.AspNetCore.Hosting.EndRequest' event.
        /// </summary>
        public void OnEndRequest(HttpContext httpContext, long timestamp)
        {
            if (!this.enableNewDiagnosticEvents)
            {
                EndRequest(httpContext, timestamp);
            }
        }

        /// <summary>
        /// Diagnostic event handler method for 'Microsoft.AspNetCore.Hosting.UnhandledException' event.
        /// </summary>
        public void OnHostingException(HttpContext httpContext, Exception exception)
        {
            this.OnException(httpContext, exception);

            // In AspNetCore 1.0, when an exception is unhandled it will only send the UnhandledException event, but not the EndRequest event, so we need to call EndRequest here.
            // In AspNetCore 2.0, after sending UnhandledException, it will stop the created activity, which will send HttpRequestIn.Stop event, so we will just end the request there.
            if (!this.enableNewDiagnosticEvents)
            {
                this.EndRequest(httpContext, Stopwatch.GetTimestamp());
            }
        }

        /// <summary>
        /// Diagnostic event handler method for 'Microsoft.AspNetCore.Hosting.HandledException' event.
        /// </summary>
        public void OnDiagnosticsHandledException(HttpContext httpContext, Exception exception)
        {
            this.OnException(httpContext, exception);
        }

        /// <summary>
        /// Diagnostic event handler method for 'Microsoft.AspNetCore.Diagnostics.UnhandledException' event.
        /// </summary>
        public void OnDiagnosticsUnhandledException(HttpContext httpContext, Exception exception)
        {
            this.OnException(httpContext, exception);
        }

        private RequestTelemetry InitializeRequestTelemetry(HttpContext httpContext, Activity activity, bool isActivityCreatedFromRequestIdHeader, long timestamp)
        {
            var requestTelemetry = new RequestTelemetry();

            if (!this.enableW3CHeaders)
            {
                requestTelemetry.Context.Operation.Id = activity.RootId;
                requestTelemetry.Id = activity.Id;
            }

            foreach (var prop in activity.Baggage)
            {
                if (!requestTelemetry.Properties.ContainsKey(prop.Key))
                {
                    requestTelemetry.Properties[prop.Key] = prop.Value;
                }
            }

            this.client.Initialize(requestTelemetry);

            requestTelemetry.Source = GetAppIdFromRequestHeader(httpContext.Request.Headers, requestTelemetry.Context.InstrumentationKey);

            requestTelemetry.Start(timestamp);
            httpContext.Features.Set(requestTelemetry);

            return requestTelemetry;
        }

        private string GetAppIdFromRequestHeader(IHeaderDictionary requestHeaders, string instrumentationKey)
        {
            // set Source
            string headerCorrelationId = HttpHeadersUtilities.GetRequestContextKeyValue(requestHeaders, RequestResponseHeaders.RequestContextSourceKey);

            // If the source header is present on the incoming request, and it is an external component (not the same ikey as the one used by the current component), populate the source field.
            if (!string.IsNullOrEmpty(headerCorrelationId))
            {
                headerCorrelationId = StringUtilities.EnforceMaxLength(headerCorrelationId, InjectionGuardConstants.AppIdMaxLength);
                if (string.IsNullOrEmpty(instrumentationKey))
                {
                    return headerCorrelationId;
                }

                string applicationId = null;
                if ((this.applicationIdProvider?.TryGetApplicationId(instrumentationKey, out applicationId) ?? false)
                         && applicationId != headerCorrelationId)
                {
                    return headerCorrelationId;
                }
            }

            return null;
        }

        private void SetAppIdInResponseHeader(HttpContext httpContext, RequestTelemetry requestTelemetry)
        {
            if (this.injectResponseHeaders)
            {
                IHeaderDictionary responseHeaders = httpContext.Response?.Headers;
                if (responseHeaders != null &&
                    !string.IsNullOrEmpty(requestTelemetry.Context.InstrumentationKey) &&
                    (!responseHeaders.ContainsKey(RequestResponseHeaders.RequestContextHeader) ||
                     HttpHeadersUtilities.ContainsRequestContextKeyValue(responseHeaders,
                         RequestResponseHeaders.RequestContextTargetKey)))
                {
                    string applicationId = null;
                    if (this.applicationIdProvider?.TryGetApplicationId(requestTelemetry.Context.InstrumentationKey,
                            out applicationId) ?? false)
                    {
                        HttpHeadersUtilities.SetRequestContextKeyValue(responseHeaders,
                            RequestResponseHeaders.RequestContextTargetKey, applicationId);
                    }
                }
            }
        }

        private void EndRequest(HttpContext httpContext, long timestamp)
        {
            if (this.client.IsEnabled())
            {
                // It's possible to host multiple apps (ASP.NET Core or generic hosts) in the same process
                // Each of this apps has it's own HostingDiagnosticListener and corresponding Http listener.
                // We should ignore events for all of them except one
                if (!SubscriptionManager.IsActive(this))
                {
                    AspNetCoreEventSource.Instance.NotActiveListenerNoTracking(
                        "EndRequest", Activity.Current?.Id);
                    return;
                }

                var telemetry = httpContext?.Features.Get<RequestTelemetry>();

                if (telemetry == null)
                {
                    return;
                }

                telemetry.Stop(timestamp);
                telemetry.ResponseCode = httpContext.Response.StatusCode.ToString(CultureInfo.InvariantCulture);

                var successExitCode = httpContext.Response.StatusCode < 400;
                if (telemetry.Success == null)
                {
                    telemetry.Success = successExitCode;
                }
                else
                {
                    telemetry.Success &= successExitCode;
                }

                if (string.IsNullOrEmpty(telemetry.Name))
                {
                    telemetry.Name = httpContext.Request.Method + " " + httpContext.Request.Path.Value;
                }
                
                telemetry.Url = httpContext.Request.GetUri();
                telemetry.Context.GetInternalContext().SdkVersion = this.sdkVersion;
                this.client.TrackRequest(telemetry);

                var activity = httpContext?.Features.Get<Activity>();
                if (activity != null && activity.OperationName == ActivityCreatedByHostingDiagnosticListener)
                {
                    activity.Stop();
                }
            }
        }

        private void OnException(HttpContext httpContext, Exception exception)
        {
            if (this.trackExceptions && this.client.IsEnabled())
            {
                // It's possible to host multiple apps (ASP.NET Core or generic hosts) in the same process
                // Each of this apps has it's own HostingDiagnosticListener and corresponding Http listener.
                // We should ignore events for all of them except one
                if (!SubscriptionManager.IsActive(this))
                {
                    AspNetCoreEventSource.Instance.NotActiveListenerNoTracking(
                        "Exception", Activity.Current?.Id);
                    return;
                }

                var telemetry = httpContext?.Features.Get<RequestTelemetry>();
                if (telemetry != null)
                {
                    telemetry.Success = false;
                }

                var exceptionTelemetry = new ExceptionTelemetry(exception);
                exceptionTelemetry.HandledAt = ExceptionHandledAt.Platform;
                exceptionTelemetry.Context.GetInternalContext().SdkVersion = this.sdkVersion;
                this.client.Track(exceptionTelemetry);
            }
        }

        private void SetW3CContext(IHeaderDictionary requestHeaders, Activity activity, out string sourceAppId)
        {
            sourceAppId = null;
            if (requestHeaders.TryGetValue(W3C.W3CConstants.TraceParentHeader, out StringValues traceParentValues))
            {
                var parentTraceParent = StringUtilities.EnforceMaxLength(traceParentValues.First(),
                    InjectionGuardConstants.TraceParentHeaderMaxLength);
                activity.SetTraceparent(parentTraceParent);
            }
            else
            {
                activity.GenerateW3CContext();
            }

            string[] traceStateValues = HttpHeadersUtilities.SafeGetCommaSeparatedHeaderValues(requestHeaders, W3C.W3CConstants.TraceStateHeader,
                InjectionGuardConstants.TraceStateHeaderMaxLength, InjectionGuardConstants.TraceStateMaxPairs);

            if (traceStateValues != null && traceStateValues.Any())
            {
                var pairsExceptAz = new StringBuilder();
                foreach (var t in traceStateValues)
                {
                    if (t.StartsWith(W3C.W3CConstants.AzureTracestateNamespace + "=", StringComparison.Ordinal))
                    {
                        // start after 'az='
                        TryExtractAppIdFromAzureTracestate(t.Substring(3), out sourceAppId);
                    }
                    else
                    {
                        pairsExceptAz.Append(t).Append(',');
                    }
                }

                if (pairsExceptAz.Length > 0)
                {
                    // remove last comma
                    var tracestateStr = pairsExceptAz.ToString(0, pairsExceptAz.Length - 1);
                    activity.SetTracestate(StringUtilities.EnforceMaxLength(tracestateStr, InjectionGuardConstants.TraceStateHeaderMaxLength));
                }
            }

            ReadCorrelationContext(requestHeaders, activity);
        }

        private void ReadCorrelationContext(IHeaderDictionary requestHeaders, Activity activity)
        {
            string[] baggage = requestHeaders.GetCommaSeparatedValues(RequestResponseHeaders.CorrelationContextHeader);
            if (baggage != StringValues.Empty && !activity.Baggage.Any())
            {
                foreach (var item in baggage)
                {
                    if (NameValueHeaderValue.TryParse(item, out var baggageItem))
                    {
                        var itemName = StringUtilities.EnforceMaxLength(baggageItem.Name, InjectionGuardConstants.ContextHeaderKeyMaxLength);
                        var itemValue = StringUtilities.EnforceMaxLength(baggageItem.Value, InjectionGuardConstants.ContextHeaderValueMaxLength);
                        activity.AddBaggage(itemName, itemValue);
                    }
                }
            }
        }

        private static bool TryExtractAppIdFromAzureTracestate(string azTracestate, out string appId)
        {
            appId = null;
            var parts = azTracestate.Split(W3C.W3CConstants.TracestateAzureSeparator);

            var appIds = parts.Where(p => p.StartsWith(W3C.W3CConstants.ApplicationIdTraceStateField, StringComparison.Ordinal)).ToArray();

            if (appIds.Length != 1)
            {
                return false;
            }

            appId = appIds[0];
            return true;
        }

        public void Dispose()
        {
            SubscriptionManager.Detach(this);
        }

        public void OnNext(KeyValuePair<string, object> value)
        {
            HttpContext httpContext = null;
            Exception exception = null;
            long? timestamp = null;

            switch (value.Key)
            {
                case "Microsoft.AspNetCore.Hosting.HttpRequestIn.Start":
                    httpContext = this.httpContextFetcherStart.Fetch(value.Value) as HttpContext;
                    if (httpContext != null)
                    {
                        this.OnHttpRequestInStart(httpContext);
                    }
                    break;
                case "Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop":
                    httpContext = this.httpContextFetcherStop.Fetch(value.Value) as HttpContext;
                    if (httpContext != null)
                    {
                        this.OnHttpRequestInStop(httpContext);
                    }
                    break;
                case "Microsoft.AspNetCore.Hosting.BeginRequest":
                    httpContext = this.httpContextFetcherBeginRequest.Fetch(value.Value) as HttpContext;
                    timestamp = this.timestampFetcherBeginRequest.Fetch(value.Value) as long?;
                    if (httpContext != null && timestamp.HasValue)
                    {
                        this.OnBeginRequest(httpContext, timestamp.Value);
                    }
                    break;
                case "Microsoft.AspNetCore.Hosting.EndRequest":
                    httpContext = this.httpContextFetcherEndRequest.Fetch(value.Value) as HttpContext;
                    timestamp = this.timestampFetcherEndRequest.Fetch(value.Value) as long?;
                    if (httpContext != null && timestamp.HasValue)
                    {
                        this.OnEndRequest(httpContext, timestamp.Value);
                    }
                    break;
                case "Microsoft.AspNetCore.Diagnostics.UnhandledException":
                    httpContext = this.httpContextFetcherDiagExceptionUnhandled.Fetch(value.Value) as HttpContext;
                    exception = this.exceptionFetcherDiagExceptionUnhandled.Fetch(value.Value) as Exception;
                    if (httpContext != null && exception != null)
                    {
                        this.OnDiagnosticsUnhandledException(httpContext, exception);
                    }
                    break;
                case "Microsoft.AspNetCore.Diagnostics.HandledException":
                    httpContext = this.httpContextFetcherDiagExceptionHandled.Fetch(value.Value) as HttpContext;
                    exception = this.exceptionFetcherDiagExceptionHandled.Fetch(value.Value) as Exception;
                    if (httpContext != null && exception != null)
                    {
                        this.OnDiagnosticsHandledException(httpContext, exception);
                    }
                    break;
                case "Microsoft.AspNetCore.Hosting.UnhandledException":
                    httpContext = this.httpContextFetcherHostingExceptionUnhandled.Fetch(value.Value) as HttpContext;
                    exception = this.exceptionFetcherHostingExceptionUnhandled.Fetch(value.Value) as Exception;
                    if (httpContext != null && exception != null)
                    {
                        this.OnHostingException(httpContext, exception);
                    }
                    break;
            }
        }

        /// <inheritdoc />
        public void OnError(Exception error)
        {
        }

        /// <inheritdoc />
        public void OnCompleted()
        {
        }

    }
}