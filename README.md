# QuerySight: SQL Charting Visualizer for SSMS 22

QuerySight is a dual-component tooling architecture designed to enable the SSMS GitHub Copilot Agent to automatically generate and render charts inside a native SQL Server Management Studio (SSMS) 22 tool window.

It consists of:
1. **SSMS VSIX Extension**: A C# VSPackage containing a WPF tool window hosting a WebView2 canvas (Chart.js) and a background WebSocket listener.
2. **Python MCP Server**: An MCP server exposing a `render_ssms_chart` tool which forwards SQL query data directly to the local extension.

---

## Workspace Directory Structure

```text
SQL Charts/
├── README.md                           # This instructions file
├── QuerySight.sln                      # VS 2022 Solution
└── src/
    ├── QuerySight.Extension/           # VSIX extension project
    │   ├── QuerySight.Extension.csproj # SDK-Style project targeting .NET 4.8
    │   ├── source.extension.vsixmanifest# Targets VS 2022 / SSMS 22 shells
    │   ├── QuerySightPackage.cs        # Package class & server entry point
    │   ├── QuerySightCommand.cs        # Manual window opening trigger
    │   ├── QuerySightToolWindow.cs     # Tool Window host pane
    │   ├── QuerySightToolWindowControl.xaml / .cs # WPF WebView2 container
    │   ├── ChartBridgeServer.cs        # WebSocket & HTTP POST listener
    │   └── Resources/
    │       └── chart_canvas.html       # Embedded Chart.js canvas asset
    └── QuerySight.McpServer/           # Python MCP server project
        ├── mcp_server.py               # FastMCP tool implementation
        └── requirements.txt            # Python dependencies
```

---

## Prerequisites

- **SQL Server Management Studio (SSMS)**: Version 22.0 or higher.
- **Visual Studio 2022**: Installed with the **Visual Studio Extension Development** workload (required to compile VSIX packages).
- **Microsoft Edge WebView2 Runtime**: Usually pre-installed on Windows 10/11.
- **Python**: Version 3.10 or higher.

---

## Component 1: Building and Installing the SSMS Extension

### Step 1: Compile the VSIX Package
You can compile the extension either using Visual Studio or command line.

#### Method A: Using Visual Studio 2022
1. Open the solution file `QuerySight.sln` in Visual Studio 2022.
2. Ensure you restore NuGet packages (automatically happens during build).
3. Set your configuration mode to **Release** or **Debug**.
4. Build the solution (`Ctrl + Shift + B`).
5. Upon successful compilation, you will find the installer file here:
   `src/QuerySight.Extension/bin/[Configuration]/QuerySight.Extension.vsix`

#### Method B: Using MSBuild in Developer PowerShell
Run the following commands in the workspace root:
```powershell
nuget restore
msbuild /p:Configuration=Release QuerySight.sln
```

### Step 2: Install VSIX to SSMS 22
Because SSMS 22.x uses the isolated Visual Studio 2022 Shell, we target the shell (`Microsoft.VisualStudio.Pro`). You must instruct SSMS's VSIX installer to install it.

1. Locate the `VSIXInstaller.exe` utility associated with SSMS. Typically, it resides in a folder like:
   `C:\Program Files (x86)\Microsoft SQL Server Management Studio 22\Common7\IDE\VSIXInstaller.exe`
2. Run the installer pointing to the compiled `.vsix` file:
   ```powershell
   & "C:\Program Files (x86)\Microsoft SQL Server Management Studio 22\Common7\IDE\VSIXInstaller.exe" src/QuerySight.Extension/bin/Release/QuerySight.Extension.vsix
   ```
3. Complete the installation wizard and start SSMS 22.
4. Verify the extension is loaded by opening the tool window manually via the menu:
   **View -> Other Windows -> QuerySight SQL Chart**.

---

## Component 2: Setting up and Running the Python MCP Server

The Python MCP server exposes the `render_ssms_chart` tool to your Copilot Agent.

### Step 1: Install Dependencies
Navigate to the MCP server folder and install packages:
```powershell
cd src/QuerySight.McpServer
pip install -r requirements.txt
```
*(Or use `uv` for instant setup: `uv pip install -r requirements.txt`)*

### Step 2: Launch the MCP Server
Start the server in standard I/O (stdio) mode:
```powershell
python mcp_server.py
```
*(With `uv`: `uv run python mcp_server.py`)*

---

## Integration with GitHub Copilot Agent / LLM Client

Add the QuerySight MCP server to your local Claude/Copilot configuration files (e.g., `claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "querysight": {
      "command": "python",
      "args": ["C:\\path\\to\\SQL Charts\\src\\QuerySight.McpServer\\mcp_server.py"]
    }
  }
}
```

### LLM Tool Parameters: `render_ssms_chart`
When the Copilot agent calls the tool, it sends:
- `chart_type`: `"bar"` | `"line"` | `"pie"`
- `title`: String header description.
- `query_data_json`: Stringified list of rows:
  ```json
  "[{\"Year\": 2022, \"Sales\": 45000}, {\"Year\": 2023, \"Sales\": 56000}]"
  ```
The chart window in SSMS will immediately pop up, focus, and display the visualization!
