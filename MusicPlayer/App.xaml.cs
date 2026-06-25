using System.IO;
using System.IO.Pipes;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace MusicPlayer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : WpfApplication
    {
        private const string SingleInstanceMutexName = @"Local\MusicPlayer.SingleInstance";
        private const string SingleInstancePipeName = "MusicPlayer.SingleInstancePipe";
        private Mutex? singleInstanceMutex;
        private CancellationTokenSource? pipeCancellation;

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                PerfLog.Mark("SingleInstance duplicate startup");
                NotifyExistingInstance();
                Shutdown();
                return;
            }

            base.OnStartup(e);

            pipeCancellation = new CancellationTokenSource();
            _ = ListenForInstanceRequestsAsync(pipeCancellation.Token);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            pipeCancellation?.Cancel();
            pipeCancellation?.Dispose();
            singleInstanceMutex?.ReleaseMutex();
            singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }

        private async Task ListenForInstanceRequestsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        SingleInstancePipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cancellationToken);
                    using var reader = new StreamReader(server);
                    var message = await reader.ReadLineAsync(cancellationToken);
                    if (string.Equals(message, "show", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            if (MainWindow is MainWindow mainWindow)
                            {
                                mainWindow.ShowExistingInstance();
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    PerfLog.Exception("SingleInstance pipe listener", ex);
                    await Task.Delay(500, cancellationToken).ContinueWith(_ => { }, TaskScheduler.Default);
                }
            }
        }

        private static void NotifyExistingInstance()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", SingleInstancePipeName, PipeDirection.Out);
                client.Connect(700);
                using var writer = new StreamWriter(client)
                {
                    AutoFlush = true
                };
                writer.WriteLine("show");
            }
            catch (Exception ex)
            {
                PerfLog.Exception("SingleInstance notify existing", ex);
            }
        }

        private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            PerfLog.Exception("DispatcherUnhandledException", e.Exception);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                PerfLog.Exception($"UnhandledException isTerminating={e.IsTerminating}", exception);
            }
            else
            {
                PerfLog.Mark($"UnhandledException isTerminating={e.IsTerminating}: {e.ExceptionObject}");
            }
        }

        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            PerfLog.Exception("UnobservedTaskException", e.Exception);
        }
    }
}
