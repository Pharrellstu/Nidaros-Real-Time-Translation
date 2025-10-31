using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NidarosRTT.Core.Services
{
    /// <summary>
    /// Background service that monitors the health of the translation service
    /// Implements circuit breaker pattern to prevent cascading failures
    /// </summary>
    public class TranslationHealthMonitor : BackgroundService, ITranslationHealthMonitor
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TranslationHealthMonitor> _logger;
        private readonly string _translatorServiceUrl;
        private readonly TimeSpan _healthCheckInterval;
        private readonly TimeSpan _healthCheckTimeout;
        private readonly int _failureThreshold;
        private readonly TimeSpan _circuitBreakerDuration;

        private TranslationServiceStatus _currentStatus;
        private int _consecutiveFailures;
        private DateTime _circuitOpenedAt;
        private readonly object _statusLock = new object();

        public TranslationServiceStatus CurrentStatus
        {
            get
            {
                lock (_statusLock)
                {
                    return _currentStatus;
                }
            }
        }

        public event EventHandler<TranslationStatusChangedEventArgs>? StatusChanged;

        public TranslationHealthMonitor(
            IHttpClientFactory httpClientFactory,
            ILogger<TranslationHealthMonitor> logger,
            string translatorServiceUrl,
            int healthCheckIntervalSeconds = 45,
            int healthCheckTimeoutSeconds = 10,
            int failureThreshold = 3,
            int circuitBreakerDurationSeconds = 60)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _translatorServiceUrl = translatorServiceUrl;
            _healthCheckInterval = TimeSpan.FromSeconds(healthCheckIntervalSeconds);
            _healthCheckTimeout = TimeSpan.FromSeconds(healthCheckTimeoutSeconds);
            _failureThreshold = failureThreshold;
            _circuitBreakerDuration = TimeSpan.FromSeconds(circuitBreakerDurationSeconds);
            _currentStatus = TranslationServiceStatus.Checking;
            _consecutiveFailures = 0;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[HEALTH MONITOR] Starting translation service health monitoring...");
            _logger.LogInformation("[HEALTH MONITOR] Check interval: {Interval}s, Timeout: {Timeout}s, Failure threshold: {Threshold}",
                _healthCheckInterval.TotalSeconds, _healthCheckTimeout.TotalSeconds, _failureThreshold);

            // Wait a bit before starting to allow services to initialize
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckHealthAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[HEALTH MONITOR] Unexpected error during health check");
                }

                await Task.Delay(_healthCheckInterval, stoppingToken);
            }

            _logger.LogInformation("[HEALTH MONITOR] Health monitoring stopped.");
        }

        public async Task<TranslationServiceStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            // Check if circuit breaker is open
            lock (_statusLock)
            {
                if (_currentStatus == TranslationServiceStatus.Unavailable)
                {
                    var timeSinceOpened = DateTime.UtcNow - _circuitOpenedAt;
                    if (timeSinceOpened < _circuitBreakerDuration)
                    {
                        _logger.LogDebug("[HEALTH MONITOR] Circuit breaker is open. Time remaining: {TimeRemaining}s",
                            (_circuitBreakerDuration - timeSinceOpened).TotalSeconds);
                        return _currentStatus;
                    }
                    else
                    {
                        _logger.LogInformation("[HEALTH MONITOR] Circuit breaker duration expired. Attempting health check...");
                    }
                }
            }

            UpdateStatus(TranslationServiceStatus.Checking);

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = _healthCheckTimeout;

                _logger.LogDebug("[HEALTH MONITOR] Checking translation service health at {Url}/health", _translatorServiceUrl);

                var response = await httpClient.GetAsync($"{_translatorServiceUrl}/health", cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[HEALTH MONITOR] ✓ Translation service is healthy (HTTP {StatusCode})", (int)response.StatusCode);
                    HandleSuccess();
                    return TranslationServiceStatus.Available;
                }
                else
                {
                    _logger.LogWarning("[HEALTH MONITOR] ✗ Translation service returned unhealthy status: HTTP {StatusCode}", (int)response.StatusCode);
                    HandleFailure($"HTTP {(int)response.StatusCode}");
                    return _currentStatus;
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("[HEALTH MONITOR] ✗ Translation service health check timed out after {Timeout}s", _healthCheckTimeout.TotalSeconds);
                HandleFailure("Timeout");
                return _currentStatus;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("[HEALTH MONITOR] ✗ Translation service health check failed: {Message}", ex.Message);
                HandleFailure($"Connection error: {ex.Message}");
                return _currentStatus;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HEALTH MONITOR] ✗ Unexpected error during health check");
                HandleFailure($"Error: {ex.Message}");
                return _currentStatus;
            }
        }

        public void ReportTranslationFailure()
        {
            _logger.LogWarning("[HEALTH MONITOR] Translation failure reported by worker");
            HandleFailure("Worker reported failure");
        }

        public void ReportTranslationSuccess()
        {
            lock (_statusLock)
            {
                if (_currentStatus == TranslationServiceStatus.Available)
                {
                    // Already in good state, reset failure counter
                    if (_consecutiveFailures > 0)
                    {
                        _consecutiveFailures = 0;
                    }
                }
                else
                {
                    // Service was down but now working, mark as available
                    HandleSuccess();
                }
            }
        }

        private void HandleSuccess()
        {
            lock (_statusLock)
            {
                _consecutiveFailures = 0;

                if (_currentStatus != TranslationServiceStatus.Available)
                {
                    _currentStatus = TranslationServiceStatus.Available;
                    _logger.LogInformation("[HEALTH MONITOR] 🟢 Translation service status changed to AVAILABLE");
                    OnStatusChanged(new TranslationStatusChangedEventArgs
                    {
                        Status = TranslationServiceStatus.Available,
                        Timestamp = DateTime.UtcNow,
                        Message = "Translation service recovered"
                    });
                }
            }
        }

        private void HandleFailure(string reason)
        {
            lock (_statusLock)
            {
                _consecutiveFailures++;
                _logger.LogWarning("[HEALTH MONITOR] Consecutive failures: {Count}/{Threshold}. Reason: {Reason}",
                    _consecutiveFailures, _failureThreshold, reason);

                if (_consecutiveFailures >= _failureThreshold && _currentStatus != TranslationServiceStatus.Unavailable)
                {
                    _currentStatus = TranslationServiceStatus.Unavailable;
                    _circuitOpenedAt = DateTime.UtcNow;
                    _logger.LogError("[HEALTH MONITOR] 🔴 Translation service status changed to UNAVAILABLE. Circuit breaker OPEN for {Duration}s",
                        _circuitBreakerDuration.TotalSeconds);
                    OnStatusChanged(new TranslationStatusChangedEventArgs
                    {
                        Status = TranslationServiceStatus.Unavailable,
                        Timestamp = DateTime.UtcNow,
                        Message = $"Translation service unavailable after {_failureThreshold} consecutive failures"
                    });
                }
            }
        }

        private void UpdateStatus(TranslationServiceStatus status)
        {
            lock (_statusLock)
            {
                _currentStatus = status;
            }
        }

        protected virtual void OnStatusChanged(TranslationStatusChangedEventArgs e)
        {
            StatusChanged?.Invoke(this, e);
        }
    }
}
