/**
 * WI #8866: Third-party AI notetaker bots (Fireflies, Otter, ...) often stay in a
 * meeting after every human has left. They must not count as "someone is here" for
 * the empty-meeting / end-poll decisions, or the bot sits in silence until the
 * calendar event ends. They are still recorded in the roster timeline as usual.
 *
 * Patterns are case-insensitive substrings of the participant display name.
 * Override with NOTETAKER_BOT_PATTERNS (comma-separated).
 */
const DEFAULT_NOTETAKER_BOT_PATTERNS = ['Fireflies', 'Otter', 'Fathom', 'Grain', 'Notetaker', 'tl;dv'];

function loadPatterns(): string[] {
  const env = process.env.NOTETAKER_BOT_PATTERNS;
  const source = env !== undefined && env.trim().length > 0 ? env.split(',') : DEFAULT_NOTETAKER_BOT_PATTERNS;
  return source.map(p => p.trim().toLowerCase()).filter(p => p.length > 0);
}

// Loaded lazily so dotenv has populated process.env before first use
let patterns: string[] | null = null;

export function isNotetakerBot(name: string): boolean {
  patterns ??= loadPatterns();
  const lower = name.toLowerCase();
  return patterns.some(p => lower.includes(p));
}
