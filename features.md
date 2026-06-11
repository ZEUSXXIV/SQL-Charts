# QuerySight SSMS Extension Features

QuerySight is a high-fidelity SQL visualization and query-assist tool window built for SQL Server Management Studio (SSMS) 22. Here is a list of features implemented to date:

---

## 1. Visualization & Integration Features

### 📊 Interactive Charting Canvas (WebView2)
* Hosts an embedded HTML/JS workspace loading **Chart.js** inside a native WPF tool window context.
* Supports **Bar**, **Line**, and **Pie** charts configured with harmonized linear color gradients, responsive sizing, and elegant status animations.
* Features a collapsible **Monospace Data Grid Table Explorer** at the bottom to inspect raw database rows.
* Includes **Copy to Clipboard (CSV)** and **Export to PNG** actions.

### 🔌 Local Python MCP Server Integration
* Implements a local Python MCP server (`mcp_server.py`) exposing the `render_ssms_chart` tool to LLM agents.
* Transmits structured JSON data over a background WebSocket connection (`ws://localhost:8080/chartbridge/`), popping open the visualizer instantly.

### 🌐 Smart Connection Context Resolution
* Queries active connection strings dynamically by using reflection to interact with the SSMS shell.
* Monitors the active editor tab's selected database catalog dropdown (via internal `ScriptFactory.GetCurrentlyActiveFrameDocView` methods), ensuring charts run against the correct database rather than defaulting to `master`.

---

## 2. Editor & Assistance Features (V1.0.10)

### 📝 Embedded Monaco SQL Editor
* Replaces standard WPF query text boxes with Microsoft's **Monaco Editor** (the engine behind VS Code) inside WebView2.
* Configured with visual dark styling (`vs-dark`), Consolas typography, line numbers, search, code folding, and automatic layout scaling.

### 🧠 Active Database Autocomplete (Intellisense)
* Dynamically fetches the active database schema (tables, views, and columns) on connection change.
* Injects and updates the schema JSON inside Monaco's Intellisense engine to show table and column recommendations as you type.

### 📋 Collapsible Schema Sidebar Explorer
* Embeds a hierarchical TreeView sidebar displaying active database tables, views, and columns.
* **Double-Click Insertion**: Double-clicking any tree node wraps the identifier in standard brackets (e.g. `[SalesLT].[Product]` or `[ProductID]`) and inserts it at the cursor.

---

## 3. Layout & UX Enhancements (V1.0.11)

### 📐 Resizable & Default-Open Grid Layout
* Replaced static height panels with a 4-row layout controlled by a horizontal **GridSplitter**.
* Allows users to drag the SQL Query Panel up/down to customize editor height in proportion to the chart canvas.
* Configured the SQL Panel to be open/visible by default on loading.
* **Stateful Toggle**: Toggling the panel closed saves the custom height in memory, restoring it exactly when expanded again.

---

## 4. Enhanced Quick Chart Builder (No-SQL Mode) (V1.0.12)

### 🔗 Smart Auto-Joins (Foreign Key Relationships)
* Automatically queries the active database's metadata for foreign keys.
* When a table is selected, the **Join Table** dropdown lists related tables dynamically.
* Selecting a table automatically expands the column mappings for X-Axis, Y-Axis, and Filters, prefixing them with table aliases (e.g. `Product.Name` vs `SalesOrderDetail.LineTotal`).
* Compiles the dynamic `INNER JOIN ... ON ...` clauses behind the scenes.

### 🔢 Limit & Sorting Controls (Top N)
* Adds a **Row Limit** dropdown to restrict output (e.g., `Top 10`, `Top 20`, `Top 50`, etc.) using standard `TOP N` SQL query statements.
* Adds a **Sort By** (`None`, `X-Axis`, `Y-Axis`) and **Sort Direction** (`Ascending`, `Descending`) selector.
* Automatically wraps sorting criteria in `ORDER BY` clauses, handling aggregation statements correctly (e.g. `ORDER BY SUM([LineTotal]) DESC`).

### 🔍 Dataset Filtering (Where Clause)
* Adds **Filter Column**, **Operator** (`=`, `>`, `<`, `LIKE`, `IN`, `IS NULL`, `IS NOT NULL`), and **Filter Value** text field.
* Automatically compiles standard `WHERE` filter statements, handles quote escaping, and distinguishes between text and numeric column values dynamically.
