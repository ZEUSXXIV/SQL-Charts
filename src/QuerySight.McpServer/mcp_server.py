import json
import asyncio
import websockets
from typing import Any
from mcp.server.fastmcp import FastMCP

# Initialize the FastMCP server for QuerySight
mcp = FastMCP("QuerySight")

@mcp.tool()
async def render_ssms_chart(chart_type: str, title: str, query_data_json: Any) -> str:
    """
    Pushes query results from the SSMS GitHub Copilot Agent to the SSMS QuerySight tool window.
    
    Parameters:
    - chart_type: The type of chart to display ('bar', 'line', 'pie').
    - title: The title/header of the chart (e.g. 'Monthly Revenue 2025').
    - query_data_json: The query results data (either a JSON string or a parsed list of row objects).
    """
    # 1. Parse and validate JSON data
    try:
        if isinstance(query_data_json, str):
            data_parsed = json.loads(query_data_json)
        else:
            data_parsed = query_data_json

        if not isinstance(data_parsed, list):
            return "Error: query_data_json must be a JSON array of objects (e.g. '[{...}, {...}]' or a list)."
        if len(data_parsed) == 0:
            return "Error: query_data_json is empty. Cannot render chart with 0 rows."
    except json.JSONDecodeError as e:
        return f"Error: query_data_json is not valid JSON string. Details: {str(e)}"

    # 2. Package the payload for the SSMS VSIX extension
    payload = {
        "chart_type": chart_type,
        "title": title,
        "data": data_parsed
    }

    # 3. Connect to the C# bridge server via WebSocket and send the payload
    uri = "ws://localhost:8080/chartbridge/"
    try:
        # Establish WebSocket connection with a 5-second timeout
        async with websockets.connect(uri, open_timeout=5.0) as websocket:
            # Send JSON payload
            await websocket.send(json.dumps(payload))
            
            # Await acknowledgment response from the VSIX extension
            ack_raw = await websocket.recv()
            ack = json.loads(ack_raw)
            
            if ack.get("status") == "success":
                return f"Success: Chart '{title}' ({chart_type}) with {len(data_parsed)} records was successfully pushed and rendered in SSMS."
            else:
                return f"Warning: Chart data sent, but SSMS extension returned warning: {ack.get('message')}"
                
    except ConnectionRefusedError:
        return (
            "Error: Could not connect to SSMS. Please verify that:\n"
            "1. SQL Server Management Studio 22 is open.\n"
            "2. The QuerySight VSIX extension is installed and loaded.\n"
            "3. The background server is listening on ws://localhost:8080/chartbridge/."
        )
    except OSError as e:
        return f"Error: Socket communication error. Details: {str(e)}"
    except Exception as e:
        return f"Error: Unexpected error occurred when pushing chart: {str(e)}"

if __name__ == "__main__":
    # Start the FastMCP server (standard input/output communication)
    mcp.run()
