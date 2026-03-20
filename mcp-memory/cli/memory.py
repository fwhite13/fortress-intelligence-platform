#!/usr/bin/env python3
"""
FIP Memory CLI — cross-session memory for Claude Code sessions
Install: curl https://steamserver.tail7a7e88.ts.net:3100/cli/memory.py -o ~/.local/bin/memory && chmod +x ~/.local/bin/memory
"""

import argparse
import json
import os
import sys
import urllib.request
import urllib.error
from pathlib import Path

CONFIG_PATH = Path.home() / ".config" / "fip-memory" / "config.json"
CONFIG_PATH_LEGACY = Path.home() / ".fip-memory.json"

DEFAULT_SERVER = "https://mcp.fortressam.ai"


def load_config() -> dict:
    for path in [CONFIG_PATH, CONFIG_PATH_LEGACY]:
        if path.exists():
            with open(path) as f:
                return json.load(f)
    return {}


def save_config(cfg: dict) -> None:
    CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(CONFIG_PATH, "w") as f:
        json.dump(cfg, f, indent=2)
    print(f"Config saved to {CONFIG_PATH}")


def get_server_and_token(cfg: dict) -> tuple[str, str]:
    server = cfg.get("server") or os.environ.get("FIP_MEMORY_SERVER") or DEFAULT_SERVER
    token = cfg.get("token") or os.environ.get("FIP_MEMORY_TOKEN")
    if not token:
        print("Error: No API token configured. Run: memory configure", file=sys.stderr)
        sys.exit(1)
    return server.rstrip("/"), token


def mcp_call(server: str, token: str, tool: str, arguments: dict) -> dict:
    """Call an MCP tool via HTTP POST to /mcp"""
    payload = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {
            "name": tool,
            "arguments": arguments,
        },
    }
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        f"{server}/mcp",
        data=data,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {token}",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            body = resp.read().decode("utf-8")
            result = json.loads(body)
            if "error" in result:
                print(f"Error: {result['error']}", file=sys.stderr)
                sys.exit(1)
            # Extract text content from MCP response
            content = result.get("result", {}).get("content", [])
            if content and content[0].get("type") == "text":
                return json.loads(content[0]["text"])
            return result
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8")
        print(f"HTTP {e.code}: {body}", file=sys.stderr)
        sys.exit(1)
    except urllib.error.URLError as e:
        print(f"Connection error: {e.reason}", file=sys.stderr)
        sys.exit(1)


def cmd_configure(args: argparse.Namespace) -> None:
    cfg = load_config()
    if args.server:
        cfg["server"] = args.server
    if args.token:
        cfg["token"] = args.token
    if not args.server and not args.token:
        # Interactive
        current_server = cfg.get("server", DEFAULT_SERVER)
        print(f"Server URL [{current_server}]: ", end="")
        server_input = input().strip()
        if server_input:
            cfg["server"] = server_input
        print("API Token: ", end="")
        token_input = input().strip()
        if token_input:
            cfg["token"] = token_input
    save_config(cfg)
    print("Configuration saved.")


def cmd_add(args: argparse.Namespace) -> None:
    cfg = load_config()
    server, token = get_server_and_token(cfg)

    content = args.content
    if not content:
        print("Reading from stdin (Ctrl+D to finish):")
        content = sys.stdin.read().strip()
    if not content:
        print("Error: No content provided", file=sys.stderr)
        sys.exit(1)

    arguments: dict = {"content": content}
    if args.type:
        arguments["entry_type"] = args.type
    if args.project:
        arguments["project"] = args.project
    if args.scope:
        arguments["scope"] = args.scope
    if args.confirmed:
        arguments["confirmed"] = True

    result = mcp_call(server, token, "memory_add", arguments)

    if result.get("confirmation_required"):
        print(f"\n{result['message']}")
        print(f"\nPreview: {result['preview'][:200]}")
        print("\nRun with --confirmed to proceed.")
    else:
        print(f"✓ Memory stored: {result.get('id')}")
        print(f"  Created: {result.get('created_at')}")


