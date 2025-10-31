using System;
using System.Threading;
using System.Threading.Tasks;

namespace NidarosRTT.Core.Services
{
    /// <summary>
    /// Represents the current status of the translation service
    /// </summary>
    public enum TranslationServiceStatus
    {
        Available,
        Unavailable,
        Checking
    }

    /// <summary>
    /// Event args for translation service status changes
    /// </summary>
    public class TranslationStatusChangedEventArgs : EventArgs
    {
        public TranslationServiceStatus Status { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Monitors the health of the translation service and provides status updates
    /// </summary>
    public interface ITranslationHealthMonitor
    {
        /// <summary>
        /// Gets the current status of the translation service
        /// </summary>
        TranslationServiceStatus CurrentStatus { get; }

        /// <summary>
        /// Event raised when translation service status changes
        /// </summary>
        event EventHandler<TranslationStatusChangedEventArgs> StatusChanged;

        /// <summary>
        /// Manually triggers a health check
        /// </summary>
        Task<TranslationServiceStatus> CheckHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reports a translation failure to the health monitor
        /// </summary>
        void ReportTranslationFailure();

        /// <summary>
        /// Reports a successful translation to the health monitor
        /// </summary>
        void ReportTranslationSuccess();
    }
}
