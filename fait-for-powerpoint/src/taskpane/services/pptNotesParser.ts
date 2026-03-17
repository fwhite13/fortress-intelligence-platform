export interface PptNotesSpec {
  speakerNotes: string;
  sources: string[];
}

export function parseNotesSpec(content: string): PptNotesSpec | null {
  const match = content.match(/```ppt_notes_spec\s*([\s\S]*?)```/);
  if (!match) return null;

  try {
    const parsed = JSON.parse(match[1].trim());
    if (typeof parsed.speakerNotes !== 'string') return null;
    return {
      speakerNotes: parsed.speakerNotes,
      sources: Array.isArray(parsed.sources) ? parsed.sources : [],
    };
  } catch {
    return null;
  }
}

export function stripNotesSpec(content: string): string {
  return content.replace(/```ppt_notes_spec\s*[\s\S]*?```/g, '').trim();
}

export function stripAllSpecs(content: string): string {
  return content.replace(/```ppt_notes_spec\s*[\s\S]*?```/g, '').trim();
}
