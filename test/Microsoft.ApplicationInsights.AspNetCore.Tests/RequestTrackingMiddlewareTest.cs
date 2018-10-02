﻿namespace Microsoft.ApplicationInsights.AspNetCore.Tests
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Microsoft.ApplicationInsights.AspNetCore.DiagnosticListeners;
    using Microsoft.ApplicationInsights.AspNetCore.Tests.Helpers;
    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;
    using Microsoft.ApplicationInsights.W3C;
    using Microsoft.AspNetCore.Http;
    using Xunit;

    public class RequestTrackingMiddlewareTest : IDisposable
    {
        private const string HttpRequestScheme = "http";
        private const string ExpectedAppId = "cid-v1:some-app-id";

        private static readonly HostString HttpRequestHost = new HostString("testHost");
        private static readonly PathString HttpRequestPath = new PathString("/path/path");
        private static readonly QueryString HttpRequestQueryString = new QueryString("?query=1");

        private static Uri CreateUri(string scheme, HostString host, PathString? path = null, QueryString? query = null)
        {
            string uriString = string.Format(CultureInfo.InvariantCulture, "{0}://{1}", scheme, host);
            if (path != null)
            {
                uriString += path.Value;
            }
            if (query != null)
            {
                uriString += query.Value;
            }
            return new Uri(uriString);
        }

        private HttpContext CreateContext(string scheme, HostString host, PathString? path = null, QueryString? query = null, string method = null)
        {
            HttpContext context = new DefaultHttpContext();
            context.Request.Scheme = scheme;
            context.Request.Host = host;

            if (path.HasValue)
            {
                context.Request.Path = path.Value;
            }

            if (query.HasValue)
            {
                context.Request.QueryString = query.Value;
            }

            if (!string.IsNullOrEmpty(method))
            {
                context.Request.Method = method;
            }

            Assert.Null(context.Features.Get<RequestTelemetry>());

            return context;
        }

        private ConcurrentQueue<ITelemetry> sentTelemetry = new ConcurrentQueue<ITelemetry>();

        private HostingDiagnosticListener middleware;

        public RequestTrackingMiddlewareTest()
        {
            this.middleware = new HostingDiagnosticListener(
                CommonMocks.MockTelemetryClient(telemetry => this.sentTelemetry.Enqueue(telemetry)), 
                CommonMocks.GetMockApplicationIdProvider(),
                injectResponseHeaders: true,
                trackExceptions: true,
                enableW3CHeaders: false);
        }

        [Fact]
        public void TestSdkVersionIsPopulatedByMiddleware()
        {
            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost);

            HandleRequestBegin(context, 0);

            Assert.NotNull(context.Features.Get<RequestTelemetry>());

            Assert.Equal(CommonMocks.TestApplicationId, HttpHeadersUtilities.GetRequestContextKeyValue(context.Response.Headers, RequestResponseHeaders.RequestContextTargetKey));

            HandleRequestEnd(context, 0);

            Assert.Single(sentTelemetry);
            Assert.IsType<RequestTelemetry>(this.sentTelemetry.First());

            RequestTelemetry requestTelemetry = this.sentTelemetry.First() as RequestTelemetry;
            Assert.True(requestTelemetry.Duration.TotalMilliseconds >= 0);
            Assert.True(requestTelemetry.Success);
            Assert.Equal(CommonMocks.InstrumentationKey, requestTelemetry.Context.InstrumentationKey);
            Assert.True(string.IsNullOrEmpty(requestTelemetry.Source));
            Assert.Equal(CreateUri(HttpRequestScheme, HttpRequestHost), requestTelemetry.Url);
            Assert.NotEmpty(requestTelemetry.Context.GetInternalContext().SdkVersion);
            Assert.Contains(SdkVersionTestUtils.VersionPrefix, requestTelemetry.Context.GetInternalContext().SdkVersion);
        }

        [Fact]
        public void TestRequestUriIsPopulatedByMiddleware()
        {
            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost, HttpRequestPath, HttpRequestQueryString);

            HandleRequestBegin(context, 0);

            Assert.NotNull(context.Features.Get<RequestTelemetry>());
            Assert.Equal(CommonMocks.TestApplicationId, HttpHeadersUtilities.GetRequestContextKeyValue(context.Response.Headers, RequestResponseHeaders.RequestContextTargetKey));

            HandleRequestEnd(context, 0);

            Assert.Single(sentTelemetry);
            Assert.IsType<RequestTelemetry>(this.sentTelemetry.First());
            RequestTelemetry requestTelemetry = sentTelemetry.First() as RequestTelemetry;
            Assert.NotNull(requestTelemetry.Url);
            Assert.True(requestTelemetry.Duration.TotalMilliseconds >= 0);
            Assert.True(requestTelemetry.Success);
            Assert.Equal(CommonMocks.InstrumentationKey, requestTelemetry.Context.InstrumentationKey);
            Assert.True(string.IsNullOrEmpty(requestTelemetry.Source));
            Assert.Equal(CreateUri(HttpRequestScheme, HttpRequestHost, HttpRequestPath, HttpRequestQueryString), requestTelemetry.Url);
            Assert.NotEmpty(requestTelemetry.Context.GetInternalContext().SdkVersion);
            Assert.Contains(SdkVersionTestUtils.VersionPrefix, requestTelemetry.Context.GetInternalContext().SdkVersion);
        }

        [Fact]
        public void RequestWillBeMarkedAsFailedForRunawayException()
        {
            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost);

            HandleRequestBegin(context, 0);

            Assert.NotNull(context.Features.Get<RequestTelemetry>());
            Assert.Equal(CommonMocks.TestApplicationId, HttpHeadersUtilities.GetRequestContextKeyValue(context.Response.Headers, RequestResponseHeaders.RequestContextTargetKey));

            middleware.OnDiagnosticsUnhandledException(context, null);
            HandleRequestEnd(context, 0);

            var telemetries = sentTelemetry.ToArray();
            Assert.Equal(2, sentTelemetry.Count);
            Assert.IsType<ExceptionTelemetry>(telemetries[0]);

            Assert.IsType<RequestTelemetry>(telemetries[1]);
            RequestTelemetry requestTelemetry = telemetries[1] as RequestTelemetry;
            Assert.True(requestTelemetry.Duration.TotalMilliseconds >= 0);
            Assert.False(requestTelemetry.Success);
            Assert.Equal(CommonMocks.InstrumentationKey, requestTelemetry.Context.InstrumentationKey);
            Assert.True(string.IsNullOrEmpty(requestTelemetry.Source));
            Assert.Equal(CreateUri(HttpRequestScheme, HttpRequestHost), requestTelemetry.Url);
            Assert.NotEmpty(requestTelemetry.Context.GetInternalContext().SdkVersion);
            Assert.Contains(SdkVersionTestUtils.VersionPrefix, requestTelemetry.Context.GetInternalContext().SdkVersion);
        }

        [Fact]
        public void OnBeginRequestCreateNewActivityAndInitializeRequestTelemetry()
        {
            if (HostingDiagnosticListener.IsAspNetCore20)
            {
                return;
            }

            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "POST");

            middleware.OnBeginRequest(context, 0);

            Assert.NotNull(Activity.Current);

            var requestTelemetry = context.Features.Get<RequestTelemetry>();
            Assert.NotNull(requestTelemetry);
            Assert.Equal(requestTelemetry.Id, Activity.Current.Id);
            Assert.Equal(requestTelemetry.Context.Operation.Id, Activity.Current.RootId);
            Assert.Null(requestTelemetry.Context.Operation.ParentId);

            // W3C compatible-Id ( should go away when W3C is implemented in .NET https://github.com/dotnet/corefx/issues/30331)
            Assert.Equal(32, requestTelemetry.Context.Operation.Id.Length);
            Assert.True(Regex.Match(requestTelemetry.Context.Operation.Id, @"[a-z][0-9]").Success);
            // end of workaround test
        }

        [Fact]
        public void OnBeginRequestCreateNewActivityAndInitializeRequestTelemetryFromStandardHeader()
        {
            if (HostingDiagnosticListener.IsAspNetCore20)
            {
                return;
            }

            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "POST");
            var standardRequestId = Guid.NewGuid().ToString();
            var standardRequestRootId = Guid.NewGuid().ToString();
            context.Request.Headers[RequestResponseHeaders.StandardParentIdHeader] = standardRequestId;
            context.Request.Headers[RequestResponseHeaders.StandardRootIdHeader] = standardRequestRootId;

            middleware.OnBeginRequest(context, 0);

            Assert.NotNull(Activity.Current);

            var requestTelemetry = context.Features.Get<RequestTelemetry>();
            Assert.NotNull(requestTelemetry);
            Assert.Equal(requestTelemetry.Id, Activity.Current.Id);
            Assert.Equal(requestTelemetry.Context.Operation.Id, standardRequestRootId);
            Assert.Equal(requestTelemetry.Context.Operation.ParentId, standardRequestId);
        }

        [Fact]
        public void OnBeginRequestCreateNewActivityAndInitializeRequestTelemetryFromRequestIdHeader()
        {
            if (HostingDiagnosticListener.IsAspNetCore20)
            {
                return;
            }

            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "POST");
            var requestId = Guid.NewGuid().ToString();
            var standardRequestId = Guid.NewGuid().ToString();
            var standardRequestRootId = Guid.NewGuid().ToString();
            context.Request.Headers[RequestResponseHeaders.RequestIdHeader] = requestId;
            context.Request.Headers[RequestResponseHeaders.StandardParentIdHeader] = standardRequestId;
            context.Request.Headers[RequestResponseHeaders.StandardRootIdHeader] = standardRequestRootId;
            context.Request.Headers[RequestResponseHeaders.CorrelationContextHeader] = "prop1=value1, prop2=value2";

            middleware.OnBeginRequest(context, 0);

            Assert.NotNull(Activity.Current);
            Assert.Single(Activity.Current.Baggage.Where(b => b.Key == "prop1" && b.Value == "value1"));
            Assert.Single(Activity.Current.Baggage.Where(b => b.Key == "prop2" && b.Value == "value2"));

            var requestTelemetry = context.Features.Get<RequestTelemetry>();
            Assert.NotNull(requestTelemetry);
            Assert.Equal(requestTelemetry.Id, Activity.Current.Id);
            Assert.Equal(requestTelemetry.Context.Operation.Id, Activity.Current.RootId);
            Assert.NotEqual(requestTelemetry.Context.Operation.Id, standardRequestRootId);
            Assert.Equal(requestTelemetry.Context.Operation.ParentId, requestId);
            Assert.NotEqual(requestTelemetry.Context.Operation.ParentId, standardRequestId);
            Assert.Equal("value1", requestTelemetry.Context.Properties["prop1"]);
            Assert.Equal("value2", requestTelemetry.Context.Properties["prop2"]);
        }

        [Fact]
        public void OnHttpRequestInStartInitializeTelemetryIfActivityParentIdIsNotNull()
        {
            if (!HostingDiagnosticListener.IsAspNetCore20)
            {
                return;
            }

            var context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "POST");
            var activity = new Activity("operation");
            activity.SetParentId(Guid.NewGuid().ToString());
            activity.AddBaggage("item1", "value1");
            activity.AddBaggage("item2", "value2");

            activity.Start();

            middleware.OnHttpRequestInStart(context);
            middleware.OnHttpRequestInStop(context);

            Assert.Single(sentTelemetry);
            var requestTelemetry = this.sentTelemetry.First() as RequestTelemetry;

            Assert.Equal(requestTelemetry.Id, activity.Id);
            Assert.Equal(requestTelemetry.Context.Operation.Id, activity.RootId);
            Assert.Equal(requestTelemetry.Context.Operation.ParentId, activity.ParentId);
            Assert.True(requestTelemetry.Context.Properties.Count > activity.Baggage.Count());

            foreach (var prop in activity.Baggage)
            {
                Assert.True(requestTelemetry.Context.Properties.ContainsKey(prop.Key));
                Assert.Equal(requestTelemetry.Context.Properties[prop.Key], prop.Value);
            }
        }

        [Fact]
        public void OnHttpRequestInStartCreateNewActivityIfParentIdIsNullAndHasStandardHeader()
        {
            if (!HostingDiagnosticListener.IsAspNetCore20)
            {
                return;
            }

            var context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "POST");
            var standardRequestId = Guid.NewGuid().ToString();
            var standardRequestRootId = Guid.NewGuid().ToString();
            context.Request.Headers[RequestResponseHeaders.StandardParentIdHeader] = standardRequestId;
            context.Request.Headers[RequestResponseHeaders.StandardRootIdHeader] = standardRequestRootId;

            var activity = new Activity("operation");
            activity.Start();

            middleware.OnHttpRequestInStart(context);

            var activityInitializedByStandardHeader = Activity.Current;
            Assert.NotEqual(activityInitializedByStandardHeader, activity);
            Assert.Equal(activityInitializedByStandardHeader.ParentId, standardRequestRootId);

            middleware.OnHttpRequestInStop(context);

            Assert.Single(sentTelemetry);
            var requestTelemetry = this.sentTelemetry.First() as RequestTelemetry;

            Assert.Equal(requestTelemetry.Id, activityInitializedByStandardHeader.Id);
            Assert.Equal(requestTelemetry.Context.Operation.Id, standardRequestRootId);
            Assert.Equal(requestTelemetry.Context.Operation.ParentId, standardRequestId);
        }

        [Fact]
        public void OnEndRequestSetsRequestNameToMethodAndPathForPostRequest()
        {
            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "POST");

            HandleRequestBegin(context, 0);

            Assert.NotNull(context.Features.Get<RequestTelemetry>());
            Assert.Equal(CommonMocks.TestApplicationId, HttpHeadersUtilities.GetRequestContextKeyValue(context.Response.Headers, RequestResponseHeaders.RequestContextTargetKey));

            HandleRequestEnd(context, 0);

            Assert.Single(sentTelemetry);
            Assert.IsType<RequestTelemetry>(this.sentTelemetry.First());
            RequestTelemetry requestTelemetry = this.sentTelemetry.Single() as RequestTelemetry;
            Assert.True(requestTelemetry.Duration.TotalMilliseconds >= 0);
            Assert.True(requestTelemetry.Success);
            Assert.Equal(CommonMocks.InstrumentationKey, requestTelemetry.Context.InstrumentationKey);
            Assert.True(string.IsNullOrEmpty(requestTelemetry.Source));
            Assert.Equal(CreateUri(HttpRequestScheme, HttpRequestHost, "/Test"), requestTelemetry.Url);
            Assert.NotEmpty(requestTelemetry.Context.GetInternalContext().SdkVersion);
            Assert.Contains(SdkVersionTestUtils.VersionPrefix, requestTelemetry.Context.GetInternalContext().SdkVersion);
            Assert.Equal("POST /Test", requestTelemetry.Name);
        }

        [Fact]
        public void OnEndRequestSetsRequestNameToMethodAndPath()
        {
            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "GET");

            HandleRequestBegin(context, 0);

            Assert.NotNull(context.Features.Get<RequestTelemetry>());
            Assert.Equal(CommonMocks.TestApplicationId, HttpHeadersUtilities.GetRequestContextKeyValue(context.Response.Headers, RequestResponseHeaders.RequestContextTargetKey));

            HandleRequestEnd(context, 0);

            Assert.NotNull(this.sentTelemetry);
            Assert.IsType<RequestTelemetry>(this.sentTelemetry.First());
            RequestTelemetry requestTelemetry = this.sentTelemetry.First() as RequestTelemetry;
            Assert.True(requestTelemetry.Duration.TotalMilliseconds >= 0);
            Assert.True(requestTelemetry.Success);
            Assert.Equal(CommonMocks.InstrumentationKey, requestTelemetry.Context.InstrumentationKey);
            Assert.True(string.IsNullOrEmpty(requestTelemetry.Source));            
            Assert.Equal(CreateUri(HttpRequestScheme, HttpRequestHost, "/Test"), requestTelemetry.Url);
            Assert.NotEmpty(requestTelemetry.Context.GetInternalContext().SdkVersion);
            Assert.Contains(SdkVersionTestUtils.VersionPrefix, requestTelemetry.Context.GetInternalContext().SdkVersion);
            Assert.Equal("GET /Test", requestTelemetry.Name);
        }

        [Fact]
        public void OnEndRequestFromSameInstrumentationKey()
        {
            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "GET");
            HttpHeadersUtilities.SetRequestContextKeyValue(context.Request.Headers, RequestResponseHeaders.RequestContextSourceKey, CommonMocks.TestApplicationId);

            HandleRequestBegin(context, 0);

            Assert.NotNull(context.Features.Get<RequestTelemetry>());

            Assert.Equal(CommonMocks.TestApplicationId, HttpHeadersUtilities.GetRequestContextKeyValue(context.Response.Headers, RequestResponseHeaders.RequestContextTargetKey));

            HandleRequestEnd(context, 0);

            Assert.NotNull(this.sentTelemetry);
            Assert.IsType<RequestTelemetry>(this.sentTelemetry.First());
            RequestTelemetry requestTelemetry = this.sentTelemetry.First() as RequestTelemetry;
            Assert.True(requestTelemetry.Duration.TotalMilliseconds >= 0);
            Assert.True(requestTelemetry.Success);
            Assert.Equal(CommonMocks.InstrumentationKey, requestTelemetry.Context.InstrumentationKey);
            Assert.True(string.IsNullOrEmpty(requestTelemetry.Source));
            Assert.Equal(CreateUri(HttpRequestScheme, HttpRequestHost, "/Test"), requestTelemetry.Url);
            Assert.NotEmpty(requestTelemetry.Context.GetInternalContext().SdkVersion);
            Assert.Contains(SdkVersionTestUtils.VersionPrefix, requestTelemetry.Context.GetInternalContext().SdkVersion);
            Assert.Equal("GET /Test", requestTelemetry.Name);
        }

        [Fact]
        public void OnEndRequestFromDifferentInstrumentationKey()
        {
            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "GET");
            HttpHeadersUtilities.SetRequestContextKeyValue(context.Request.Headers, RequestResponseHeaders.RequestContextSourceKey, "DIFFERENT_INSTRUMENTATION_KEY_HASH");

            HandleRequestBegin(context, 0);

            Assert.NotNull(context.Features.Get<RequestTelemetry>());
            Assert.Equal(CommonMocks.TestApplicationId, HttpHeadersUtilities.GetRequestContextKeyValue(context.Response.Headers, RequestResponseHeaders.RequestContextTargetKey));

            HandleRequestEnd(context, 0);

            Assert.Single(sentTelemetry);
            Assert.IsType<RequestTelemetry>(this.sentTelemetry.First());
            RequestTelemetry requestTelemetry = this.sentTelemetry.First() as RequestTelemetry;
            Assert.True(requestTelemetry.Duration.TotalMilliseconds >= 0);
            Assert.True(requestTelemetry.Success);
            Assert.Equal(CommonMocks.InstrumentationKey, requestTelemetry.Context.InstrumentationKey);
            Assert.Equal("DIFFERENT_INSTRUMENTATION_KEY_HASH", requestTelemetry.Source);            
            Assert.Equal(CreateUri(HttpRequestScheme, HttpRequestHost, "/Test"), requestTelemetry.Url);
            Assert.NotEmpty(requestTelemetry.Context.GetInternalContext().SdkVersion);
            Assert.Contains(SdkVersionTestUtils.VersionPrefix, requestTelemetry.Context.GetInternalContext().SdkVersion);
            Assert.Equal("GET /Test", requestTelemetry.Name);
        }

        [Fact]
        public async void SimultaneousRequestsGetDifferentIds()
        {
            var context1 = new DefaultHttpContext();
            context1.Request.Scheme = HttpRequestScheme;
            context1.Request.Host = HttpRequestHost;
            context1.Request.Method = "GET";
            context1.Request.Path = "/Test?id=1";

            var context2 = new DefaultHttpContext();
            context2.Request.Scheme = HttpRequestScheme;
            context2.Request.Host = HttpRequestHost;
            context2.Request.Method = "GET";
            context2.Request.Path = "/Test?id=2";

            var task1 = Task.Run(() =>
            {
                var act = new Activity("operation1");
                act.Start();
                HandleRequestBegin(context1, 0);
                HandleRequestEnd(context1, 0);
            });

            var task2 = Task.Run(() =>
            {
                var act = new Activity("operation2");
                act.Start();
                HandleRequestBegin(context2, 0);
                HandleRequestEnd(context2, 0);
            });

            await Task.WhenAll(task1, task2);

            Assert.Equal(2, sentTelemetry.Count);

            var telemetries = this.sentTelemetry.ToArray();
            Assert.IsType<RequestTelemetry>(telemetries[0]);
            Assert.IsType<RequestTelemetry>(telemetries[1]);
            var id1 = ((RequestTelemetry)telemetries[0]).Id;
            var id2 = ((RequestTelemetry)telemetries[1]).Id;
            Assert.NotEqual(id1, id2);
        }

        [Fact]
        public void SimultaneousRequestsGetCorrectDurations()
        {
            if (HostingDiagnosticListener.IsAspNetCore20)
            {
                return;
            }

            var context1 = new DefaultHttpContext();
            context1.Request.Scheme = HttpRequestScheme;
            context1.Request.Host = HttpRequestHost;
            context1.Request.Method = "GET";
            context1.Request.Path = "/Test?id=1";

            var context2 = new DefaultHttpContext();
            context2.Request.Scheme = HttpRequestScheme;
            context2.Request.Host = HttpRequestHost;
            context2.Request.Method = "GET";
            context2.Request.Path = "/Test?id=2";

            long startTime = Stopwatch.GetTimestamp();
            long simulatedSeconds = Stopwatch.Frequency;

            HandleRequestBegin(context1, startTime);
            HandleRequestBegin(context2, startTime + simulatedSeconds);
            HandleRequestEnd(context1, startTime + simulatedSeconds * 5);
            HandleRequestEnd(context2, startTime + simulatedSeconds * 10);

            var telemetries = this.sentTelemetry.ToArray();
            Assert.Equal(2, telemetries.Length);
            Assert.Equal(TimeSpan.FromSeconds(5), ((RequestTelemetry)telemetries[0]).Duration);
            Assert.Equal(TimeSpan.FromSeconds(9), ((RequestTelemetry)telemetries[1]).Duration);
        }

        [Fact]
        public void OnEndRequestSetsPreciseDurations()
        {
            if (HostingDiagnosticListener.IsAspNetCore20)
            {
                return;
            }

            var context = new DefaultHttpContext();
            context.Request.Scheme = HttpRequestScheme;
            context.Request.Host = HttpRequestHost;
            context.Request.Method = "GET";
            context.Request.Path = "/Test?id=1";

            long startTime = Stopwatch.GetTimestamp();
            HandleRequestBegin(context, startTime);

            var expectedDuration = TimeSpan.Parse("00:00:01.2345670");
            double durationInStopwatchTicks = Stopwatch.Frequency * expectedDuration.TotalSeconds;

            HandleRequestEnd(context, startTime + (long)durationInStopwatchTicks);

            Assert.Single(sentTelemetry);
            Assert.Equal(Math.Round(expectedDuration.TotalMilliseconds, 3), Math.Round(((RequestTelemetry)sentTelemetry.First()).Duration.TotalMilliseconds, 3));
        }

        [Fact]
        public void SetsSourceProvidedInHeaders()
        {
            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost);
            HttpHeadersUtilities.SetRequestContextKeyValue(context.Request.Headers, RequestResponseHeaders.RequestContextTargetKey, "someAppId");

            HandleRequestBegin(context, 0);
            HandleRequestEnd(context, 0);

            Assert.Single(sentTelemetry);
            Assert.IsType<RequestTelemetry>(this.sentTelemetry.Single());
            RequestTelemetry requestTelemetry = this.sentTelemetry.OfType<RequestTelemetry>().Single();

            Assert.Equal("someAppId", requestTelemetry.Source);
        }

        [Fact]
        public void ResponseHeadersAreNotInjectedWhenDisabled()
        {
            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost);

            var noHeadersMiddleware = new HostingDiagnosticListener(
                CommonMocks.MockTelemetryClient(telemetry => this.sentTelemetry.Enqueue(telemetry)),
                CommonMocks.GetMockApplicationIdProvider(),
                injectResponseHeaders: false,
                trackExceptions: true,
                enableW3CHeaders: false);

            noHeadersMiddleware.OnBeginRequest(context, 0);
            Assert.False(context.Response.Headers.ContainsKey(RequestResponseHeaders.RequestContextHeader));
            noHeadersMiddleware.OnEndRequest(context, 0);
            Assert.False(context.Response.Headers.ContainsKey(RequestResponseHeaders.RequestContextHeader));

            Assert.Single(sentTelemetry);
            Assert.IsType<RequestTelemetry>(this.sentTelemetry.First());
        }

        [Fact]
        public void ExceptionsAreNotTrackedInjectedWhenDisabled()
        {
            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost);

            var noExceptionsMiddleware = new HostingDiagnosticListener(
                CommonMocks.MockTelemetryClient(telemetry => this.sentTelemetry.Enqueue(telemetry)),
                CommonMocks.GetMockApplicationIdProvider(),
                injectResponseHeaders: true,
                trackExceptions: false,
                enableW3CHeaders: false);

            noExceptionsMiddleware.OnHostingException(context, new Exception("HostingException"));
            noExceptionsMiddleware.OnDiagnosticsHandledException(context, new Exception("DiagnosticsHandledException"));
            noExceptionsMiddleware.OnDiagnosticsUnhandledException(context, new Exception("UnhandledException"));

            Assert.Empty(sentTelemetry);
        }

        [Fact]
        public void DoesntAddSourceIfRequestHeadersDontHaveSource()
        {
            HttpContext context = CreateContext(HttpRequestScheme, HttpRequestHost);

            HandleRequestBegin(context, 0);
            HandleRequestEnd(context, 0);

            Assert.Single(sentTelemetry);
            Assert.IsType<RequestTelemetry>(this.sentTelemetry.Single());
            RequestTelemetry requestTelemetry = this.sentTelemetry.OfType<RequestTelemetry>().Single();

            Assert.True(string.IsNullOrEmpty(requestTelemetry.Source));
        }

        #pragma warning disable 612, 618
        [Fact]
        public void OnBeginRequestWithW3CHeadersIsTrackedCorrectly()
        {
            var configuration = TelemetryConfiguration.CreateDefault();
            configuration.TelemetryInitializers.Add(new W3COperationCorrelationTelemetryInitializer());
            this.middleware = new HostingDiagnosticListener(
                CommonMocks.MockTelemetryClient(telemetry => this.sentTelemetry.Enqueue(telemetry), configuration),
                CommonMocks.GetMockApplicationIdProvider(),
                injectResponseHeaders: true,
                trackExceptions: true,
                enableW3CHeaders: true);

            var context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "POST");

            context.Request.Headers[W3CConstants.TraceParentHeader] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
            context.Request.Headers[W3CConstants.TraceStateHeader] = "state=some";
            context.Request.Headers[RequestResponseHeaders.CorrelationContextHeader] = "k=v";

            if (HostingDiagnosticListener.IsAspNetCore20)
            {
                var activity = new Activity("operation");
                activity.Start();

                middleware.OnHttpRequestInStart(context);

                Assert.NotEqual(Activity.Current, activity);
            }
            else
            {
                middleware.OnBeginRequest(context, Stopwatch.GetTimestamp());
            }

            var activityInitializedByW3CHeader = Activity.Current;
            Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", activityInitializedByW3CHeader.GetTraceId());
            Assert.Equal("00f067aa0ba902b7", activityInitializedByW3CHeader.GetParentSpanId());
            Assert.Equal(16, activityInitializedByW3CHeader.GetSpanId().Length);
            Assert.Equal("state=some", activityInitializedByW3CHeader.GetTracestate());
            Assert.Equal("v", activityInitializedByW3CHeader.Baggage.Single(t => t.Key == "k").Value);

            if (HostingDiagnosticListener.IsAspNetCore20)
            {
                middleware.OnHttpRequestInStop(context);
            }
            else
            {
                middleware.OnEndRequest(context, Stopwatch.GetTimestamp());
            }

            Assert.Single(sentTelemetry);
            var requestTelemetry = (RequestTelemetry)this.sentTelemetry.Single();

            Assert.Equal($"|4bf92f3577b34da6a3ce929d0e0e4736.{activityInitializedByW3CHeader.GetSpanId()}.", requestTelemetry.Id);
            Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", requestTelemetry.Context.Operation.Id);
            Assert.Equal("|4bf92f3577b34da6a3ce929d0e0e4736.00f067aa0ba902b7.", requestTelemetry.Context.Operation.ParentId);

            Assert.True(context.Response.Headers.TryGetValue(RequestResponseHeaders.RequestContextHeader, out var appId));
            Assert.Equal($"appId={CommonMocks.TestApplicationId}", appId);
        }

        [Fact]
        public void OnBeginRequestWithW3CHeadersAndRequestIdIsTrackedCorrectly()
        {
            var configuration = TelemetryConfiguration.CreateDefault();
            configuration.TelemetryInitializers.Add(new W3COperationCorrelationTelemetryInitializer());
            this.middleware = new HostingDiagnosticListener(
                CommonMocks.MockTelemetryClient(telemetry => this.sentTelemetry.Enqueue(telemetry), configuration),
                CommonMocks.GetMockApplicationIdProvider(),
                injectResponseHeaders: true,
                trackExceptions: true,
                enableW3CHeaders: true);

            var context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "POST");

            context.Request.Headers[RequestResponseHeaders.RequestIdHeader] = "|abc.1.2.3.";
            context.Request.Headers[W3CConstants.TraceParentHeader] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
            context.Request.Headers[W3CConstants.TraceStateHeader] = "state=some";
            context.Request.Headers[RequestResponseHeaders.CorrelationContextHeader] = "k=v";

            middleware.OnBeginRequest(context, Stopwatch.GetTimestamp());
            var activityInitializedByW3CHeader = Activity.Current;

            Assert.Equal("|abc.1.2.3.", activityInitializedByW3CHeader.ParentId);
            Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", activityInitializedByW3CHeader.GetTraceId());
            Assert.Equal("00f067aa0ba902b7", activityInitializedByW3CHeader.GetParentSpanId());
            Assert.Equal(16, activityInitializedByW3CHeader.GetSpanId().Length);
            Assert.Equal("state=some", activityInitializedByW3CHeader.GetTracestate());
            Assert.Equal("v", activityInitializedByW3CHeader.Baggage.Single(t => t.Key == "k").Value);

            middleware.OnEndRequest(context, Stopwatch.GetTimestamp());

            Assert.Single(sentTelemetry);
            var requestTelemetry = (RequestTelemetry)this.sentTelemetry.Single();

            Assert.Equal($"|4bf92f3577b34da6a3ce929d0e0e4736.{activityInitializedByW3CHeader.GetSpanId()}.", requestTelemetry.Id);
            Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", requestTelemetry.Context.Operation.Id);
            Assert.Equal("|4bf92f3577b34da6a3ce929d0e0e4736.00f067aa0ba902b7.", requestTelemetry.Context.Operation.ParentId);

            Assert.True(context.Response.Headers.TryGetValue(RequestResponseHeaders.RequestContextHeader, out var appId));
            Assert.Equal($"appId={CommonMocks.TestApplicationId}", appId);

            Assert.Equal("abc", requestTelemetry.Properties["ai_legacyRootId"]);
            Assert.StartsWith("|abc.1.2.3.", requestTelemetry.Properties["ai_legacyRequestId"]);
        }

        [Fact]
        public void OnBeginRequestWithNoW3CHeadersAndRequestIdIsTrackedCorrectly()
        {
            var configuration = TelemetryConfiguration.CreateDefault();
            configuration.TelemetryInitializers.Add(new W3COperationCorrelationTelemetryInitializer());
            this.middleware = new HostingDiagnosticListener(
                CommonMocks.MockTelemetryClient(telemetry => this.sentTelemetry.Enqueue(telemetry), configuration),
                CommonMocks.GetMockApplicationIdProvider(),
                injectResponseHeaders: true,
                trackExceptions: true,
                enableW3CHeaders: true);

            var context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "POST");

            context.Request.Headers[RequestResponseHeaders.RequestIdHeader] = "|abc.1.2.3.";
            context.Request.Headers[RequestResponseHeaders.CorrelationContextHeader] = "k=v";

            middleware.OnBeginRequest(context, Stopwatch.GetTimestamp());
            var activityInitializedByW3CHeader = Activity.Current;

            Assert.Equal("|abc.1.2.3.", activityInitializedByW3CHeader.ParentId);
            middleware.OnEndRequest(context, Stopwatch.GetTimestamp());

            Assert.Single(sentTelemetry);
            var requestTelemetry = (RequestTelemetry)this.sentTelemetry.Single();

            Assert.Equal($"|{activityInitializedByW3CHeader.GetTraceId()}.{activityInitializedByW3CHeader.GetSpanId()}.", requestTelemetry.Id);
            Assert.Equal(activityInitializedByW3CHeader.GetTraceId(), requestTelemetry.Context.Operation.Id);
            Assert.Equal("|abc.1.2.3.", requestTelemetry.Context.Operation.ParentId);

            Assert.Equal("abc", requestTelemetry.Properties["ai_legacyRootId"]);
            Assert.StartsWith("|abc.1.2.3.", requestTelemetry.Properties["ai_legacyRequestId"]);
        }

        [Fact]
        public void OnBeginRequestWithW3CSupportAndNoHeadersIsTrackedCorrectly()
        {
            var configuration = TelemetryConfiguration.CreateDefault();
            configuration.TelemetryInitializers.Add(new W3COperationCorrelationTelemetryInitializer());
            this.middleware = new HostingDiagnosticListener(
                CommonMocks.MockTelemetryClient(telemetry => this.sentTelemetry.Enqueue(telemetry), configuration),
                CommonMocks.GetMockApplicationIdProvider(),
                injectResponseHeaders: true,
                trackExceptions: true,
                enableW3CHeaders: true);

            var context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "POST");

            middleware.OnBeginRequest(context, Stopwatch.GetTimestamp());

            var activityInitializedByW3CHeader = Activity.Current;
            Assert.Null(activityInitializedByW3CHeader.ParentId);
            Assert.NotNull(activityInitializedByW3CHeader.GetTraceId());
            Assert.Equal(32, activityInitializedByW3CHeader.GetTraceId().Length);
            Assert.Equal(16, activityInitializedByW3CHeader.GetSpanId().Length);
            Assert.Equal($"00-{activityInitializedByW3CHeader.GetTraceId()}-{activityInitializedByW3CHeader.GetSpanId()}-02",
                activityInitializedByW3CHeader.GetTraceparent());
            Assert.Null(activityInitializedByW3CHeader.GetTracestate());
            Assert.Empty(activityInitializedByW3CHeader.Baggage);

            middleware.OnEndRequest(context, Stopwatch.GetTimestamp());

            Assert.Single(sentTelemetry);
            var requestTelemetry = (RequestTelemetry)this.sentTelemetry.Single();

            Assert.Equal($"|{activityInitializedByW3CHeader.GetTraceId()}.{activityInitializedByW3CHeader.GetSpanId()}.", requestTelemetry.Id);
            Assert.Equal(activityInitializedByW3CHeader.GetTraceId(), requestTelemetry.Context.Operation.Id);
            Assert.Null(requestTelemetry.Context.Operation.ParentId);

            Assert.True(context.Response.Headers.TryGetValue(RequestResponseHeaders.RequestContextHeader, out var appId));
            Assert.Equal($"appId={CommonMocks.TestApplicationId}", appId);
        }

        [Fact]
        public void OnBeginRequestWithW3CHeadersAndAppIdInState()
        {
            var configuration = TelemetryConfiguration.CreateDefault();
            configuration.TelemetryInitializers.Add(new W3COperationCorrelationTelemetryInitializer());
            this.middleware = new HostingDiagnosticListener(
                CommonMocks.MockTelemetryClient(telemetry => this.sentTelemetry.Enqueue(telemetry), configuration),
                CommonMocks.GetMockApplicationIdProvider(),
                injectResponseHeaders: true,
                trackExceptions: true,
                enableW3CHeaders: true);

            var context = CreateContext(HttpRequestScheme, HttpRequestHost, "/Test", method: "POST");

            context.Request.Headers[W3CConstants.TraceParentHeader] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00";
            context.Request.Headers[W3CConstants.TraceStateHeader] = $"state=some,{W3CConstants.AzureTracestateNamespace}={ExpectedAppId}";

            if (HostingDiagnosticListener.IsAspNetCore20)
            {
                var activity = new Activity("operation");
                activity.Start();

                middleware.OnHttpRequestInStart(context);
                Assert.NotEqual(Activity.Current, activity);
            }
            else
            {
                middleware.OnBeginRequest(context, Stopwatch.GetTimestamp());
            }

            var activityInitializedByW3CHeader = Activity.Current;

            Assert.Equal("state=some", activityInitializedByW3CHeader.GetTracestate());

            if (HostingDiagnosticListener.IsAspNetCore20)
            {
                middleware.OnHttpRequestInStop(context);
            }
            else
            {
                middleware.OnEndRequest(context, Stopwatch.GetTimestamp());
            }

            Assert.Single(sentTelemetry);
            var requestTelemetry = (RequestTelemetry)this.sentTelemetry.Single();

            Assert.Equal(ExpectedAppId, requestTelemetry.Source);

            Assert.True(context.Response.Headers.TryGetValue(RequestResponseHeaders.RequestContextHeader, out var appId));
            Assert.Equal($"appId={CommonMocks.TestApplicationId}", appId);
        }
#pragma warning restore 612, 618

        private void HandleRequestBegin(HttpContext context, long timestamp)
        {
            if (HostingDiagnosticListener.IsAspNetCore20)
            {
                if (Activity.Current == null)
                {
                    var activity = new Activity("operation");
                    activity.Start();
                }
                middleware.OnHttpRequestInStart(context);
            }
            else
            {
                middleware.OnBeginRequest(context, timestamp);
            }
        }

        private void HandleRequestEnd(HttpContext context, long timestamp)
        {
            if (HostingDiagnosticListener.IsAspNetCore20)
            {
                middleware.OnHttpRequestInStop(context);
            }
            else
            {
                middleware.OnEndRequest(context, timestamp);
            }
        }

        public void Dispose()
        {
            while (Activity.Current != null)
            {
                Activity.Current.Stop();
            }
        }
    }
}
