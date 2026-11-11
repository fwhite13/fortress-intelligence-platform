/**
 * Chat commands addressed to the notetaker bot ("Notetaker, leave"), like Fireflies' leave-by-chat.
 *
 * Only a message that is *entirely* a command addressed to the bot by name triggers it, so ordinary chat that
 * happens to contain "leave" or "stop" never ends a recording, and the bot's own messages (which are sentences,
 * not commands) never match.
 */

const COMMAND_WORDS = [
  'leave',
  'go away',
  'get out',
  'stop',
  'stop recording',
  'stop the recording',
  'exit',
  'disconnect',
  'drop',
  'leave the meeting',
  'leave the call',
  'leave this meeting',
  'leave this call',
  'leave the chat',
  'leave now',
  'please leave',
  'stop recording now',
];

function escapeRegex(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Names the bot answers to: its configured display name, plus "notetaker" and the first word of the name. */
export function botAliases(botName: string): string[] {
  const names = new Set<string>();
  const clean = (botName || '').trim();
  if (clean) {
    names.add(clean.toLowerCase());
    const first = clean.split(/\s+/)[0];
    if (first && first.length >= 4) names.add(first.toLowerCase());
  }
  names.add('notetaker');
  names.add('note taker');
  return [...names].sort((a, b) => b.length - a.length);
}

export function buildLeaveCommandRegex(botName: string): RegExp {
  const names = botAliases(botName).map(escapeRegex).join('|');
  const words = COMMAND_WORDS.map(escapeRegex).sort((a, b) => b.length - a.length).join('|');
  // [optional greeting/@] NAME [, : -] [please] COMMAND [please/now/thanks] [punctuation]
  return new RegExp(
    `^\\s*@?\\s*(?:(?:hey|hi|ok|okay)[,\\s]+)?(?:${names})\\s*[,:\\-\\u2013\\u2014]?\\s*(?:(?:please|pls|kindly)\\s+)?(?:you\\s+can\\s+)?(?:${words})(?:\\s+(?:now|please|pls|thanks|thank\\s+you))?\\s*[.!?]*\\s*$`,
    'i',
  );
}

/** True when the whole message is a leave command addressed to the bot. */
export function isLeaveCommand(message: string, botName: string): boolean {
  const text = (message || '').replace(/\s+/g, ' ').trim();
  if (!text || text.length > 120) return false;
  return buildLeaveCommandRegex(botName).test(text);
}
