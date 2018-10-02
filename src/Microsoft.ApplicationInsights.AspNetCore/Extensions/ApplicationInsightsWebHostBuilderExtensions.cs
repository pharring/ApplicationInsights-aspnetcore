﻿namespace Microsoft.AspNetCore.Hosting
{
    using Microsoft.ApplicationInsights.AspNetCore.Extensions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Extension methods for <see cref="IWebHostBuilder"/> that allow adding Application Insights services to application.
    /// </summary>
    public static class ApplicationInsightsWebHostBuilderExtensions
    {
        /// <summary>
        /// Configures <see cref="IWebHostBuilder"/> to use Application Insights services.
        /// </summary>
        /// <param name="webHostBuilder">The <see cref="IWebHostBuilder"/> instance.</param>
        /// <returns>The <see cref="IWebHostBuilder"/>.</returns>
        public static IWebHostBuilder UseApplicationInsights(this IWebHostBuilder webHostBuilder)
        {
            webHostBuilder.ConfigureServices(collection =>
            {
                collection.AddApplicationInsightsTelemetry();                
            });

            return webHostBuilder;
        }

        /// <summary>
        /// Configures <see cref="IWebHostBuilder"/> to use Application Insights services.
        /// </summary>
        /// <param name="webHostBuilder">The <see cref="IWebHostBuilder"/> instance.</param>
        /// <param name="instrumentationKey">Instrumentation key to use for telemetry.</param>
        /// <returns>The <see cref="IWebHostBuilder"/>.</returns>
        public static IWebHostBuilder UseApplicationInsights(this IWebHostBuilder webHostBuilder, string instrumentationKey)
        {
            webHostBuilder.ConfigureServices(collection => collection.AddApplicationInsightsTelemetry(instrumentationKey));
            return webHostBuilder;
        }
    }
}
