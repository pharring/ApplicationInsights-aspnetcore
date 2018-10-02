﻿using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.AspNetCore.Logging
{
    /// <summary>
    /// <see cref="ApplicationInsightsLoggerOptions"/> defines the custom behavior of the tracing information sent to Application Insights.
    /// </summary>
    public class ApplicationInsightsLoggerOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInsightsLoggerOptions" /> class.
        /// Application Insights logger options can configure how <see cref="ILogger"/> behaves when sending telemetry.
        /// </summary>
        public ApplicationInsightsLoggerOptions()
        {
            TrackExceptionsAsExceptionTelemetry = true;
        }

        /// <summary>
        /// Gets or sets a value whether to track exceptions as <see cref="ExceptionTelemetry"/>.
        /// </summary>
        public bool TrackExceptionsAsExceptionTelemetry
        { get; set; }

        /// <summary>
        /// Gets or sets value indicating, whether EventId and EventName properties should be included in telemetry.
        /// </summary>
        public bool IncludeEventId { get; set; }
    }
}