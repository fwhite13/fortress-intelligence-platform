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

/** A command is a short message: anything longer is ordinary chat (or speech), never a command. */
export const LEAVE_COMMAND_MAX_CHARS = 120;

/**
 * WI #9468: what the spoken path needs inside the page, where this module cannot be imported: the same
 * whole-utterance regex as the chat command (source and flags), the same length cap, and the pattern for the
 * spellings captions use for "notetaker" ("note taker", "Note-taker") that the page rewrites to "notetaker"
 * before testing. The regex itself is unchanged.
 */
export interface LeaveCommandPageSpec {
  source: string;
  flags: string;
  maxChars: number;
  notetakerSource: string;
}

export function leaveCommandPageSpec(botName: string): LeaveCommandPageSpec {
  const re = buildLeaveCommandRegex(botName);
  return {
    source: re.source,
    flags: re.flags,
    maxChars: LEAVE_COMMAND_MAX_CHARS,
    notetakerSource: 'note[\\s\\-\\u2010\\u2011]+taker',
  };
}

/** True when the whole message is a leave command addressed to the bot. */
export function isLeaveCommand(message: string, botName: string): boolean {
  const text = (message || '').replace(/\s+/g, ' ').trim();
  if (!text || text.length > LEAVE_COMMAND_MAX_CHARS) return false;
  return buildLeaveCommandRegex(botName).test(text);
}
