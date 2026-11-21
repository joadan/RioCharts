# riocharts-mcp sample

Try the RioCharts MCP server with an AI agent on your machine. Open this folder in the agent,
ask for a chart, and the agent renders it as a PNG or SVG file in `charts/`.

The folder has:

| File | What |
|------|------|
| `.mcp.json` | Registers the server with Claude Code (it asks you to enable it the first time). |
| `.vscode/mcp.json` | The same for GitHub Copilot in VS Code. |
| `data/orders.csv` | An online store selling outdoor gear, hourly for a week per sales channel (Web, App, Marketplace): sessions, orders, revenue (€). |
| `data/stores.csv` | The store's pickup shops: revenue (k€), orders and customer rating, saved as Swedish Excel does (semicolons, decimal commas). |
| `data/revenue-by-category.json` | Yearly revenue per product category (€ and share in %). |

Each file in `data/` is a dataset named after the file. Add your own CSV or JSON files there; the
server reads them again when they change.

## 1. Install the server

Once `RioCharts.Mcp` is on NuGet:

```
dotnet tool install -g RioCharts.Mcp --prerelease
```

Until then, pack it from this repository and install it from the local package (run in the
repository root):

```
dotnet pack src/RioCharts.Mcp -o artifacts
dotnet tool install -g RioCharts.Mcp --prerelease --add-source artifacts
```

Check it with `riocharts-mcp --help`. To update after code changes, pack again and run
`dotnet tool update -g RioCharts.Mcp --prerelease --add-source artifacts`.

## 2. Open this folder in your agent

- **Claude Code:** start `claude` in this folder and allow the `riocharts` server when asked.
- **VS Code (Copilot agent mode):** open this folder; the server is listed under MCP servers.
- **Claude Desktop:** add the server to `claude_desktop_config.json` with absolute paths, since
  it does not start in this folder:

  ```json
  { "mcpServers": { "riocharts": { "command": "riocharts-mcp",
      "args": ["--data", "C:/path/to/samples/RioCharts.Mcp.Sample/data", "--out", "C:/path/to/charts"] } } }
  ```

## 3. Ask for charts

- "Chart sessions per channel for the week, with a line at 1000 sessions."
- "Bar chart of revenue per day for all channels, stacked."
- "Which pickup shops sell the most? Show the top 5 as bars."
- "Donut of revenue per category, dark theme, as SVG."
- "Here are today's orders: 08:00 12, 09:00 18, 10:00 25. Make a line chart." (inline data, no file needed)

The agent gets the image back, a short description of the chart, and the path of the saved file.
If the spec is wrong it gets errors that name the problem and the valid options, and tries again.

Server options (add them to `args`): `--width`, `--height`, `--theme light|dark`, and
`--culture` (e.g. `sv-SE` for decimal commas and Swedish dates). Run `riocharts-mcp --help` for all.
