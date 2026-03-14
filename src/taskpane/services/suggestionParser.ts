import type { CellSuggestion } from '../components/WriteSuggestionsDialog';

export interface ParseResult {
  displayText: string;   // response with JSON block stripped
  suggestions: CellSuggestion[] | null;
}

export function parseSuggestions(rawText: string): ParseResult {
  // Look for ```json\n{...}\n``` block (with "suggestions" key) anywhere in the text
  const regex = /```json\s*(\{[\s\S]*?"suggestions"[\s\S]*?\})\s*```/;
  const match = rawText.match(regex);

  if (!match) {
    return { displayText: rawText, suggestions: null };
  }

  try {
    const parsed = JSON.parse(match[1]);
    const suggestions: CellSuggestion[] = parsed.suggestions;

    if (!Array.isArray(suggestions) || suggestions.length === 0) {
      return { displayText: rawText, suggestions: null };
    }

    // Strip the JSON block from the displayed text and clean up surrounding whitespace
    const displayText = rawText.replace(match[0], '').replace(/\n{3,}/g, '\n\n').trim();

    return { displayText, suggestions };
  } catch {
    // Bad JSON — return full text, no suggestions
    return { displayText: rawText, suggestions: null };
  }
}