def cmd_search(args: argparse.Namespace) -> None:
    cfg = load_config()
    server, token = get_server_and_token(cfg)

    arguments: dict = {"query": args.query}
    if args.project:
        arguments["project"] = args.project
    if args.limit:
        arguments["limit"] = args.limit

    results = mcp_call(server, token, "memory_search", arguments)

    if not results:
        print("No results found.")
        return

    for i, entry in enumerate(results, 1):
        similarity = entry.get("similarity", 0)
        scope_tag = f"[{entry['scope']}]"
        project_tag = f" ({entry['project']})" if entry.get("project") else ""
        print(f"\n{'='*60}")
        print(f"{i}. {scope_tag}{project_tag} {entry['entry_type']} — similarity: {similarity:.3f}")
        print(f"   ID: {entry['id']}")
        print(f"   {entry['content']}")
        print(f"   Created: {entry['created_at']}")


def cmd_list(args: argparse.Namespace) -> None:
    cfg = load_config()
    server, token = get_server_and_token(cfg)

    arguments: dict = {}
    if args.project:
        arguments["project"] = args.project
    if args.scope:
        arguments["scope"] = args.scope
    if args.limit:
        arguments["limit"] = args.limit

    results = mcp_call(server, token, "memory_list", arguments)

    if not results:
        print("No entries found.")
        return

    print(f"{'ID':<38} {'SCOPE':<10} {'TYPE':<10} {'PROJECT':<15} CONTENT")
    print("-" * 100)
    for entry in results:
        content_preview = entry["content"][:50].replace("\n", " ")
        project = entry.get("project") or ""
        print(f"{entry['id']:<38} {entry['scope']:<10} {entry['entry_type']:<10} {project:<15} {content_preview}")


def cmd_delete(args: argparse.Namespace) -> None:
    cfg = load_config()
    server, token = get_server_and_token(cfg)

    result = mcp_call(server, token, "memory_delete", {"id": args.id})

    if result.get("error"):
        print(f"Error: {result['error']}", file=sys.stderr)
        sys.exit(1)
    else:
        print(f"✓ Deleted: {result.get('deleted')}")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="FIP Memory CLI — manage cross-session memory",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Commands:
  configure   Set server URL and API token
  add         Add a memory entry
  search      Search memories by semantic similarity
  list        List recent memory entries
  delete      Delete a memory entry by ID

Examples:
  memory configure --server https://steamserver.tail7a7e88.ts.net:3100 --token <token>
  memory add "We decided to use pgvector for embeddings storage" --type decision --project firm
  memory search "pgvector architecture decisions" --project firm
  memory list --scope personal --limit 10
  memory delete 550e8400-e29b-41d4-a716-446655440000
        """,
    )
    subparsers = parser.add_subparsers(dest="command")

    # configure
    p_configure = subparsers.add_parser("configure", help="Configure server and token")
    p_configure.add_argument("--server", help="Server URL")
    p_configure.add_argument("--token", help="API token")

    # add
    p_add = subparsers.add_parser("add", help="Add a memory entry")
    p_add.add_argument("content", nargs="?", default="", help="Memory content (or pipe via stdin)")
    p_add.add_argument("--type", choices=["decision", "lesson", "context", "note"], default="note")
    p_add.add_argument("--project", help="Project tag")
    p_add.add_argument("--scope", choices=["personal", "org"], default="personal")
    p_add.add_argument("--confirmed", action="store_true", help="Confirm org write")

    # search
    p_search = subparsers.add_parser("search", help="Search memories")
    p_search.add_argument("query", help="Search query")
    p_search.add_argument("--project", help="Filter by project")
    p_search.add_argument("--limit", type=int, default=10, help="Max results")

    # list
    p_list = subparsers.add_parser("list", help="List memory entries")
    p_list.add_argument("--project", help="Filter by project")
    p_list.add_argument("--scope", choices=["personal", "org", "all"], default="all")
    p_list.add_argument("--limit", type=int, default=20, help="Max results")

    # delete
    p_delete = subparsers.add_parser("delete", help="Delete a memory entry")
    p_delete.add_argument("id", help="Entry UUID to delete")

    args = parser.parse_args()

    if not args.command:
        parser.print_help()
        sys.exit(1)

    dispatch = {
        "configure": cmd_configure,
        "add": cmd_add,
        "search": cmd_search,
        "list": cmd_list,
        "delete": cmd_delete,
    }
    dispatch[args.command](args)


if __name__ == "__main__":
    main()
