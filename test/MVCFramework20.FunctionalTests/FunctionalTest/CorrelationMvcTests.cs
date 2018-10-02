﻿namespace MVCFramework20.FunctionalTests.FunctionalTest
{
    using FunctionalTestUtils;
    using Microsoft.ApplicationInsights.DataContracts;
    using System;
    using System.Linq;
    using System.Net.Http;
    using AI;
    using Xunit;
    using Xunit.Abstractions;

    public class CorrelationMvcTests : TelemetryTestsBase
    {
        private const string assemblyName = "MVCFramework20.FunctionalTests20";

        public CorrelationMvcTests(ITestOutputHelper output) : base(output)
        {
        }


        [Fact]
        public void CorrelationInfoIsPropagatedToDependendedService()
        {
#if netcoreapp2_0 // Correlation works on .Net core.
            using (var server = new InProcessServer(assemblyName, this.output))
            {
                using (var httpClient = new HttpClient())
                {
                    var task = httpClient.GetAsync(server.BaseHost + "/");
                    task.Wait(TestTimeoutMs);
                }

                var actual = server.Execute<Envelope>(() => server.Listener.ReceiveItems(2, TestListenerTimeoutInMs));
                this.DebugTelemetryItems(actual);

                var dependencyTelemetry = actual.OfType<TelemetryItem<RemoteDependencyData>>().FirstOrDefault();
                Assert.NotNull(dependencyTelemetry);                         

                var requestTelemetry = actual.OfType<TelemetryItem<RequestData>>().FirstOrDefault();
                Assert.NotNull(requestTelemetry);

                Assert.Equal(requestTelemetry.tags["ai.operation.id"], dependencyTelemetry.tags["ai.operation.id"]);
                Assert.Contains(dependencyTelemetry.tags["ai.operation.id"], requestTelemetry.tags["ai.operation.parentId"]);               
            }
#endif
        }
    }
}
