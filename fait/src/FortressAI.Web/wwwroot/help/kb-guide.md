# Knowledge Base Guide

The **Knowledge Base (KB)** is FAIT's memory system — a searchable store of documents, notes, and information that the AI can draw on when answering your questions. Instead of re-explaining your context every time you start a conversation, you teach FAIT once and it remembers.

---

## In This Guide

- [Overview](#overview)
- [The Three KB Tiers](#the-three-kb-tiers)
- [Uploading Documents](#uploading-documents)
- [Managing Documents & Entries](#managing-documents--entries)
- [Using the KB in Chat](#using-the-kb-in-chat)
- [Team KB](#team-kb)
- [Notes & Text Entries](#notes--text-entries)
- [Troubleshooting](#troubleshooting)

---

## Overview

When you ask FAIT a question, it can do one of two things: answer from its general training, or pull in context from your Knowledge Base. With KB enabled, FAIT searches your documents for relevant passages and uses them to ground its answer in your actual organizational data.

This means you can ask questions like:
- *"What does our procurement policy say about vendor approval thresholds?"*
- *"Summarize the key points from the architecture document I uploaded."*
- *"What did the team note about the migration timeline?"*

…and FAIT will respond with information from your actual documents, not just general knowledge.

> **💡 Tip:** KB context is additive — it supplements the AI's general reasoning, it doesn't replace it. If no relevant passages are found, FAIT still answers from its training.

---

## The Three KB Tiers

FAIT organizes knowledge into three tiers with different ownership and visibility rules.

### 🏛️ Fortress KB (Corporate)

**Managed by:** Administrators only  
**Visible to:** All users in your organization

The Fortress KB is your organization's shared knowledge base. Admins curate it with company-wide content: policies, procedures, product documentation, HR guides, compliance materials, and anything that everyone should have access to.

As a regular user, you can search and use the Fortress KB in your conversations, but you cannot add or modify its content. If you notice something missing or outdated, contact your administrator.

### 👤 My KB (Personal)

**Managed by:** You  
**Visible to:** Only you

Your personal KB is completely private. Upload documents you work with regularly, paste in notes, or store research that informs your work. No one else can see or search your personal KB.

Use My KB for:
- Personal reference documents you return to often
- Notes and summaries you've written
- Research materials for ongoing projects
- Anything you want the AI to know about your specific work

### 👥 Team KB

**Managed by:** Team owner and members  
**Visible to:** Team members only

Team KBs are shared within a specific team. When a team owner creates a team and invites members, those members gain access to the team's KB. Anyone in the team can upload documents to the team KB; only the team owner can manage membership.

Use Team KB for:
- Project documentation shared across a workgroup
- Team-specific processes and runbooks
- Shared research and reference material

---

## Uploading Documents

### Supported Formats

| Format | Extension |
|--------|-----------|
| PDF | `.pdf` |
| Word Document | `.docx` |
| Plain Text | `.txt` |
| Markdown | `.md` |

> **⚠️ Note:** Maximum file size is **10 MB** per document. Files larger than this cannot be uploaded.

### How to Upload a Document

**To upload to your personal KB:**

1. Navigate to the **Knowledge Base** page from the sidebar.
2. Make sure you're on the **My KB** tab.
3. Click **Upload Document**.
4. Select your file from the file browser (supported: PDF, DOCX, TXT, MD).
5. The upload begins immediately. You'll see a progress indicator while the file transfers.

**To upload to a team KB:**

1. Navigate to the **Knowledge Base** page and click the **Teams** tab.
2. Click on the team you want to add documents to.
3. Click **Upload Document** on the team detail page.
4. Select your file and wait for the upload to complete.

### What Happens After Upload

Uploading a document is just the first step. After the file is stored, it goes through an **ingestion pipeline**:

1. **Upload** — File is stored securely in cloud storage.
2. **Processing** — The document is parsed, text is extracted, and it's split into searchable chunks.
3. **Indexing** — The chunks are embedded and added to the vector search index.
4. **Available** — The document is now searchable in chat.

> **⚠️ Note:** Ingestion typically takes **1–5 minutes** after upload. If you search immediately after uploading, the document may not appear yet. Wait a few minutes and try again.

---

## Managing Documents & Entries

### Viewing Your Documents

On the **My KB** tab, your knowledge base is organized into two sections:

- **Entries** — Text notes you've created manually
- **Knowledge Documents** — Files you've uploaded

Documents show the filename, file size, and upload date.

### Searching Your KB

Use the **search bar** at the top of the My KB tab to filter your entries. The search matches against entry titles and content. It does not search the full text of uploaded documents — that searching happens at chat time when FAIT queries the vector index.

### Deleting a Document

To delete an uploaded document:

1. Find the document in the **Knowledge Documents** section.
2. Click the **delete (trash) icon** on the right side of the document row.
3. The document is removed immediately.

> **⚠️ Note:** After deleting a document, the vector search index updates within 1–5 minutes. During that window, the document may still appear in search results.

### Editing a Note Entry

To edit a text entry:

1. Find the entry card in your KB.
2. Click the **edit (pencil) icon** in the top-right of the card.
3. Modify the title, content, or tags in the dialog.
4. Click **Save**.

---

## Using the KB in Chat

### Enabling and Disabling KB

KB toggles appear in the toolbar **above the message input** in the chat view:

- **Fortress KB** — Toggles the corporate knowledge base on/off
- **My KB** — Toggles your personal KB on/off
- **Team KB** — Opens a dropdown to select team KBs

An active toggle is highlighted in **gold**. An inactive toggle has a neutral border. Click any toggle to switch it.

Your KB preferences are saved **per conversation**. If you start a new conversation, the toggles reset to their defaults.

### How KB Context Works in Chat

When you send a message with one or more KBs active, FAIT:

1. Searches the active KB(s) for passages relevant to your question.
2. Injects the most relevant passages into the AI's context (invisibly — you don't see the raw retrieved text).
3. The AI uses that context to inform its response.

You won't see explicit citations by default, but you can ask FAIT to reference its sources: *"What document did that come from?"* or *"Can you cite the source for that?"*

> **💡 Tip:** If FAIT seems to be ignoring information from a document you uploaded, double-check that the relevant KB toggle is active and that ingestion has completed (1–5 minutes after upload).

---

## Team KB

### How Teams Work

Teams are groups of users who share a KB. Each team has:
- A **name** and optional description
- One or more **members** (with Owner and Member roles)
- A shared pool of **entries and documents**

You must be a member of a team to access its KB. Team membership is managed on the Knowledge Base > Teams tab.

### Selecting Teams in Chat

The **Team KB** button in the chat toolbar opens a dropdown showing all teams you belong to. You can:

- **Check a team** to include its KB in the current conversation
- **Uncheck a team** to remove it
- **Select multiple teams** — all checked teams are searched in parallel

The button shows a count badge `(2)` when multiple teams are active, so you always know how many team KBs are contributing to a conversation.

### Managing Team Membership

If you are a **team owner**, you'll see an **Add Member** button on your team's detail page. Click it to select a user from the dropdown and add them to the team. To remove a member, click the **remove (person-minus) icon** next to their name.

> **⚠️ Note:** Only the team owner can add or remove members.

---

## Notes & Text Entries

Not everything worth knowing lives in a file. FAIT's **notes** feature lets you create text-based KB entries directly — no file needed.

### Creating a Note

**In My KB:**

1. Go to **Knowledge Base** → **My KB** tab.
2. Click **+ New Note**.
3. Enter a **title** (required) and **content** (the text you want to store).
4. Optionally add **tags** — comma-separated labels like `policy, compliance, Q4`.
5. Click **Save**.

**In a Team KB:**

1. Go to **Knowledge Base** → **Teams** tab and open your team.
2. Click **+ New Note**.
3. Fill in the title, content, and optional tags.
4. Click **Save**.

Notes are immediately searchable (no ingestion delay). They appear as cards in the KB view, showing a preview of the content and any tags.

> **💡 Tip:** Tags are great for organizing notes by topic or project. You can use the search bar to filter by tag keyword.

---

## Troubleshooting

### Document isn't appearing in chat results

**Wait for ingestion.** Documents take 1–5 minutes to become searchable after upload. If you just uploaded, wait and try again.

**Check the toggle.** Make sure the KB toggle for that document's tier is active (highlighted gold) in the chat toolbar.

**Verify the upload succeeded.** Return to the Knowledge Base page and confirm the document appears in the Knowledge Documents section with the correct filename.

### Upload fails with an error

**Check the file size.** Maximum is 10 MB. Larger files cannot be uploaded — try splitting the document.

**Check the file format.** Only PDF, DOCX, TXT, and MD files are supported. Other formats (Excel, PowerPoint, images) are not supported.

**Try again.** Occasional upload failures happen due to temporary connectivity issues. Refresh the page and retry.

### KB results seem irrelevant or off-topic

**The search is semantic, not keyword-based.** FAIT uses vector search, which finds conceptually similar content, not just exact keyword matches. If a document uses very different terminology than your question, try rephrasing your question to match the document's language.

**Consider uploading a summary note.** If a large document isn't being retrieved reliably, create a note summarizing the key facts. Shorter, focused notes often retrieve better than long documents.

### I can't see a team in the Team KB dropdown

You must be a **member of the team** to see it. Ask the team owner to add you via the Knowledge Base > Teams page.
