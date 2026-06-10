using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace QuerySight.Extension
{
    /// <summary>
    /// Interaction logic for QuerySightToolWindowControl.xaml
    /// </summary>
    public partial class QuerySightToolWindowControl : UserControl
    {
        private bool _isInitialized = false;

        public QuerySightToolWindowControl()
        {
            InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            try
            {
                // 1. Establish User Data Folder in LocalAppData to avoid permission errors when running inside SSMS
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string userDataFolder = Path.Combine(localAppData, "QuerySight", "WebView2");
                if (!Directory.Exists(userDataFolder))
                {
                    Directory.CreateDirectory(userDataFolder);
                }

                // 2. Create the custom CoreWebView2Environment
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webView.EnsureCoreWebView2Async(env);

                // 3. Map a virtual host name to the local directory where the extension is running.
                // This makes it easy to load files relative to the installation path securely.
                string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "querysight.local",
                    assemblyFolder,
                    CoreWebView2HostResourceAccessKind.Allow);

                // 4. Point the control to the virtual domain mapped HTML
                // This resolves to the file: assemblyFolder\Resources\chart_canvas.html
                webView.Source = new Uri("https://querysight.local/Resources/chart_canvas.html");

                webView.CoreWebView2.NavigationCompleted += (s, args) =>
                {
                    loadingPanel.Visibility = Visibility.Collapsed;
                    webView.Visibility = Visibility.Visible;
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to initialize WebView2: {ex.Message}\n\nMake sure the Microsoft Edge WebView2 Runtime is installed on this machine.",
                    "QuerySight - WebView2 Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Renders a chart inside the WebView2 context by executing the window.renderChart JavaScript function.
        /// </summary>
        /// <param name="jsonPayload">The JSON string containing the chart configurations and data.</param>
        public async void RenderChart(string jsonPayload)
        {
            try
            {
                // Ensure WebView2 is fully loaded and ready
                await webView.EnsureCoreWebView2Async();
                
                // Build a call directly to window.renderChart passing the JSON string as a native JS object literal
                string script = $"window.renderChart({jsonPayload});";
                
                // Run script asynchronously on the WebView2 thread
                await webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error executing render script inside WebView2: {ex.Message}");
            }
        }
    }
}
