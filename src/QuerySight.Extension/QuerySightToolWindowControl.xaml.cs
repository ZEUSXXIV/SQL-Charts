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

        private void BtnToggleSql_Click(object sender, RoutedEventArgs e)
        {
            sqlPanel.Visibility = sqlPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void BtnRunChart_Click(object sender, RoutedEventArgs e)
        {
            string query = txtSqlScript.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                ShowStatus("Error: Please write a SQL query first.", false);
                return;
            }

            string chartType = (comboChartType.SelectedItem as ComboBoxItem)?.Content as string ?? "bar";
            ShowStatus("Connecting and running query...", true);
            btnRunChart.IsEnabled = false;

            try
            {
                // Retrieve connection string from active SSMS window via reflection
                string connString = GetActiveConnectionString();
                if (string.IsNullOrEmpty(connString))
                {
                    ShowStatus("Error: No active SSMS connection found. Open a connected query tab first.", false);
                    btnRunChart.IsEnabled = true;
                    return;
                }

                // Run SQL query on background thread
                string jsonPayload = await System.Threading.Tasks.Task.Run(() =>
                {
                    return ExecuteQueryAndSerialize(connString, query, chartType);
                });

                // Render chart inside WebView2
                RenderChart(jsonPayload);
                ShowStatus("Chart generated successfully!", true);
            }
            catch (Exception ex)
            {
                ShowStatus($"Error: {ex.GetBaseException().Message}", false);
            }
            finally
            {
                btnRunChart.IsEnabled = true;
            }
        }

        private void ShowStatus(string message, bool isSuccess)
        {
            txtStatus.Text = message;
            txtStatus.Foreground = isSuccess ? 
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81)) : // Emerald Green
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));   // Rose Red
        }

        private static string ExecuteQueryAndSerialize(string connectionString, string query, string chartType)
        {
            using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new System.Data.SqlClient.SqlCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        var rows = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
                        while (reader.Read())
                        {
                            var row = new System.Collections.Generic.Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                string name = reader.GetName(i);
                                object val = reader.GetValue(i);
                                if (val == DBNull.Value)
                                {
                                    val = null;
                                }
                                row[name] = val;
                            }
                            rows.Add(row);
                        }

                        if (rows.Count == 0)
                        {
                            throw new Exception("Query returned 0 rows. Cannot render chart.");
                        }

                        var payload = new
                        {
                            chart_type = chartType,
                            title = "Direct SQL Chart",
                            data = rows
                        };

                        return Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                    }
                }
            }
        }

        private static string GetActiveConnectionString()
        {
            object connectionInfo = GetActiveConnectionInfo();
            if (connectionInfo == null) return null;

            try
            {
                // Get UIConnectionInfo property
                var uiConnProp = connectionInfo.GetType().GetProperty("UIConnectionInfo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (uiConnProp == null) return null;

                object uiConn = uiConnProp.GetValue(connectionInfo);
                if (uiConn == null) return null;

                // Get properties
                var serverNameProp = uiConn.GetType().GetProperty("ServerName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var userNameProp = uiConn.GetType().GetProperty("UserName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var useIntegratedSecurityProp = uiConn.GetType().GetProperty("UseIntegratedSecurity", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var passwordProp = uiConn.GetType().GetProperty("Password", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                string serverName = serverNameProp?.GetValue(uiConn) as string;
                string userName = userNameProp?.GetValue(uiConn) as string;
                bool useIntegratedSecurity = (bool)(useIntegratedSecurityProp?.GetValue(uiConn) ?? true);
                string password = passwordProp?.GetValue(uiConn) as string;

                // Advanced Options (for database name)
                string databaseName = null;
                var advancedOptionsProp = uiConn.GetType().GetProperty("AdvancedOptions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (advancedOptionsProp != null)
                {
                    var advancedOptions = advancedOptionsProp.GetValue(uiConn) as System.Collections.IDictionary;
                    if (advancedOptions != null && advancedOptions.Contains("DATABASE"))
                    {
                        databaseName = advancedOptions["DATABASE"] as string;
                    }
                }

                var builder = new System.Data.SqlClient.SqlConnectionStringBuilder();
                builder.DataSource = serverName;
                if (useIntegratedSecurity)
                {
                    builder.IntegratedSecurity = true;
                }
                else
                {
                    builder.UserID = userName;
                    builder.Password = password;
                }

                if (!string.IsNullOrEmpty(databaseName))
                {
                    builder.InitialCatalog = databaseName;
                }

                return builder.ConnectionString;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error building connection string: " + ex.Message);
            }
            return null;
        }

        private static object GetActiveConnectionInfo()
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.FullName.StartsWith("SqlPackageBase") || 
                        assembly.FullName.StartsWith("SqlWorkbench") || 
                        assembly.FullName.StartsWith("Microsoft.SqlServer.Management.SDK.SqlStudio"))
                    {
                        Type serviceCacheType = assembly.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
                        if (serviceCacheType != null)
                        {
                            var scriptFactoryProperty = serviceCacheType.GetProperty("ScriptFactory", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            if (scriptFactoryProperty != null)
                            {
                                var scriptFactory = scriptFactoryProperty.GetValue(null);
                                if (scriptFactory != null)
                                {
                                    var connInfoProperty = scriptFactory.GetType().GetProperty("CurrentlyActiveWndConnectionInfo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                    if (connInfoProperty != null)
                                    {
                                        return connInfoProperty.GetValue(scriptFactory);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error getting active connection via reflection: " + ex.Message);
            }
            return null;
        }
    }
}
