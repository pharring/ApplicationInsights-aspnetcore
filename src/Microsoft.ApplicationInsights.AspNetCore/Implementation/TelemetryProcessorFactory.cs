﻿namespace Microsoft.ApplicationInsights.AspNetCore
{
    using System;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// A generic factory for telemetry processors of a given type.
    /// </summary>
    internal class TelemetryProcessorFactory : ITelemetryProcessorFactory
    {
        private readonly IServiceProvider serviceProvider;
        private readonly Type telemetryProcessorType;

        /// <summary>
        /// Constructs an instance of the factory.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        /// <param name="telemetryProcessorType">The type of telemetry processor to create.</param>
        public TelemetryProcessorFactory(IServiceProvider serviceProvider, Type telemetryProcessorType)
        {
            this.serviceProvider = serviceProvider;
            this.telemetryProcessorType = telemetryProcessorType;
        }

        /// <summary>
        /// Creates an instance of the telemetry processor, passing the
        /// next <see cref="ITelemetryProcessor"/> in the call chain to
        /// its constructor.
        /// </summary>
        public ITelemetryProcessor Create(ITelemetryProcessor next)
        {
            return (ITelemetryProcessor)ActivatorUtilities.CreateInstance(this.serviceProvider, this.telemetryProcessorType, next);
        }
    }
}
