using Binner.Common;
using Binner.Model;
using Binner.Model.Configuration;
using Binner.Model.IO.Printing;
using Binner.Model.Responses;
using Binner.Services.IO.Printing;
using Binner.Services.Printing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Topshelf;

namespace Binner.PrintSpoolService
{
    [Description("Hosts the Binner Print Spool Service for printing labels")]
    [DisplayName("Binner Print Spool Service")]
    public class PrintService : ServiceControl, IDisposable
    {
        private const int PollIntervalMillisecondsDefault = 5000;
        private const string EventHubName = "/hubs/printHub";
        private static readonly string _configFile = EnvironmentVarConstants.GetEnvOrDefault(EnvironmentVarConstants.Config, AppConstants.AppSettings);
        private static readonly string _logManagerConfigFile = EnvironmentVarConstants.GetEnvOrDefault(EnvironmentVarConstants.NlogConfig, AppConstants.NLogConfig);

        private bool _isDisposed;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private ILoggerFactory _loggerFactory;
        private ILogger _logger;
        private readonly ManualResetEvent _closingEvent = new ManualResetEvent(false);
        private readonly AutoResetEvent _messageReceivedEvent = new AutoResetEvent(false);
        private readonly HttpClientHandler _httpClientHandler;
        private readonly HttpClient _httpClient;
        private JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // minimize whitespace
            WriteIndented = false,
        };
        private PrintConfiguration _config = new PrintConfiguration();
        private readonly ILabelGenerator _labelGenerator;
        private readonly IBarcodeGenerator _barcodeGenerator;
        private IPrinterSettings? _printerConfiguration;
        private Version _version = new Version(1, 0, 0);
        private TimeSpan _internalQueueInterval = TimeSpan.FromMilliseconds(100);
        private TimeSpan _pollInterval = TimeSpan.FromMilliseconds(PollIntervalMillisecondsDefault);
        private int _errorCount;
        private HubConnection? _connection;
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();

