# FAIT User Guide

**FAIT** (Fortress AI Toolkit) is your intelligent workspace on the **Fortress Intelligence Platform (FIP)** — bringing together AI-powered chat, organizational knowledge, and connected business tools in one place. Whether you're researching a question, drafting a document, or querying your team's knowledge base, FAIT is designed to help you work smarter.

---

## In This Guide

- [What's New — March 2026](#whats-new)
- [Getting Started](#getting-started)
- [Chat](#chat)
- [Knowledge Base](#knowledge-base)
- [MCP Tools — Connected Business Tools](#mcp-tools)
- [Account & Settings](#account--settings)
- [Tips & Best Practices](#tips--best-practices)
- [Getting Help](#getting-help)

---

## What's New

> **✨ New:** March 2026

### Sign In with Microsoft

You can now sign in to FAIT using your organizational Microsoft account — no separate password required. On the login page, click **Sign in with Microsoft** and authenticate with your existing Microsoft credentials. This uses your organization's Azure Active Directory (Entra ID), so the same account you use for Teams, Outlook, and other Microsoft apps now works here too.

### Team Knowledge Bases — Multi-Select

Team KBs have been upgraded to support selecting multiple teams at once. Previously you could only search one team's knowledge base per conversation. Now, the **Team KB** button in the chat toolbar opens a dropdown where you can check off as many teams as you like — all selected team knowledge bases will be searched in parallel when you send a message.

### Brave Web Search

FAIT can now search the web in real time using **Brave Web Search**. When this tool is active, FAIT will automatically reach out to the internet when your question requires current or external information — news, technical documentation, public data, and more. No separate configuration is needed; your admin enables it at the system level.

### User Management for Admins

Administrators can now invite and manage users directly from the **Admin panel** — no manual account provisioning required. Admins can invite users by email, set system roles (user or admin), control module-level permissions, disable or re-enable accounts, and remove users when needed.

### Automatic Conversation Naming

When you start a new conversation, FAIT automatically generates a meaningful title based on your first exchange. Your conversation list stays organized without you having to manually rename anything. You can always rename a conversation yourself if you prefer something different.

---

## Getting Started

New to FAIT? Here's how to get up and running in a few minutes.

### Step 1 — Sign In

Navigate to the FAIT login page. You have two options:

1. **Sign in with Microsoft** — Click the Microsoft button and authenticate with your organizational account. This is the recommended method for most users.
2. **Sign in with email** — Enter your email and password. If you're registering for the first time, click **Complete Registration** and fill in your display name, email, and a password.

### Step 2 — Start a Conversation

Once signed in, you land on the chat interface. A new conversation is already open and ready.

1. Type your question or request in the input box at the bottom of the screen.
2. Press **Enter** to send (or **Shift+Enter** to add a new line).
3. FAIT will begin responding immediately — responses stream in word by word.

> **💡 Tip:** Your first conversation is created automatically when you open the chat. The sidebar on the left shows your conversation history — click any conversation to return to it.

### Step 3 — Enable Knowledge Bases

Above the input box, you'll see a row of toggle buttons. These control which knowledge sources FAIT searches when responding:

1. Click **Fortress KB** to search the organization's corporate knowledge base.
2. Click **My KB** to include your personal uploaded documents.
3. Click **Team KB** to open the team selector and check off one or more team knowledge bases.

Active toggles are highlighted in gold. You can mix and match — enable all three at once if you want the broadest context.

### Step 4 — Try Web Search

If Brave Web Search is configured in your organization, you'll see a small indicator next to the KB toggles showing `🔍 Brave available`. FAIT will automatically use web search when your question needs current or external information. Just ask normally — no special command needed.

### Step 5 — Explore the Sidebar

The sidebar (open via the hamburger icon if it's collapsed) gives you access to:

- **New Chat** — Start a fresh conversation
- **Conversation history** — All your past conversations, most recent first
- **Knowledge Base** — Manage your personal and team knowledge bases
- **Admin** (admins only) — Manage users, KB entries, and tool integrations

---

## Chat

### How Conversations Work

Each conversation maintains its full context as you go back and forth with the AI. FAIT uses **Claude** (via Amazon Bedrock) as its underlying model, capable of reasoning, writing, summarizing, analyzing documents, answering questions, and much more.

If a conversation grows very long, FAIT automatically applies **context summarization** — earlier messages are condensed to keep the conversation within model limits. When this happens, a small notice appears in the chat: *"Earlier messages were summarized to stay within context limits."* The conversation continues normally.

### Getting the Best Results

- **Be specific.** "Summarize the Q4 budget policy" works better than "tell me about budgets."
- **Provide context.** If you're working in a specific domain, mention it: "As a project manager, help me write…"
- **Use follow-ups.** FAIT remembers everything in the current conversation. You don't have to repeat yourself — just refine or build on what was said.
- **Enable relevant KBs.** If your question is about company policy, turn on Fortress KB. If it's about your team's work, turn on Team KB.

### Choosing a Model

A **model selector** appears in the top-right corner of the input toolbar. You can switch between available Claude models per conversation. The selected model is saved per-conversation so you can use different models for different tasks.

### Managing Conversations

**Rename a conversation** — In the sidebar, hover over a conversation name and click the edit icon to give it a custom name.

**Delete a conversation** — Hover over a conversation in the sidebar and click the delete (trash) icon. This is permanent.

**Start fresh** — Click **New Chat** at the top of the sidebar to open a new conversation.

> **⚠️ Note:** Deleting a conversation removes all its messages permanently. There is no undo.

---

## Knowledge Base

FAIT's **Knowledge Base (KB)** lets you ground conversations in real documents and information — from company-wide policies to your own personal notes.

### The Three KB Tiers

| Tier | Who manages it | Who can see it |
|------|---------------|----------------|
| **Fortress KB** (Corporate) | Admins only | All users |
| **My KB** (Personal) | You | Only you |
| **Team KB** | Team owner / members | Team members only |

- **Fortress KB** is the organization-wide knowledge base. Admins upload documents and create entries that all users can access. Think: company policies, standard procedures, product documentation.
- **My KB** is your private space. Upload documents and create notes that only you can see and search.
- **Team KB** is shared within a team. Team owners manage membership and can upload documents visible to all team members.

### Toggling KB in Chat

The KB toggles appear above the chat input:

- **Fortress KB** — Click to toggle on/off (highlighted gold when active)
- **My KB** — Click to toggle on/off
- **Team KB** — Click to open the team dropdown; check/uncheck individual teams

When a KB is active, FAIT automatically searches it as part of every message you send. The retrieved context is injected silently into the conversation — you don't see it directly, but the AI uses it to inform its answers.

> **💡 Tip:** You can enable multiple KBs at the same time. FAIT searches all active sources and combines the most relevant results.

For full details on uploading documents, managing entries, and troubleshooting, open the **Knowledge Base Help** from the KB management page (click the `?` icon there).

---

## MCP Tools

### What Are MCP Tools?

**MCP Tools** are connected business integrations that extend FAIT beyond conversation into action. They allow the AI to reach out to external systems — searching the web, querying your project management tools, and more — all within the natural flow of a conversation.

From a user's perspective, tools work automatically. You don't have to trigger them manually. When FAIT determines that a tool would help answer your question, it calls the tool, gets the result, and incorporates it into its response.

### Available Tools

Your organization may have some or all of the following tools configured:

**🔍 Brave Web Search**
Enables FAIT to search the public web for current information. Useful when you need recent news, technical documentation, or anything that isn't in your knowledge base. Ask naturally: *"What's the current status of X?"* or *"Find the latest documentation for Y."*

**📋 Azure DevOps**
Connects FAIT to your Azure DevOps organization. You can ask about work items, sprints, pull requests, and pipelines. Example: *"What are the open bugs in the current sprint?"* or *"Show me recent PRs for the main branch."*

### How Tools Appear in Chat

When a tool is available, you'll see a small indicator in the toolbar area below the KB toggles — something like `🔍 Brave available`. This tells you which tools are active for your session.

While FAIT is executing a tool, a brief status message appears in the chat: `🔍 Searching...` (or similar). This lets you know the AI is fetching external data before completing its response.

> **⚠️ Note:** Tools are configured at the system level by your administrator. If you don't see a tool you'd expect, contact your admin to check whether it's been set up.

---

## Account & Settings

### Your Display Name

Your display name appears in the user interface and in team membership lists. If you registered with an email and password, you can set your display name during registration. Microsoft SSO users get their name from their Microsoft profile.

### Signing Out

To sign out, look for your account options in the application. Signing out ends your session immediately.

> **💡 Tip:** If you use Microsoft SSO, signing out of FAIT does not sign you out of your Microsoft account or other Microsoft apps.

---

## Tips & Best Practices

### Writing Effective Prompts

**Be specific about what you want.** Instead of asking "tell me about the leave policy," try "What is the maximum number of carry-over vacation days allowed under our current leave policy?" The more precise your question, the more useful the answer.

**State your role or context when it matters.** If you're writing a customer email, say so. If you're analyzing a technical document, mention your background. Context shapes the response style and depth.

**Use multi-turn conversation.** Don't try to put everything in one message. Ask, refine, follow up. FAIT remembers the whole conversation, so "Can you make that more concise?" or "What about the compliance implications?" works naturally.

### KB vs. Web Search — When to Use Which

| Use case | Best source |
|----------|------------|
| Company policies, procedures, internal docs | Fortress KB |
| Your own notes, research, personal uploads | My KB |
| Shared team documentation | Team KB |
| Current events, public documentation, recent news | Web Search (Brave) |
| General knowledge, reasoning, writing help | No KB needed — just ask |

### Managing Your Knowledge Base

**Upload documents before you need them.** Documents take 1–5 minutes to process after upload. If you're planning a research session, upload your documents a few minutes ahead of time.

**Use notes for quick context.** Can't upload a file? Create a **note** in My KB — paste text directly. Notes are immediately searchable.

**Tag your entries.** When creating KB notes, add tags to group related content. This makes it easier to manage your KB as it grows.

---

## Getting Help

If you run into issues or have questions not covered in this guide:

- **Contact your Fortress Group administrator** for account access, permissions, or tool configuration questions.
- **Email support:** support@fortressam.ai

For feature-specific help, look for the **`?` help button** on each page — the Knowledge Base page and Admin panel each have their own detailed guides.
