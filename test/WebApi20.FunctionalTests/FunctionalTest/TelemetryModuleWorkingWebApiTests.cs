﻿namespace WebApi20.FunctionalTests.FunctionalTest
{
    using FunctionalTestUtils;
    using System;
    using System.Diagnostics;
    using System.Linq;
    using Microsoft.ApplicationInsights.AspNetCore;
    using Microsoft.ApplicationInsights.DependencyCollector;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.W3C;
    using Microsoft.Extensions.DependencyInjection;
    using Xunit;
    using Xunit.Abstractions;

#pragma warning disable 612, 618
    public class TelemetryModuleWorkingWebApiTests : TelemetryTestsBase, IDisposable
    {
        private const string assemblyName = "WebApi20.FunctionalTests20";
        public TelemetryModuleWorkingWebApiTests(ITestOutputHelper output) : base (output)
        {
        }

        // The NET451 conditional check is wrapped inside the test to make the tests visible in the test explorer. We can move them to the class level once if the issue is resolved.

        [Fact]
        public void TestBasicDependencyPropertiesAfterRequestingBasicPage()
        {
            const string RequestPath = "/api/values";

            using (var server = new InProcessServer(assemblyName, this.output))
            {
                DependencyTelemetry expected = new DependencyTelemetry();
                expected.ResultCode = "200";
                expected.Success = true;
                expected.Name = "GET " + RequestPath;
                expected.Data = server.BaseHost + RequestPath;

                this.ValidateBasicDependency(server, RequestPath, expected);
            }
        }

        [Fact]
        public void TestDependencyAndRequestWithW3CStandard()
        {
            const string RequestPath = "/api/values";

            using (var server = new InProcessServer(assemblyName, this.output, builder =>
            {
                return builder.ConfigureServices(
                    services =>
                    {
                        services.AddApplicationInsightsTelemetry(
                            o => o.RequestCollectionOptions.EnableW3CDistributedTracing = true);

                        // enable headers injection on localhost
                        var dependencyModuleConfigFactoryDescriptor = services.Where(sd => sd.ServiceType == typeof(ITelemetryModuleConfigurator));
                        services.Remove(dependencyModuleConfigFactoryDescriptor.First());

                        services.ConfigureTelemetryModule<DependencyTrackingTelemetryModule>((module, o) =>
                        {
                            module.EnableW3CHeadersInjection = true;
                        });
                    });
            }))
            {
                DependencyTelemetry expected = new DependencyTelemetry
                {
                    ResultCode = "200",
                    Success = true,
                    Name = "GET " + RequestPath,
                    Data = server.BaseHost + RequestPath
                };

                var activity = new Activity("dummy")
                    .Start();

                var (request, dependency) = this.ValidateBasicDependency(server, RequestPath, expected);
                string expectedTraceId = activity.GetTraceId();
                string expectedParentSpanId = activity.GetSpanId();

                Assert.Equal(expectedTraceId, request.tags["ai.operation.id"]);
                Assert.Equal(expectedTraceId, dependency.tags["ai.operation.id"]);
                Assert.Equal($"|{expectedTraceId}.{expectedParentSpanId}.", dependency.tags["ai.operation.parentId"]);
            }
        }

        [Fact]
        public void TestIfPerformanceCountersAreCollected()
        {
#if NET451 || NET461
            this.output.WriteLine("Validating perfcounters");
            ValidatePerformanceCountersAreCollected(assemblyName);
#endif
        }

        public void Dispose()
        {
            while (Activity.Current != null)
            {
                Activity.Current.Stop();
            }
        }
    }
#pragma warning restore 612, 618
}
