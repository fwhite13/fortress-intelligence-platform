# Teams Guide

**Teams** are collaborative groups within FAIT that let you share a **Team Knowledge Base** with your colleagues. When you belong to a team, you can upload shared documents, create shared notes, and enable that team's KB in your chat conversations — so everyone on the team benefits from the same knowledge.

---

## In This Guide

- [Overview](#overview)
- [Creating a Team](#creating-a-team)
- [Managing Members](#managing-members)
- [Team Knowledge Base](#team-knowledge-base)
- [Best Practices](#best-practices)

---

## Overview

### What Teams Are For

A team in FAIT is a group of users who share a knowledge base. Think of it as a shared folder for the AI — everyone on the team can contribute to it, and everyone can draw on it in their chat conversations.

Teams are organized around work groups, projects, or domains. Some examples:

- An **Engineering** team that shares architecture docs, runbooks, and technical specs
- A **Sales** team that shares product sheets, competitive intelligence, and objection-handling guides
- A **Project X** team that shares project-specific documentation for the duration of that initiative

### What You Can Do in a Team

As a **team member**, you can:
- View the team's KB entries and uploaded documents
- Upload documents to the team KB
- Create new text entries in the team KB
- Enable the team's KB in your chat conversations

As a **team owner**, you can also:
- Add new members to the team
- Remove members from the team

---

## Creating a Team

Any user can create a team. When you create a team, you automatically become its **owner**.

1. Navigate to **Knowledge Base** from the sidebar.
2. Click the **Teams** tab.
3. Click **New Team**.
4. In the dialog, enter:
   - **Name** — A clear, descriptive name for the team (e.g., "Engineering," "Sales Enablement," "Project Phoenix")
   - **Description** — Optional but recommended. Briefly describe the team's purpose.
5. Click **Save**.

Your new team appears in the team list. Click it to open the team detail view where you can start adding members and uploading content.

> **💡 Tip:** Team names appear in the **Team KB dropdown** in chat. Choose names that are clear enough for teammates to recognize at a glance.

---

## Managing Members

### Adding a Member

Only the **team owner** can add or remove members.

1. Open the team detail view (click your team from the Teams tab).
2. Click **Add Member**.
3. A dialog shows all available users in your organization who aren't already on the team.
4. Select the user you want to add.
5. Click **Add**.

The new member immediately gains access to the team's KB and can see the team in their Team KB dropdown in chat.

### Removing a Member

1. Open the team detail view.
2. Scroll to the **Members** section at the bottom.
3. Find the member you want to remove.
4. Click the **remove icon** (person-minus) next to their name.

> **⚠️ Note:** Removing a member revokes their access to the team's KB immediately. Any conversations they had with the team KB enabled will no longer draw on that KB's content. The team's documents and entries are not deleted — only the member's access is removed.

> **⚠️ Note:** You cannot remove yourself as the team owner.

### Member Roles

| Role | Permissions |
|------|-------------|
| **Owner** | Full access: add/remove members, manage KB, upload docs |
| **Member** | KB access: create entries, upload docs, use in chat |

---

## Team Knowledge Base

### What's in the Team KB

The team KB contains:
- **Uploaded documents** — PDF, DOCX, TXT, or MD files uploaded by any team member
- **Notes** — Text entries created by team members

All content is shared — any team member can see, search, and use all entries and documents in the team KB.

### Uploading a Document to the Team KB

1. Open the team detail view.
2. Click **Upload Document**.
3. Select a file (supported: PDF, DOCX, TXT, MD; maximum 10 MB).
4. The upload begins immediately. Wait for the confirmation message.

After upload, documents go through the ingestion pipeline and become searchable within **1–5 minutes**.

> **💡 Tip:** Upload finishes quickly, but the document isn't searchable until ingestion completes. If teammates report they can't find a document in chat, ask them to wait a few minutes and try again.

### Adding a Note to the Team KB

1. Open the team detail view.
2. Click **+ New Note**.
3. Enter a **title** and **content** for the entry.
4. Add optional **tags** (comma-separated) to categorize the note.
5. Click **Save**.

Notes are immediately available — no ingestion delay.

### Deleting Team Documents

To remove an uploaded document from the team KB:

1. Open the team detail view.
2. Find the document in the **Knowledge Documents** section.
3. Click the **delete (trash) icon** next to it.

The document is removed, and the vector index updates within 1–5 minutes.

### Using the Team KB in Chat

The team KB is available in chat via the **Team KB** button in the chat toolbar:

1. Click **Team KB** to open the team selector dropdown.
2. Check the box next to your team (or teams — you can select multiple).
3. Send your message. FAIT will search all checked teams' KBs as part of every message in this conversation.

The Team KB button shows a count badge when multiple teams are active, e.g., `Team KB (2)`.

> **💡 Tip:** Your Team KB selections are saved per conversation. If you start a new conversation, the selection resets to none — re-enable the teams you want for each new chat.

---

## Best Practices

### When to Create a Team

**Create a team when** a group of people regularly needs to share the same reference material in their AI conversations. Teams work best when the knowledge is genuinely shared — things multiple people need to know, not just one person's notes.

**Stick to personal KB** for information that only you need. Personal KB is private and instantly available without team overhead.

**Keep teams focused.** A team called "Engineering" with 200 documents is harder to maintain and produces noisier search results than a team called "API Platform" with 20 focused documents.

### Organizing Team Content

**Use descriptive document names.** When uploading files, the filename is what appears in the KB document list. A file named `2024-leave-policy-v3.pdf` is far easier to manage than `document_final_final2.pdf`.

**Use notes for quick-reference facts.** If there's a piece of information teammates ask about repeatedly, write it as a KB note. It's fast to create, immediately searchable, and easy to update.

**Tag entries consistently.** Agree with your team on a tagging convention. For example: `policy`, `procedure`, `reference`, `archive`. Consistent tags make it much easier to search and manage the KB over time.

**Archive outdated content.** Outdated documents reduce search quality for everyone. When a policy or procedure is superseded, delete the old document and upload the new one. Keep the team KB current.
