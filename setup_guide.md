# QuerySight Installation & Setup Guide

QuerySight is a high-fidelity SQL visualization and query-assist tool window built natively for **SQL Server Management Studio (SSMS) 22**. It exposes a local Model Context Protocol (MCP) server so that GitHub Copilot Chat running in SSMS can automatically generate and display interactive database charts.

Follow these step-by-step instructions to compile, install, and configure both the SSMS VSIX extension and the local Python MCP server.

---

## 🛠️ Step 1: Compiling the SSMS VSIX Extension

You can compile the extension either using **Visual Studio 2022** or the **MSBuild** command line.

### Method A: Using Visual Studio 2022
1. Open the solution file [QuerySight.sln](file:///c:/Users/Naveen/Documents/projects/sentencepiece/SQL%20Charts/QuerySight.sln) in Visual Studio 2022.
2. Select the **Release** build configuration from the toolbar dropdown.
3. Build the solution (`Ctrl + Shift + B` or **Build > Build Solution**).
4. Upon compilation, the installer file is created at:
   `src/QuerySight.Extension/bin/Release/QuerySight.Extension.vsix`

### Method B: Using Developer Command Line
From the project workspace root, run the following MSBuild command:
```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" src/QuerySight.Extension/QuerySight.Extension.csproj /p:Configuration=Release
```

---

## 🔌 Step 2: Deploying the VSIX Extension to SSMS 22

Because SSMS 22 uses the isolated Visual Studio 2022 Shell, standard double-clicking may not target it automatically. We target it explicitly using SSMS's VSIXInstaller:

1. Close any running instances of SSMS.
2. Open **PowerShell** and run the VSIXInstaller pointing to the compiled package:
   ```powershell
   Start-Process -FilePath "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" -ArgumentList "/q", '"C:\<Path-To-Your-Clone>\SQL-Charts\src\QuerySight.Extension\bin\Release\QuerySight.Extension.vsix"' -Wait
   ```
3. Update the SSMS package configuration cache for the registration to take effect:
   ```powershell
   Start-Process -FilePath "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe" -ArgumentList "/updateconfiguration" -Wait
   ```
4. Verify the installation by starting SSMS. You can open the tool window manually via:
   **View > Other Windows > QuerySight SQL Chart**

---

## 🐍 Step 3: Setting Up the Python MCP Server

The MCP server handles communication between GitHub Copilot and the SSMS tool window over a local WebSocket port (`8080`).

### 1. Install Dependencies
Ensure you have Python 3.10+ installed. Install the required libraries globally or in your specific python environment:
```powershell
pip install mcp websockets
```

### 2. Locate the Server Script
The Python script is located at:
`C:\<Path-To-Your-Clone>\SQL-Charts\src\QuerySight.McpServer\mcp_server.py`

---

## ⚙️ Step 4: Registering the MCP Server & SSMS Config

To enable GitHub Copilot Chat inside SSMS to communicate with the QuerySight MCP tool, you must configure two files: the user `.mcp.json` file and the SSMS `settings.json` file.

### 1. Configure the `.mcp.json` File
Create or modify the file at `C:\Users\<Your-Username>\.mcp.json` with the following configuration (replace the script path with the absolute path of your local clone):

```json
{
  "inputs": [],
  "servers": {
    "QuerySight": {
      "type": "stdio",
      "command": "python",
      "args": [
        "C:\\<Path-To-Your-Clone>\\SQL-Charts\\src\\QuerySight.McpServer\\mcp_server.py"
      ],
      "env": {}
    }
  }
}
```

### 2. Configure SSMS `settings.json`
To register the server and allow the `render_ssms_chart` tool to run inside the Copilot Chat panel, you must update the SSMS Unified Settings file.

1. Open the settings file in a text editor:
   `C:\Users\<Your-Username>\AppData\Local\Microsoft\SSMS\22.0_a53a4edb\settings.json`
2. Add/merge the following keys into the JSON configuration (replacing the path to your `.mcp.json` file accordingly):
   ```json
   {
     "copilot.featureFlags.chatUI.enabledMcpServers": "sql-tools::C:\\Program Files\\Microsoft SQL Server Management Studio 22\\Release\\Common7\\IDE\\Extensions\\Microsoft\\SSMS.CopilotUiTools\\McpServer\\mcp.json,QuerySight::C:\\Users\\<Your-Username>\\.mcp.json",
     "copilot.featureFlags.chatUI.enabledTools": "QuerySight_render_ssms_chart"
   }
   ```
3. Restart SSMS.

---

## ⚡ Step 5: Testing & Verification

1. Start SSMS.
2. Open the **GitHub Copilot Chat** panel on the right sidebar.
3. Open the **QuerySight SQL Chart** tool window via **View > Other Windows > QuerySight SQL Chart** (it can remain docked or collapsed).
4. Run a query in an editor tab (e.g. against `AdventureWorks2019`).
5. In the Copilot Chat box, ask:
   > *"Run a query on my database to find the total sales (LineTotal) grouped by Product Category Name, and render the results as a bar chart titled 'Sales by Category' using the QuerySight tool."*
6. Copilot will run the query, call the `render_ssms_chart` tool, and the QuerySight window will pop open showing your interactive chart!
