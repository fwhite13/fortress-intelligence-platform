# Admin Guide

The **Admin panel** gives designated administrators full control over the Fortress Intelligence Platform — managing users, curating the corporate knowledge base, and configuring connected tools. This guide walks through every section of the Admin panel.

> **⚠️ Note:** The Admin panel is only accessible to users with the **admin** role. If you believe you should have admin access, contact your Fortress Group administrator.

---

## In This Guide

- [Overview](#overview)
- [User Management](#user-management)
- [Corporate Knowledge Base](#corporate-knowledge-base)
- [MCP Server Management](#mcp-server-management)

---

## Overview

As an admin, you have three core responsibilities in FAIT:

1. **Managing users** — Inviting new users, assigning roles, controlling access, and removing accounts when needed.
2. **Curating the Corporate KB** — Maintaining the organization-wide knowledge base that all users search in chat.
3. **Configuring tools** — Registering and managing MCP servers that power connected business tool integrations like Brave Web Search and Azure DevOps.

Navigate to the Admin panel via the **Admin** link in the sidebar (visible only to admins). The panel is organized into three tabs: **Corporate KB**, **MCP Servers**, and **Users**.

---

## User Management

The **Users** tab provides a complete view of all accounts in the system and the tools to manage them.

### Inviting a New User

1. Go to **Admin** → **Users** tab.
2. Click **Invite User** in the top-right corner.
3. In the dialog, enter the user's email address and any initial details.
4. Complete the invitation. The user will receive instructions to set up their account.

> **💡 Tip:** Users can also self-register via the login page if your organization allows it. Use the invite flow when you want to proactively add someone.

### User Table

The user table shows:

| Column | Description |
|--------|-------------|
| **Name / Email** | Display name (if set) and email address |
| **System Role** | `user` or `admin` |
| **Module Access** | Granted permissions by module |
| **Status** | Active or Disabled |
| **Actions** | Edit permissions, disable/enable, delete |

### Changing a User's Role

1. Find the user in the table.
2. In the **System Role** column, click the dropdown and select `user` or `admin`.
3. The change takes effect immediately.

> **⚠️ Note:** Be thoughtful when granting `admin` role — admins have full access to user management, the Corporate KB, and MCP server configuration.

### Managing Module Permissions

Each user can have fine-grained permissions beyond their system role. To edit:

1. Click the **manage accounts icon** (⚙️) in the user's Actions column.
2. In the permissions dialog, toggle access on or off for each module.
3. Save your changes.

### Disabling a User

Disabling a user prevents them from signing in without deleting their data.

1. Find the user in the table.
2. Click the **disable icon** (person with slash) in the Actions column.
3. The user's status changes to **Disabled** immediately.

To re-enable a disabled user, click the **enable icon** (person with plus) that replaces the disable button.

> **⚠️ Note:** You cannot disable your own account.

### Deleting a User

Deleting a user is permanent and removes their account from the system.

1. Find the user in the table.
2. Click the **delete icon** (trash) in the Actions column.
3. A confirmation dialog will appear describing the consequences.
4. Confirm to proceed.

> **⚠️ Note:** Deleting a user removes their account and access. Their personal KB data and conversation history are also removed. This action cannot be undone. Consider **disabling** a user instead if you may need to restore access later.

> **⚠️ Note:** You cannot delete your own account.

---

## Corporate Knowledge Base

The **Corporate KB** tab is where you manage the organization-wide knowledge base — the **Fortress KB** that all users can search in chat.

### What Goes in the Corporate KB

The Corporate KB is best suited for:
- Company policies (HR, security, compliance, travel, expense)
- Standard operating procedures
- Product and service documentation
- Onboarding materials
- Frequently referenced reference guides

Keep the Corporate KB focused and maintained. Outdated or irrelevant documents reduce the quality of search results for everyone.

### Creating a Corporate KB Entry (Note)

1. Go to **Admin** → **Corporate KB** tab.
2. Click **+ New Note**.
3. Enter a **title** and the **content** of the entry.
4. Add optional **tags** to categorize the entry (comma-separated).
5. Click **Save**.

Notes are immediately available for search — no ingestion delay.

### Searching Corporate Entries

Use the **search bar** at the top of the Corporate KB tab to filter entries by title or content. This is useful for finding an entry to edit or delete.

### Editing a Corporate Entry

1. Find the entry in the list.
2. Click the **edit (pencil) icon**.
3. Modify the title, content, or tags.
4. Save your changes.

### Deleting a Corporate Entry

1. Find the entry in the list.
2. Click the **delete (trash) icon**.
3. The entry is removed immediately.

> **💡 Tip:** For bulk document management (uploading PDFs, DOCX files to the Corporate KB), work with the Fortress Group team — large-scale ingestion may require backend access depending on your deployment configuration.

---

## MCP Server Management

**MCP (Model Context Protocol) Servers** are the backend integrations that power FAIT's connected business tools. When a user chats and FAIT calls a tool like Brave Web Search or Azure DevOps, it's communicating with an MCP server registered here.

### Understanding MCP Servers

Each MCP server:
- Exposes one or more **tools** (callable functions the AI can use)
- Has an **endpoint URL** where FAIT sends tool requests
- Has an **auth type** (none, API key, or OAuth2) for securing those requests
- Can be **enabled or disabled** independently

Users don't configure MCP servers — that's an admin responsibility. Once a server is active, its tools become available automatically to all users in their chat sessions (subject to permissions).

### Viewing Registered Servers

The MCP Servers tab shows a table of all registered servers with:

| Column | Description |
|--------|-------------|
| **Name** | Human-readable server name |
| **Slug** | Short identifier used internally |
| **Auth Type** | none, api_key, or oauth2 |
| **Tools** | Number of tools exposed by this server |
| **Status** | Active or Inactive |
| **Actions** | Edit, Enable/Disable, Refresh manifest |

### Adding a New MCP Server

1. Click **Add Server** in the top-right of the MCP Servers tab.
2. Fill in the server details:
   - **Display Name** — Human-readable name (e.g., "Brave Web Search")
   - **Slug** — Short identifier used in tool names (e.g., `brave`) — cannot be changed after creation
   - **Description** — Optional description of what this server provides
   - **Endpoint URL** — The HTTP URL of the MCP server
   - **Auth Type** — Select `none`, `api_key`, or `oauth2`
   - **Rate Limit** — Maximum tool calls per user per minute (default: 30)
   - **System API Key** — If auth type is `api_key`, enter the key here
3. Click **Save**.
4. After saving, FAIT automatically fetches the server's **tool manifest** to discover what tools are available.

> **💡 Tip:** After adding a server, verify that the tool count shows the expected number of tools. If it shows 0, the server may not be reachable or the manifest fetch may have failed — use the **Refresh** button to retry.

### Editing a Server

1. Click the **edit icon** for the server you want to modify.
2. Update any fields — note that the **Slug** field is locked after creation.
3. To update an API key, enter a new value in the **System API Key** field. Leave it blank to keep the existing key.
4. Click **Save**. FAIT will re-fetch the tool manifest automatically.

### Enabling and Disabling a Server

Click the **pause/play icon** in the Actions column to toggle a server between Active and Inactive.

- **Active** — The server's tools are available to all users in chat.
- **Inactive** — The server is registered but its tools are not presented to users.

Use this to temporarily disable a tool integration without deleting its configuration.

### Refreshing a Tool Manifest

If a server's available tools change (new tools added, old ones removed), click the **refresh icon** to re-fetch the tool manifest. FAIT will update the list of available tools from that server.

### Current Integrations

The following MCP servers may be configured in your deployment:

**Brave Web Search** (`brave`)
Provides real-time web search capabilities. Requires a Brave API key configured as the system API key. Once active, FAIT automatically searches the web when users ask questions requiring current or external information.

**Azure DevOps** (`azure-devops`)
Connects to your Azure DevOps organization. Enables querying work items, sprints, pull requests, and pipelines directly from FAIT chat. Requires appropriate service account credentials or API token.

> **⚠️ Note:** Tool availability in chat depends on the server being **Active** in the MCP Servers table. If users report that a tool isn't working, check the server's status here first.