        public PrintService(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger(nameof(PrintService));
            _version = Assembly.GetExecutingAssembly()?.GetName()?.Version ?? new Version(1, 0, 0);
            _httpClientHandler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true,
            };
            _httpClient = CreateHttpClient();
            // setup services
            _barcodeGenerator = new BarcodeGenerator();
            _labelGenerator = new LabelGenerator();
        }

        public bool Start(HostControl hostControl)
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            RunStartAsync(hostControl);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            return true;
        }

        public bool Stop(HostControl hostControl)
        {
            ShutdownHost();
            return true;
        }

        private async Task RunStartAsync(HostControl hostControl)
        {
            try
            {
                var configPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName) ?? string.Empty;
                IConfiguration configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile(_configFile, optional: false, reloadOnChange: true)
                    .Build();
                _config = configuration.GetSection(nameof(PrintConfiguration)).Get<PrintConfiguration>() ?? throw new Exception($"Failed to load configuration section named '{nameof(PrintConfiguration)}'");

                var serviceThread = new Thread(new ThreadStart(PrintServiceThread));
                serviceThread.Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal unhandled exception!");
            }
        }

        private bool IsMessagingConnected() => _connection?.State == HubConnectionState.Connected;

        private async Task InitSignalRAsync()
        {
            _connection = new HubConnectionBuilder()
                    .WithUrl($"{_config.PublicUrl}{EventHubName}", options =>
                    {
                        //options.AccessTokenProvider = () => Task.FromResult((string?)new Guid("e7c1f638-66c1-4ca3-9930-f49a37289028").ToString());
                        options.Credentials = System.Net.CredentialCache.DefaultCredentials;
                        options.HttpMessageHandlerFactory = (message) =>
                        {
                            if (message is HttpClientHandler clientHandler)
                                // ignore SSL errors
                                clientHandler.ServerCertificateCustomValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                            return message;
                        };
                    })
                    .WithAutomaticReconnect([TimeSpan.FromSeconds(1)])
                    .Build();
            _connection.Reconnecting += error =>
            {
                _logger.LogInformation($"Connection to Binner lost. Attempting to reconnect... {error?.Message}");
                return Task.CompletedTask;
            };
            _connection.Reconnected += connectionId =>
            {
                _logger.LogInformation($"Connection to Binner successfullly reconnected. ConnectionId: {connectionId}");
                _connection.SendAsync("SubscribePrint", _config.PrintSpoolQueueId);
                _messageReceivedEvent.Set(); // check the print queue immediately on reconnect to see if we have missed jobs
                return Task.CompletedTask;
            };
            _connection.Closed += error =>
            {
                _logger.LogInformation($"Connection to Binner closed permanently. {error?.Message}");
                return Task.CompletedTask;
            };
            _connection.On("PrintQueued", [typeof(Guid)], async (args) =>
            {
                var printSpoolQueueId = (Guid?)args[0];
                _logger.LogInformation($"Received print queue for '{printSpoolQueueId}'!");
                _messageReceivedEvent.Set();
            });
            await ConnectWithRetryAsync(_connection, _tokenSource.Token);
        }

        private async Task<bool> ConnectWithRetryAsync(HubConnection connection, CancellationToken token)
        {
            // Keep trying to until we can start or the token is canceled.
            while (true)
            {
                try
                {
                    await connection.StartAsync(token);
                    Debug.Assert(connection.State == HubConnectionState.Connected);
                    _logger.LogInformation("Connected to Binner!");
                    await connection.SendAsync("SubscribePrint", _config.PrintSpoolQueueId);
                    return true;
                }
                catch when (token.IsCancellationRequested)
                {
                    return false;
                }
                catch
                {
                    // Failed to connect, trying again in 5000 ms.
                    _logger.LogInformation("Failed to connect to Binner.");
                    Debug.Assert(connection.State == HubConnectionState.Disconnected);
                    await Task.Delay(5000, token);
                }
            }
        }

        private HttpClient CreateHttpClient()
        {
            var httpClient = new HttpClient(_httpClientHandler);
            httpClient.DefaultRequestHeaders.UserAgent.Clear();
            httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("BinnerPrintSpoolService", _version.ToString(3)));
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true, MustRevalidate = true };
            httpClient.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            return httpClient;
        }

        private void PrintServiceThread()
        {
            try
            {
                var isStartup = true;
                _logger.LogInformation($"Binner Print Spool Service {_version.ToString(3)} started!");

                // start connecting to websocket to receive events
                AsyncHelper.RunSync(InitSignalRAsync);

                while (!_closingEvent.WaitOne(_internalQueueInterval))
                {
                    // don't enable polling if we are connected to the Binner server
                    if (isStartup)
                    {
                        // poll for anything in the queue once on startup
                        isStartup = false;
                    }
                    else
                    {
                        if (IsMessagingConnected())
                        {
                            // if a message wasn't received, continue waiting
                            if (!_messageReceivedEvent.WaitOne(10))
                                continue;
                        }
                        else
                        {
                            // poll on the poll interval
                            _closingEvent.WaitOne(_pollInterval);
                        }
                    }

                    AsyncHelper.RunSync(async () =>
                    {
                        // contact server, ask for pending print jobs
                        var pendingItems = await GetPendingQueueAsync();

                        if (pendingItems.Queue.Any())
                        {
                            // fetch the printer configuration before printing
                            var url = new Uri($"{_config.PublicUrl}/api/PrintSpoolQueue/configuration?printSpoolQueueId={_config.PrintSpoolQueueId}");
                            var response = await _httpClient.GetAsync(url);
                            ResetErrorCount();
                            if (response.IsSuccessStatusCode)
                            {
                                var responseJson = await response.Content.ReadAsStringAsync();
                                var printerConfigurationResponse = JsonSerializer.Deserialize<PrinterConfigurationResponse?>(responseJson);
                                if (printerConfigurationResponse == null) throw new InvalidOperationException($"Failed to deserialize printer configuration!");
                                _printerConfiguration = printerConfigurationResponse.PrinterConfiguration;
                            }

                            if (_printerConfiguration != null)
                            {
                                _logger.LogInformation($"Printing {pendingItems.Queue.Count} pending print items.");
                                // foreach print job, print
                                foreach (var printItem in pendingItems.Queue)
                                {
                                    var isSuccess = await PrintItemAsync(printItem, _printerConfiguration);
                                    if (isSuccess)
                                    {
                                        // mark queue item as complete
                                        await DeleteAsync(printItem);
                                    }
                                }
                            }
                            else
                            {
                                _logger.LogError($"No printer configuration is available!");
                            }
                        }
                    });
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"Failed to fetch print queue due to request exception. Will retry.");
                _errorCount++;
                HandleBackoffInterval();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Fatal exception processing the print queue.");
            }
            _logger.LogInformation($"Binner Print Spool Service shut down!");
        }

        private async Task<bool> PrintItemAsync(PrintSpoolQueue item, IPrinterSettings printerSettings)
        {
            _logger.LogTrace($"Printing job '{item.GlobalId}'.");

            // deserialize the JSON to determine the print contents of the label
            var part = JsonSerializer.Deserialize<Part?>(item.Json, _jsonSerializerOptions);
            if (part == null) throw new InvalidOperationException($"Failed to deserialize part!");

            var labelPrinter = new DymoLabelPrinterHardware(_loggerFactory, _barcodeGenerator, printerSettings);
            if (!string.IsNullOrEmpty(item.LabelJson))
            {
                // print with a template
                var template = JsonSerializer.Deserialize<LabelTemplate>(item.TemplateJson, _jsonSerializerOptions);
                if (template == null) throw new InvalidOperationException($"Failed to deserialize print template!");

                var label = JsonSerializer.Deserialize<Label>(item.LabelJson, _jsonSerializerOptions);
                if (label == null) throw new InvalidOperationException($"Failed to deserialize label!");

                var image = _labelGenerator.CreateLabelImage(label, part);
                var stream = new MemoryStream();
                await image.SaveAsPngAsync(stream);
                stream.Seek(0, SeekOrigin.Begin);

                try
                {
                    labelPrinter.PrintLabelImage(image, new PrinterOptions((LabelSource)(template.LabelPaperSource), template.Name, false));
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to print label '{template.Name}'.");
                }
            }
            else
            {
                // print without a template
                try
                {
                    var image = labelPrinter.PrintLabel(new LabelContent { Part = part }, new PrinterOptions(false));
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to print legacy label.");
                }
            }

            return false;
        }

        private async Task<PrintSpoolQueueResponse> GetPendingQueueAsync()
        {
            try
            {
                // contact server and ask for pending items
                var url = new Uri($"{_config.PublicUrl}/api/printspoolqueue/?printSpoolQueueId={_config.PrintSpoolQueueId}&cacheKill={DateTime.UtcNow.Ticks}");
                var response = await _httpClient.GetAsync(url);
                ResetErrorCount();
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    //_logger.LogTrace($"Response: {responseJson}");
                    try
                    {
                        var results = JsonSerializer.Deserialize<PrintSpoolQueueResponse>(responseJson, _jsonSerializerOptions);
                        if (results == null) return new PrintSpoolQueueResponse();
                        return results;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to deserialize server response. Response: {responseJson}");
                    }
                }
                else
                {
                    _logger.LogTrace($"Failed to fetch the print queue. Status code: {response.StatusCode}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"Failed to fetch print queue due to request exception. Will retry.");
                _errorCount++;
                HandleBackoffInterval();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to fetch print queue due to unhandled exception.");
            }
            return new PrintSpoolQueueResponse();
        }

        private async Task DeleteAsync(PrintSpoolQueue item)
        {
            try
            {
                // contact server and ask for pending items
                var url = new Uri($"{_config.PublicUrl}/api/PrintSpoolQueue?printSpoolQueueId={_config.PrintSpoolQueueId}&globalId={item.GlobalId}");
                var response = await _httpClient.DeleteAsync(url);
                ResetErrorCount();
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogTrace($"Print job '{item.GlobalId}' removed from the print queue.");
                    return;
                }
                else
                {
                    _logger.LogTrace($"Failed to remove print job '{item.GlobalId}' from the print queue. Status code: {response.StatusCode}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"Failed to fetch print queue due to request exception. Will retry.");
                _errorCount++;
                HandleBackoffInterval();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to remove print job '{item.GlobalId}' from the queue due to unhandled exception.");
            }
        }

        private void HandleBackoffInterval()
        {
            if (_errorCount > 20)
                _pollInterval = TimeSpan.FromSeconds(60);
            else if (_errorCount > 5)
                _pollInterval = TimeSpan.FromSeconds(30);
        }

        private void ResetErrorCount()
        {
            if (_errorCount > 0)
            {
                _errorCount = 0;
                _pollInterval = TimeSpan.FromMilliseconds(PollIntervalMillisecondsDefault);
            }
        }

        /// <summary>
        /// Shuts down the current web host
        /// </summary>
        private void ShutdownHost()
        {
            try
            {
                _cancellationTokenSource.Cancel();
                Dispose();
            }
            catch (Exception ex)
            {
                //_logger?.LogError(ex, "Failed to shutdown WebHost!");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            if (_isDisposed)
                return;

            if (isDisposing)
            {
                try
                {
                    _connection?.SendAsync("UnsubscribePrint", _config.PrintSpoolQueueId);
                }
                catch (Exception) { }
                try
                {
                    _closingEvent.Set();
                }
                catch (Exception) { }
            }
            _isDisposed = true;
        }
    }
}
