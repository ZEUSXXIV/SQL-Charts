using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace QuerySight.Extension
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideToolWindow(typeof(QuerySightToolWindow))]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class QuerySightPackage : AsyncPackage
    {
        /// <summary>
        /// QuerySightPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "e89c6d32-eb88-466d-88cb-e6e768a4ea84";

        private ChartBridgeServer _server;

        /// <summary>
        /// Initializes a new instance of the <see cref="QuerySightPackage"/> class.
        /// </summary>
        public QuerySightPackage()
        {
        }

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // Switch to the main thread to perform initialization that requires UI-thread components (like registering commands)
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Initialize the QuerySightCommand (which allows opening the window via a menu item or shortcut)
            await QuerySightCommand.InitializeAsync(this);

            // Start the HTTP/WebSocket bridge server on port 8080
            _server = new ChartBridgeServer(8080);
            _server.ChartDataReceived += OnChartDataReceived;
            _server.Start();
            
            System.Diagnostics.Debug.WriteLine("QuerySight charting bridge server started on http://localhost:8080/chartbridge/");
        }

        /// <summary>
        /// Fires when the background bridge server receives a chart rendering JSON payload.
        /// Transitions to the UI thread, focuses the tool window, and invokes renderChart in WebView2.
        /// </summary>
        /// <param name="jsonPayload">The chart JSON payload.</param>
        private void OnChartDataReceived(string jsonPayload)
        {
            // Safely marshal back to the UI thread from the background server threads
            this.JoinableTaskFactory.RunAsync(async () =>
            {
                await this.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    // Find or create the tool window pane
                    ToolWindowPane window = this.FindToolWindow(typeof(QuerySightToolWindow), 0, true);
                    if ((null == window) || (null == window.Frame))
                    {
                        throw new NotSupportedException("Cannot create tool window");
                    }

                    // Focus/Show the tool window frame inside the SSMS docking shell
                    IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());

                    // Get the UI control instance inside the tool window and push the data
                    var control = (QuerySightToolWindowControl)window.Content;
                    control?.RenderChart(jsonPayload);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error showing or sending data to QuerySight tool window: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Cleans up resources when the package is unloaded/disposed.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_server != null)
                {
                    _server.ChartDataReceived -= OnChartDataReceived;
                    _server.Stop();
                    _server = null;
                }
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
