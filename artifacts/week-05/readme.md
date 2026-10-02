# MCP Server – Week 5 Build

## Demo
[Watch the MCP Inspector connected to this server, running the heuristic evaluation tool](artifacts/week-05/mcp-tool-evaluation-demo.mp4) — shows tool discovery, schema validation, and execution.

## What This Is
This is a **Model Context Protocol (MCP) server**. It lets Claude Code talk to your custom tools and resources.

---

## How to Start It

### Step 1: Open your terminal in this folder
Navigate to `mcp-server/` (where this README lives).

### Step 2: Run this exact command:
```bash
python src/server.py
```

### Step 3: You'll see this message:
```
MCP server running on stdio
[Listening for messages...]
```

When you see that, **the server is running**. It will stay running and listen for Claude Code to send it commands.

**To stop it:** Press `Ctrl+C` in the terminal.

---

## What Happens Next
Once running, Claude Code can connect to this server and use the tools, resources, and prompts defined in the `src/` folder.

---

## What This Server Assumes

### Files and Folders
- The server assumes these Python files exist in the `src/` folder:
  - `server.py` (main server)
  - `tools.py` (tool definitions and handlers)
  - `resources.py` (resource definitions and content)
  - `prompts.py` (prompt definitions and templates)
- All paths are relative to the `mcp-server/` folder (the folder where this README lives).
- You must **run the server from the `mcp-server/` folder**. Running from elsewhere will fail because imports can't find the modules.

### Python Environment
- Python 3.6 or later must be installed and available as `python` in your terminal.
- Only standard library modules are used (`sys`, `json`, `typing`). No external packages required.
- No API keys, credentials, or environment variables needed.

### State and Memory
- The server is **stateless**—it holds no information between calls.
- Tool, resource, and prompt definitions are loaded from `src/` files when the server starts.
- **On restart:** Everything reloads from disk. If you edit `tools.py` or `resources.py`, you must restart the server for changes to take effect.
- No database or persistent storage. All data is in-memory.

### Input/Output
- The server **reads from stdin** and **writes to stdout** (JSON-RPC protocol).
- Startup messages go to stderr (the terminal, not parsed by MCP clients).
- **No files are written to disk.** The server does not create logs, caches, or state files. It's safe to run multiple times.

### Tool and Resource Behavior
- The `evaluate_design_heuristics` tool always returns the same mock evaluation (not connected to any real AI or analysis engine). It's a placeholder.
- The `ux_guide_sections` resource always returns the same 4 static sections. Content is hard-coded in `resources.py`.
- The `design_review_checklist` prompt always returns the same template, with parameters filled in. No external template engine or API calls.

### Assumptions About Use
- The server expects one caller (Claude Code) sending one message at a time over stdin.
- It does not handle concurrent requests. Sequential, single-threaded operation only.
- It assumes input is valid UTF-8 JSON. Invalid JSON causes an error response, but doesn't crash the server.

---

## Most Likely Breakage Point

**The assumption most likely to break:** Working directory and Python imports.

**What breaks:**
If someone clones this repo and runs the server from the wrong directory, Python can't find `tools.py`, `resources.py`, and `prompts.py`. For example:
```bash
# This fails (ImportError):
cd src
python server.py

# This also fails:
cd Week\ 5
python mcp-server/src/server.py
```

**Why:** The server code imports modules by name (`from tools import ...`), which works only when Python's working directory includes `src/`. The import paths are relative, not absolute.

**Smallest fix:**
Add two lines to the top of `server.py` (before imports) to add `src/` to the Python path:
```python
import sys
import os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '.'))
```

This makes imports work from any directory, as long as `src/server.py`, `src/tools.py`, `src/resources.py`, and `src/prompts.py` all exist together.
