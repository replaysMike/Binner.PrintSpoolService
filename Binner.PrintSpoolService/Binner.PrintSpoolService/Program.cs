// See https://aka.ms/new-console-template for more information
using Binner.Common.Extensions;
using Binner.PrintSpoolService;
using Binner.Services.IO.Printing;
using Binner.Services.Printing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using Topshelf;
using Topshelf.Runtime;
using Topshelf.Runtime.DotNetCore;

Console.WriteLine("Binner Print Spool Service");

string LogManagerConfigFile = "nlog.config"; // TODO: Inject from appsettings
string _logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogManagerConfigFile);
var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();
var logManager = LogManager.Setup().LoadConfigurationFromFile(_logFile);
var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog());
var logger = logManager.GetCurrentClassLogger();

// setup service info
var displayName = typeof(PrintService).GetDisplayName();
var serviceName = displayName.Replace(" ", "");
var serviceDescription = typeof(PrintService).GetDescription();

var rc = HostFactory.Run(x =>
{
    if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
    {
        x.UseEnvironmentBuilder(target => new DotNetCoreEnvironmentBuilder(target));
    }

    //x.AddCommandLineSwitch("dbinfo", v => BinnerConsole.PrintDbInfo(configRoot, webHostConfig));
    //x.ApplyCommandLine();
    x.Service<PrintService>(s =>
    {
        s.ConstructUsing(name => new PrintService(loggerFactory));
        s.BeforeStartingService(async tc =>
        {
        });
        s.WhenStarted((tc, hostControl) => tc.Start(hostControl));
        s.WhenStopped((tc, hostControl) => tc.Stop(hostControl));
    });
    x.RunAsLocalSystem();

    x.SetDescription(serviceDescription);
    x.SetDisplayName(displayName);
    x.SetServiceName(serviceName);
    x.SetStartTimeout(TimeSpan.FromSeconds(15));
    x.SetStopTimeout(TimeSpan.FromSeconds(10));
    x.BeforeInstall(() => logger.Info($"Installing service {serviceName}..."));
    x.BeforeUninstall(() => logger.Info($"Uninstalling service {serviceName}..."));
    x.AfterInstall(() => logger.Info($"{serviceName} service installed."));
    x.AfterUninstall(() => logger.Info($"{serviceName} service uninstalled."));
    x.OnException((ex) =>
    {
        logger.Error(ex, $"{serviceName} exception thrown: {ex.Message}");
    });

    x.UnhandledExceptionPolicy = UnhandledExceptionPolicyCode.LogErrorAndStopService;
});