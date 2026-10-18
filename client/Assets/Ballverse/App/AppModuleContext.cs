using System;

namespace Ballverse.App
{
    /// <summary>What a module receives when it starts: the sealed service registry and the error sink.</summary>
    public sealed class AppModuleContext
    {
        public AppModuleContext(AppServices services, Action<string, Exception> reportError)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            ReportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        }

        public AppServices Services { get; }

        /// <summary>Reports a non-fatal error with a short context string (goes to the log and, later, crash reporting).</summary>
        public Action<string, Exception> ReportError { get; }
    }
}
