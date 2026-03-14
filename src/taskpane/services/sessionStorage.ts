/* global Office */

const NAMESPACE = 'https://fait.dev.fortressam.ai/excel-addin/session';
const MAX_MESSAGES = 50;

export interface PersistedMessage {
  role: 'user' | 'assistant';
  content: string;
}

export async function saveConversation(messages: PersistedMessage[]): Promise<void> {
  return new Promise((resolve) => {
    // Trim to last MAX_MESSAGES
    const toSave = messages.slice(-MAX_MESSAGES);
    const xml =
      `<session xmlns="${NAMESPACE}"><messages>` +
      toSave
        .map(
          (m) =>
            `<message role="${m.role}"><![CDATA[${m.content}]]></message>`
        )
        .join('') +
      `</messages></session>`;

    // Delete existing, then add fresh
    Office.context.document.customXmlParts.getByNamespaceAsync(NAMESPACE, (existing) => {
      if (existing.value && existing.value.length > 0) {
        existing.value[0].deleteAsync(() => {
          Office.context.document.customXmlParts.addAsync(xml, () => resolve());
        });
      } else {
        Office.context.document.customXmlParts.addAsync(xml, () => resolve());
      }
    });
  });
}

export async function loadConversation(): Promise<PersistedMessage[]> {
  return new Promise((resolve) => {
    Office.context.document.customXmlParts.getByNamespaceAsync(NAMESPACE, (result) => {
      if (
        result.status !== Office.AsyncResultStatus.Succeeded ||
        !result.value ||
        result.value.length === 0
      ) {
        resolve([]);
        return;
      }
      result.value[0].getXmlAsync((xmlResult) => {
        if (xmlResult.status !== Office.AsyncResultStatus.Succeeded) {
          resolve([]);
          return;
        }
        try {
          const parser = new DOMParser();
          const doc = parser.parseFromString(xmlResult.value, 'text/xml');
          const nodes = doc.getElementsByTagName('message');
          const messages: PersistedMessage[] = [];
          for (let i = 0; i < nodes.length; i++) {
            const node = nodes[i];
            const role = node.getAttribute('role') as 'user' | 'assistant';
            const content = node.textContent ?? '';
            if (role && content) messages.push({ role, content });
          }
          resolve(messages);
        } catch {
          resolve([]);
        }
      });
    });
  });
}

export async function clearConversation(): Promise<void> {
  return new Promise((resolve) => {
    Office.context.document.customXmlParts.getByNamespaceAsync(NAMESPACE, (result) => {
      if (
        result.status !== Office.AsyncResultStatus.Succeeded ||
        !result.value ||
        result.value.length === 0
      ) {
        resolve();
        return;
      }
      result.value[0].deleteAsync(() => resolve());
    });
  });
}
