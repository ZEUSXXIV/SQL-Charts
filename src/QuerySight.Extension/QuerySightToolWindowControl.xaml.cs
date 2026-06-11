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
        private System.Windows.Threading.DispatcherTimer _connTimer;
        private static string _lastValidConnectionString;
        private static string _lastValidDatabaseName;
        private bool _isQuickBuilderMode = false;
        private string _lastLoadedTablesConnection = null;
        private double _lastSqlPanelHeight = 200;

        public QuerySightToolWindowControl()
        {
            InitializeComponent();
            this.Loaded += OnLoaded;

            // Initialize connection string poller to display the active connection info
            _connTimer = new System.Windows.Threading.DispatcherTimer();
            _connTimer.Interval = TimeSpan.FromSeconds(1.5);
            _connTimer.Tick += ConnTimer_Tick;
            _connTimer.Start();
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
                await webViewMonaco.EnsureCoreWebView2Async(env);

                // 3. Map a virtual host name to the local directory where the extension is running.
                // This makes it easy to load files relative to the installation path securely.
                string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "querysight.local",
                    assemblyFolder,
                    CoreWebView2HostResourceAccessKind.Allow);

                webViewMonaco.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "querysight.local",
                    assemblyFolder,
                    CoreWebView2HostResourceAccessKind.Allow);

                // 4. Point the controls to their virtual domain mapped HTML
                webView.Source = new Uri("https://querysight.local/Resources/chart_canvas.html");
                webViewMonaco.Source = new Uri("https://querysight.local/Resources/sql_editor.html");

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
            if (sqlPanel.Visibility == Visibility.Visible)
            {
                sqlPanel.Visibility = Visibility.Collapsed;
                if (gridSplitter != null) gridSplitter.Visibility = Visibility.Collapsed;
                if (rowSqlPanel != null)
                {
                    _lastSqlPanelHeight = rowSqlPanel.Height.Value > 50 ? rowSqlPanel.Height.Value : 200;
                    rowSqlPanel.Height = new GridLength(0);
                }
            }
            else
            {
                sqlPanel.Visibility = Visibility.Visible;
                if (gridSplitter != null) gridSplitter.Visibility = Visibility.Visible;
                if (rowSqlPanel != null)
                {
                    rowSqlPanel.Height = new GridLength(_lastSqlPanelHeight);
                }
                UpdateActiveConnectionUI();
                TriggerLoadTables();
            }
        }

        private void BtnSqlMode_Click(object sender, RoutedEventArgs e)
        {
            _isQuickBuilderMode = false;
            panelSqlEditor.Visibility = Visibility.Visible;
            panelQuickBuilder.Visibility = Visibility.Collapsed;

            btnSqlMode.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x27, 0x27, 0x2A));
            btnSqlMode.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE4, 0xE4, 0xE7));
            btnSqlMode.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0x3A, 0xED));

            btnBuilderMode.Background = System.Windows.Media.Brushes.Transparent;
            btnBuilderMode.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA1, 0xA1, 0xAA));
            btnBuilderMode.BorderBrush = System.Windows.Media.Brushes.Transparent;
        }

        private void BtnBuilderMode_Click(object sender, RoutedEventArgs e)
        {
            _isQuickBuilderMode = true;
            panelSqlEditor.Visibility = Visibility.Collapsed;
            panelQuickBuilder.Visibility = Visibility.Visible;

            btnBuilderMode.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x27, 0x27, 0x2A));
            btnBuilderMode.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE4, 0xE4, 0xE7));
            btnBuilderMode.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0x3A, 0xED));

            btnSqlMode.Background = System.Windows.Media.Brushes.Transparent;
            btnSqlMode.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA1, 0xA1, 0xAA));
            btnSqlMode.BorderBrush = System.Windows.Media.Brushes.Transparent;

            TriggerLoadTables();
        }

        private async void TriggerLoadTables()
        {
            string connStr = GetActiveConnectionString();
            if (string.IsNullOrEmpty(connStr))
            {
                txtBuilderInfo.Text = "No active connection. Open a connected query tab first.";
                txtBuilderInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)); // Red
                return;
            }

            if (connStr == _lastLoadedTablesConnection && comboTables.Items.Count > 0)
            {
                return;
            }

            txtBuilderInfo.Text = "Loading tables, views, and schemas...";
            txtBuilderInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8)); // Sky Blue
            comboTables.IsEnabled = false;

            try
            {
                var schema = await System.Threading.Tasks.Task.Run(() => GetDatabaseSchema(connStr));
                
                // 1. Populate tables combo
                comboTables.Items.Clear();
                foreach (var table in schema)
                {
                    comboTables.Items.Add(table.TableName);
                }

                // 2. Populate sidebar tree
                PopulateSchemaTree(schema);

                // 3. Inject schema JSON to Monaco for autocomplete
                if (webViewMonaco.CoreWebView2 != null)
                {
                    string schemaJson = Newtonsoft.Json.JsonConvert.SerializeObject(schema);
                    string escapedJson = schemaJson.Replace("'", "\\'");
                    await webViewMonaco.CoreWebView2.ExecuteScriptAsync($"window.updateSchema('{escapedJson}');");
                }

                _lastLoadedTablesConnection = connStr;
                comboTables.IsEnabled = true;

                if (comboTables.Items.Count > 0)
                {
                    txtBuilderInfo.Text = "Schema loaded successfully. Autocomplete & Sidebar are active!";
                    txtBuilderInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81)); // Green
                }
                else
                {
                    txtBuilderInfo.Text = "No tables or views found in this database.";
                    txtBuilderInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)); // Red
                }
            }
            catch (Exception ex)
            {
                txtBuilderInfo.Text = $"Error loading schema: {ex.Message}";
                txtBuilderInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)); // Red
            }
        }

        private void PopulateSchemaTree(System.Collections.Generic.List<TableSchemaInfo> schema)
        {
            treeSchema.Items.Clear();
            foreach (var table in schema)
            {
                var tableItem = new TreeViewItem();
                tableItem.Header = $"📋 {table.TableName}";
                tableItem.Tag = table.TableName;
                tableItem.FontWeight = FontWeights.SemiBold;

                foreach (var col in table.Columns)
                {
                    var colItem = new TreeViewItem();
                    colItem.Header = $"🔹 {col}";
                    colItem.Tag = col;
                    colItem.FontWeight = FontWeights.Normal;
                    tableItem.Items.Add(colItem);
                }

                treeSchema.Items.Add(tableItem);
            }
        }

        private async void TreeSchema_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selectedItem = treeSchema.SelectedItem as TreeViewItem;
            if (selectedItem != null)
            {
                string textToInsert = selectedItem.Tag as string;
                if (!string.IsNullOrEmpty(textToInsert))
                {
                    string text = textToInsert;
                    
                    if (!text.Contains(".") && !text.StartsWith("["))
                    {
                        text = $"[{text}]";
                    }
                    
                    if (text.Contains(".") && !text.StartsWith("["))
                    {
                        text = string.Join(".", System.Array.ConvertAll(text.Split('.'), t => "[" + t.Trim('[', ']') + "]"));
                    }

                    if (webViewMonaco.CoreWebView2 != null)
                    {
                        string js = $"window.insertText('{text.Replace("'", "\\'")}');";
                        await webViewMonaco.CoreWebView2.ExecuteScriptAsync(js);
                    }
                }
            }
        }

        private void BtnRefreshSidebar_Click(object sender, RoutedEventArgs e)
        {
            _lastLoadedTablesConnection = null; // Force reload
            TriggerLoadTables();
        }

        private async void ComboTables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedTable = comboTables.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedTable)) return;

            string connStr = GetActiveConnectionString();
            if (string.IsNullOrEmpty(connStr)) return;

            txtBuilderInfo.Text = $"Loading columns for {selectedTable}...";
            txtBuilderInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8)); // Sky Blue
            comboXAxis.IsEnabled = false;
            comboYAxis.IsEnabled = false;

            try
            {
                var columns = await System.Threading.Tasks.Task.Run(() => GetTableColumns(connStr, selectedTable));
                
                comboXAxis.Items.Clear();
                comboYAxis.Items.Clear();

                foreach (var col in columns)
                {
                    comboXAxis.Items.Add(col);
                    comboYAxis.Items.Add(col);
                }

                comboXAxis.IsEnabled = true;
                comboYAxis.IsEnabled = true;

                if (comboXAxis.Items.Count > 0) comboXAxis.SelectedIndex = 0;
                if (comboYAxis.Items.Count > 1) comboYAxis.SelectedIndex = 1;
                else if (comboYAxis.Items.Count > 0) comboYAxis.SelectedIndex = 0;

                txtBuilderInfo.Text = $"Mapped {columns.Count} columns. Choose Category and Value columns.";
                txtBuilderInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81)); // Green
            }
            catch (Exception ex)
            {
                txtBuilderInfo.Text = $"Error loading columns: {ex.Message}";
                txtBuilderInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)); // Red
            }
        }

        private void BtnRefreshSchema_Click(object sender, RoutedEventArgs e)
        {
            _lastLoadedTablesConnection = null; // Force reload
            TriggerLoadTables();
        }

        private void ComboAggregation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Optional aggregation helper
        }

        private static System.Collections.Generic.List<TableSchemaInfo> GetDatabaseSchema(string connectionString)
        {
            var schemaList = new System.Collections.Generic.List<TableSchemaInfo>();
            string query = @"
                SELECT 
                    t.TABLE_SCHEMA + '.' + t.TABLE_NAME AS TableName,
                    c.COLUMN_NAME
                FROM 
                    INFORMATION_SCHEMA.TABLES t
                JOIN 
                    INFORMATION_SCHEMA.COLUMNS c 
                    ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
                WHERE 
                    t.TABLE_TYPE = 'BASE TABLE' OR t.TABLE_TYPE = 'VIEW'
                ORDER BY 
                    t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION;";

            using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new System.Data.SqlClient.SqlCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        TableSchemaInfo currentTable = null;
                        while (reader.Read())
                        {
                            string tableName = reader.GetString(0);
                            string columnName = reader.GetString(1);

                            if (currentTable == null || currentTable.TableName != tableName)
                            {
                                currentTable = new TableSchemaInfo { TableName = tableName };
                                schemaList.Add(currentTable);
                            }
                            currentTable.Columns.Add(columnName);
                        }
                    }
                }
            }
            return schemaList;
        }

        private static System.Collections.Generic.List<string> GetTableColumns(string connectionString, string fullTableName)
        {
            var columns = new System.Collections.Generic.List<string>();
            
            string schema = "dbo";
            string tableName = fullTableName;
            int dotIdx = fullTableName.IndexOf('.');
            if (dotIdx > 0)
            {
                schema = fullTableName.Substring(0, dotIdx);
                tableName = fullTableName.Substring(dotIdx + 1);
            }

            string query = @"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @tableName
                ORDER BY ORDINAL_POSITION;";

            using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new System.Data.SqlClient.SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@schema", schema);
                    command.Parameters.AddWithValue("@tableName", tableName);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            columns.Add(reader.GetString(0));
                        }
                    }
                }
            }
            return columns;
        }

        private void ConnTimer_Tick(object sender, EventArgs e)
        {
            UpdateActiveConnectionUI();
        }

        private void UpdateActiveConnectionUI()
        {
            try
            {
                string connString = GetActiveConnectionString();
                if (string.IsNullOrEmpty(connString))
                {
                    txtConnectionInfo.Text = "Connection: None (Open query tab)";
                    txtConnectionInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)); // Red
                    return;
                }

                // Trigger schema reload automatically if the connection changes (updates both Sidebar explorer and Monaco autocompletion)
                if (connString != _lastLoadedTablesConnection)
                {
                    TriggerLoadTables();
                }

                var builder = new System.Data.SqlClient.SqlConnectionStringBuilder(connString);
                string server = builder.DataSource;
                string db = builder.InitialCatalog;

                if (string.IsNullOrEmpty(db)) db = "master";

                // Truncate display to look clean in toolbar
                if (server.Length > 20) server = server.Substring(0, 17) + "...";
                if (db.Length > 20) db = db.Substring(0, 17) + "...";

                txtConnectionInfo.Text = $"Connection: {server} | {db}";
                txtConnectionInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8)); // Sky Blue
            }
            catch
            {
                txtConnectionInfo.Text = "Connection: Error";
                txtConnectionInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)); // Red
            }
        }

        private async void BtnRunChart_Click(object sender, RoutedEventArgs e)
        {
            string query = "";
            if (_isQuickBuilderMode)
            {
                string table = comboTables.SelectedItem as string;
                string xAxis = comboXAxis.SelectedItem as string;
                string yAxis = comboYAxis.SelectedItem as string;
                string agg = (comboAggregation.SelectedItem as ComboBoxItem)?.Content as string ?? "None";

                if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(xAxis) || string.IsNullOrEmpty(yAxis))
                {
                    ShowStatus("Error: Please select a Table, Category, and Value column.", false);
                    return;
                }

                // Build query dynamically
                string cleanTable = string.Join(".", System.Array.ConvertAll(table.Split('.'), t => "[" + t.Trim('[', ']') + "]"));
                string cleanX = "[" + xAxis.Trim('[', ']') + "]";
                string cleanY = "[" + yAxis.Trim('[', ']') + "]";

                if (agg == "None")
                {
                    query = $"SELECT {cleanX}, {cleanY} FROM {cleanTable};";
                }
                else
                {
                    string aliasY = "";
                    if (agg == "SUM") aliasY = $"[Total {yAxis.Trim('[', ']')}]";
                    else if (agg == "AVG") aliasY = $"[Average {yAxis.Trim('[', ']')}]";
                    else if (agg == "COUNT") aliasY = $"[Count of {yAxis.Trim('[', ']')}]";
                    else if (agg == "MIN") aliasY = $"[Min {yAxis.Trim('[', ']')}]";
                    else if (agg == "MAX") aliasY = $"[Max {yAxis.Trim('[', ']')}]";
                    else aliasY = $"[{agg} of {yAxis.Trim('[', ']')}]";

                    query = $"SELECT {cleanX}, {agg}({cleanY}) AS {aliasY} FROM {cleanTable} GROUP BY {cleanX};";
                }
            }
            else
            {
                if (webViewMonaco.CoreWebView2 == null)
                {
                    ShowStatus("Error: Monaco Editor is initializing. Please wait.", false);
                    return;
                }

                // Execute script and get query text (returns JSON-serialized string with surrounding double quotes)
                string rawResult = await webViewMonaco.CoreWebView2.ExecuteScriptAsync("window.getQueryText()");
                
                // Parse the JSON string
                query = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(rawResult);
                query = query?.Trim();

                if (string.IsNullOrEmpty(query))
                {
                    ShowStatus("Error: Please write a SQL query first in the Monaco Editor.", false);
                    return;
                }
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

                // Parse out the database name to display it to the user
                string dbName = "Unknown";
                try
                {
                    var builder = new System.Data.SqlClient.SqlConnectionStringBuilder(connString);
                    dbName = builder.InitialCatalog;
                }
                catch {}

                ShowStatus($"Running query against '{dbName}'...", true);

                // Run SQL query on background thread
                string jsonPayload = await System.Threading.Tasks.Task.Run(() =>
                {
                    return ExecuteQueryAndSerialize(connString, query, chartType);
                });

                // Render chart inside WebView2
                RenderChart(jsonPayload);
                ShowStatus($"Chart generated successfully from database '{dbName}'!", true);
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
            if (connectionInfo == null)
            {
                return _lastValidConnectionString;
            }

            try
            {
                // connectionInfo could be UIConnectionInfo itself, or a wrapper object containing UIConnectionInfo
                object uiConn = null;
                var uiConnProp = connectionInfo.GetType().GetProperty("UIConnectionInfo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (uiConnProp != null)
                {
                    uiConn = uiConnProp.GetValue(connectionInfo);
                }
                else
                {
                    // Check if connectionInfo itself has ServerName property (meaning it is likely UIConnectionInfo)
                    var serverNameProp = connectionInfo.GetType().GetProperty("ServerName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (serverNameProp != null)
                    {
                        uiConn = connectionInfo;
                    }
                }

                if (uiConn == null) return _lastValidConnectionString;

                // Get properties from uiConn
                var serverNameProp2 = uiConn.GetType().GetProperty("ServerName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var userNameProp = uiConn.GetType().GetProperty("UserName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var useIntegratedSecurityProp = uiConn.GetType().GetProperty("UseIntegratedSecurity", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var passwordProp = uiConn.GetType().GetProperty("Password", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                string serverName = serverNameProp2?.GetValue(uiConn) as string;
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

                // Try to get the active editor's currently selected database name via ServiceCache.ExtensibilityModel (DTE)
                string activeEditorDb = GetActiveEditorDatabaseName();
                if (!string.IsNullOrEmpty(activeEditorDb))
                {
                    databaseName = activeEditorDb;
                    _lastValidDatabaseName = activeEditorDb;
                }
                else if (!string.IsNullOrEmpty(_lastValidDatabaseName))
                {
                    // Fall back to the last known database name
                    databaseName = _lastValidDatabaseName;
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

                string connStr = builder.ConnectionString;
                if (!string.IsNullOrEmpty(connStr))
                {
                    _lastValidConnectionString = connStr;
                }

                return connStr;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error building connection string: " + ex.Message);
            }
            return _lastValidConnectionString;
        }

        private static string GetActiveEditorDatabaseName()
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
                            var vsMonitorSelectionProperty = serviceCacheType.GetProperty("VSMonitorSelection", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            
                            if (scriptFactoryProperty != null && vsMonitorSelectionProperty != null)
                            {
                                object scriptFactory = scriptFactoryProperty.GetValue(null);
                                object vsMonitorSelection = vsMonitorSelectionProperty.GetValue(null);
                                
                                if (scriptFactory != null && vsMonitorSelection != null)
                                {
                                    // 1. First try using the internal GetCurrentlyActiveFrameDocView method on ScriptFactory (most reliable inside SSMS)
                                    var getDocViewMethod = GetMethodAnywhere(
                                        scriptFactory.GetType(), 
                                        "GetCurrentlyActiveFrameDocView", 
                                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                        
                                    if (getDocViewMethod != null)
                                    {
                                        // Pass true for logicalActive so it returns the active script editor even when the tool window is focused
                                        object docView = getDocViewMethod.Invoke(scriptFactory, new object[] { vsMonitorSelection, true, null });
                                        if (docView != null)
                                        {
                                            var currentDbProp = GetPropertyAnywhere(
                                                docView.GetType(), 
                                                "CurrentDB", 
                                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                                
                                            if (currentDbProp != null)
                                            {
                                                string currentDb = currentDbProp.GetValue(docView) as string;
                                                if (!string.IsNullOrEmpty(currentDb))
                                                {
                                                    return currentDb;
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            // 2. Fallback: Try using ExtensibilityModel (DTE) if the ScriptFactory direct invoke failed
                            var extensibilityModelProperty = serviceCacheType.GetProperty("ExtensibilityModel", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            if (extensibilityModelProperty != null)
                            {
                                object dte = extensibilityModelProperty.GetValue(null);
                                if (dte != null)
                                {
                                    var activeDocProp = dte.GetType().GetProperty("ActiveDocument", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                    if (activeDocProp != null)
                                    {
                                        object activeDocument = activeDocProp.GetValue(dte);
                                        if (activeDocument != null)
                                        {
                                            var activeWindowProp = activeDocument.GetType().GetProperty("ActiveWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                            if (activeWindowProp != null)
                                            {
                                                object activeWindow = activeWindowProp.GetValue(activeDocument);
                                                if (activeWindow != null)
                                                {
                                                    var objectProp = activeWindow.GetType().GetProperty("Object", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                                    if (objectProp != null)
                                                    {
                                                        object editorControl = objectProp.GetValue(activeWindow);
                                                        if (editorControl != null)
                                                        {
                                                            var currentDbProp = GetPropertyAnywhere(
                                                                editorControl.GetType(), 
                                                                "CurrentDB", 
                                                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                                                
                                                            if (currentDbProp != null)
                                                            {
                                                                string currentDb = currentDbProp.GetValue(editorControl) as string;
                                                                if (!string.IsNullOrEmpty(currentDb))
                                                                {
                                                                    return currentDb;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error getting active editor database name: " + ex.Message);
            }
            return null;
        }

        private static MethodInfo GetMethodAnywhere(Type type, string name, BindingFlags flags)
        {
            while (type != null)
            {
                var method = type.GetMethod(name, flags);
                if (method != null) return method;
                type = type.BaseType;
            }
            return null;
        }

        private static PropertyInfo GetPropertyAnywhere(Type type, string name, BindingFlags flags)
        {
            while (type != null)
            {
                var prop = type.GetProperty(name, flags);
                if (prop != null) return prop;
                type = type.BaseType;
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
                            var vsMonitorSelectionProperty = serviceCacheType.GetProperty("VSMonitorSelection", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            
                            if (scriptFactoryProperty != null && vsMonitorSelectionProperty != null)
                            {
                                object scriptFactory = scriptFactoryProperty.GetValue(null);
                                object vsMonitorSelection = vsMonitorSelectionProperty.GetValue(null);
                                
                                if (scriptFactory != null && vsMonitorSelection != null)
                                {
                                    // 1. Try to get docView of the active editor window (passing true to search logically active)
                                    var getDocViewMethod = GetMethodAnywhere(
                                        scriptFactory.GetType(), 
                                        "GetCurrentlyActiveFrameDocView", 
                                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                        
                                    if (getDocViewMethod != null)
                                    {
                                        object docView = getDocViewMethod.Invoke(scriptFactory, new object[] { vsMonitorSelection, true, null });
                                        if (docView != null)
                                        {
                                            object connInfo = GetConnectionInfoFromDocView(docView);
                                            if (connInfo != null)
                                            {
                                                return connInfo;
                                            }
                                        }
                                    }

                                    // 2. Fallback to CurrentlyActiveWndConnectionInfo
                                    var connInfoProperty = scriptFactory.GetType().GetProperty("CurrentlyActiveWndConnectionInfo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                    if (connInfoProperty != null)
                                    {
                                        object connInfo = connInfoProperty.GetValue(scriptFactory);
                                        if (connInfo != null)
                                        {
                                            return connInfo;
                                        }
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

        private static object GetConnectionInfoFromDocView(object docView)
        {
            if (docView == null) return null;
            try
            {
                var connInfoProp = GetPropertyAnywhere(docView.GetType(), "ConnectionInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (connInfoProp != null)
                {
                    object connInfoObj = connInfoProp.GetValue(docView);
                    if (connInfoObj != null)
                    {
                        // Check if it has UIConnectionInfo property
                        var uiConnProp = GetPropertyAnywhere(connInfoObj.GetType(), "UIConnectionInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (uiConnProp != null)
                        {
                            return uiConnProp.GetValue(connInfoObj);
                        }
                        
                        // If not, maybe the object itself is UIConnectionInfo or has ServerName
                        var serverNameProp = GetPropertyAnywhere(connInfoObj.GetType(), "ServerName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (serverNameProp != null)
                        {
                            return connInfoObj;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error getting connection info from docView: " + ex.Message);
            }
            return null;
        }
        public class TableSchemaInfo
        {
            public string TableName { get; set; }
            public System.Collections.Generic.List<string> Columns { get; set; } = new System.Collections.Generic.List<string>();
        }
    }
}
