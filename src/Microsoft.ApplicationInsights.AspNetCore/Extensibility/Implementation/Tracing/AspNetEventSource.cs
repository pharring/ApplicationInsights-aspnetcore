﻿//-----------------------------------------------------------------------
// <copyright file="AspNetCoreEventSource.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
namespace Microsoft.ApplicationInsights.AspNetCore.Extensibility.Implementation.Tracing
{
    using System;
    using System.Diagnostics.Tracing;

    /// <summary>
    /// Event source for Application Insights ASP.NET Core SDK.
    /// </summary>
    [EventSource(Name = "Microsoft-ApplicationInsights-AspNetCore")]
    internal sealed class AspNetCoreEventSource : EventSource
    {
        /// <summary>
        /// The singleton instance of this event source.
        /// Due to how EventSource initialization works this has to be a public field and not
        /// a property otherwise the internal state of the event source will not be enabled.
        /// </summary>
        public static readonly AspNetCoreEventSource Instance = new AspNetCoreEventSource();

        /// <summary>
        /// Prevents a default instance of the <see cref="AspNetCoreEventSource"/> class from being created.
        /// </summary>
        private AspNetCoreEventSource() : base()
        {
            try
            {                
                this.ApplicationName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;
            }
            catch (Exception exp)
            {
                this.ApplicationName = "Undefined " + exp.Message;
            }
        }

        /// <summary>
        /// Gets the application name for use in logging events.
        /// </summary>
        public string ApplicationName { [NonEvent] get; [NonEvent]private set; }

        /// <summary>
        /// Logs an event for the an exception in the TelemetryInitializerBase Initialize method.
        /// </summary>
        /// <param name="errorMessage">The error message to write an event for.</param>
        /// <param name="appDomainName">An ignored placeholder to make EventSource happy.</param>
        [Event(1, Message = "{0}", Level = EventLevel.Error, Keywords = Keywords.Diagnostics)]
        public void LogTelemetryInitializerBaseInitializeException(string errorMessage, string appDomainName = "Incorrect")
        {
            this.WriteEvent(1, errorMessage, this.ApplicationName);
        }

        /// <summary>
        /// Logs an event for the TelemetryInitializerBase Initialize method when the HttpContext is null.
        /// </summary>
        /// <param name="appDomainName">An ignored placeholder to make EventSource happy.</param>
        [Event(2, Message = "TelemetryInitializerBase.Initialize - httpContextAccessor.HttpContext is null, returning.", Level = EventLevel.Warning, Keywords = Keywords.Diagnostics)]
        public void LogTelemetryInitializerBaseInitializeContextNull(string appDomainName = "Incorrect")
        {
            this.WriteEvent(2, this.ApplicationName);
        }

        /// <summary>
        /// Logs an event for the TelemetryInitializerBase Initialize method when RequestServices is null.
        /// </summary>
        /// <param name="appDomainName">An ignored placeholder to make EventSource happy.</param>
        [Event(3, Message = "TelemetryInitializerBase.Initialize - context.RequestServices is null, returning.", Level = EventLevel.Warning, Keywords = Keywords.Diagnostics)]
        public void LogTelemetryInitializerBaseInitializeRequestServicesNull(string appDomainName = "Incorrect")
        {
            this.WriteEvent(3, this.ApplicationName);
        }

        /// <summary>
        /// Logs an event for the TelemetryInitializerBase Initialize method when the request is null.
        /// </summary>
        /// <param name="appDomainName">An ignored placeholder to make EventSource happy.</param>
        [Event(4, Message = "TelemetryInitializerBase.Initialize - request is null, returning.", Level = EventLevel.Warning, Keywords = Keywords.Diagnostics)]
        public void LogTelemetryInitializerBaseInitializeRequestNull(string appDomainName = "Incorrect")
        {
            this.WriteEvent(4, this.ApplicationName);
        }

        /// <summary>
        /// Logs an event for the ClientIpHeaderTelemetryInitializer OnInitializeTelemetry method when the location IP is null.
        /// </summary>
        /// <param name="appDomainName">An ignored placeholder to make EventSource happy.</param>
        [Event(5, Message = "ClientIpHeaderTelemetryInitializer.OnInitializeTelemetry - telemetry.Context.Location.Ip is already set, returning.", Level = EventLevel.Warning, Keywords = Keywords.Diagnostics)]
        public void LogClientIpHeaderTelemetryInitializerOnInitializeTelemetryIpNull(string appDomainName = "Incorrect")
        {
            this.WriteEvent(5, this.ApplicationName);
        }

        /// <summary>
        /// Logs an event for the WebSessionTelemetryInitializer OnInitializeTelemetry method when the session Id is null.
        /// </summary>
        /// <param name="appDomainName">An ignored placeholder to make EventSource happy.</param>
        [Event(6, Message = "WebSessionTelemetryInitializer.OnInitializeTelemetry - telemetry.Context.Session.Id is null or empty, returning.", Level = EventLevel.Warning, Keywords = Keywords.Diagnostics)]
        public void LogWebSessionTelemetryInitializerOnInitializeTelemetrySessionIdNull(string appDomainName = "Incorrect")
        {
            this.WriteEvent(6, this.ApplicationName);
        }

        /// <summary>
        /// Logs an event for the WebUserTelemetryInitializer OnInitializeTelemetry method when the session Id is null.
        /// </summary>
        /// <param name="appDomainName">An ignored placeholder to make EventSource happy.</param>
        [Event(7, Message = "WebUserTelemetryInitializer.OnInitializeTelemetry - telemetry.Context.Session.Id is null or empty, returning.", Level = EventLevel.Warning, Keywords = Keywords.Diagnostics)]
        public void LogWebUserTelemetryInitializerOnInitializeTelemetrySessionIdNull(string appDomainName = "Incorrect")
        {
            this.WriteEvent(7, this.ApplicationName);
        }

        [Event(8, Message = "Failed to retrieve App ID for the current application insights resource. Make sure the configured instrumentation key is valid. Error: {0}", Level = EventLevel.Warning, Keywords = Keywords.Diagnostics)]
        public void LogFetchAppIdFailed(string exception, string appDomainName = "Incorrect")
        {
            this.WriteEvent(8, exception, this.ApplicationName);
        }

        /// <summary>
        /// Logs an event for the HostingDiagnosticListener OnHttpRequestInStart method when the current activity is null.
        /// </summary>
        /// <param name="appDomainName">An ignored placeholder to make EventSource happy.</param>
        [Event(9, Message = "HostingDiagnosticListener.OnHttpRequestInStart - Activity.Current is null, returning.", Level = EventLevel.Warning, Keywords = Keywords.Diagnostics)]
        public void LogHostingDiagnosticListenerOnHttpRequestInStartActivityNull(string appDomainName = "Incorrect")
        {
            this.WriteEvent(9, this.ApplicationName);
        }

        [Event(10, Message = "Failed to retrieve App ID for the current application insights resource. Endpoint returned HttpStatusCode: {0}", Level = EventLevel.Warning, Keywords = Keywords.Diagnostics)]
        public void FetchAppIdFailedWithResponseCode(string exception, string appDomainName = "Incorrect")
        {
            this.WriteEvent(10, exception, this.ApplicationName);
        }

        [Event(11, Message = "Unable to configure module {0} as it is not found in service collection.", Level = EventLevel.Warning, Keywords = Keywords.Diagnostics)]
        public void UnableToFindModuleToConfigure(string moduleType, string appDomainName = "Incorrect")
        {
            this.WriteEvent(11, moduleType, this.ApplicationName);
        }

        [Event(12, Message = "Unable to find QuickPulseTelemetryModule in service collection. LiveMetrics feature will not be available. Please add QuickPulseTelemetryModule to services collection in the ConfigureServices method of your application Startup class.", Level = EventLevel.Error, Keywords = Keywords.Diagnostics)]
        public void UnableToFindQuickPulseModuleInDI(string appDomainName = "Incorrect")
        {
            this.WriteEvent(12, this.ApplicationName);
        }

        /// <summary>
        /// Keywords for the AspNetEventSource.
        /// </summary>
        public sealed class Keywords
        {
            /// <summary>
            /// Keyword for errors that trace at Verbose level.
            /// </summary>
            public const EventKeywords Diagnostics = (EventKeywords)0x1;
        }
    }
}
