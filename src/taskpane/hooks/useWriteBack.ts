import { useState } from 'react';
import type { CellSuggestion } from '../components/WriteSuggestionsDialog';

// NOTE: useWriteBack does NOT call applySuggestions — the dialog owns the write.
// acceptAll() is a pure dismiss callback invoked after the dialog has already written.
export function useWriteBack() {
  const [suggestions, setSuggestions] = useState<CellSuggestion[] | null>(null);
  const [showDialog, setShowDialog] = useState(false);
  const [applying, setApplying] = useState(false);

  const offerSuggestions = (sug: CellSuggestion[]) => {
    setSuggestions(sug);
    setShowDialog(true);
  };

  // Called by WriteSuggestionsDialog AFTER it has already written to Excel.
  // Pure dismiss — no write here to avoid double-write.
  const acceptAll = () => {
    setApplying(false);
    setShowDialog(false);
    setSuggestions(null);
  };

  const setApplyingState = (val: boolean) => setApplying(val);

  const reject = () => {
    setShowDialog(false);
    setSuggestions(null);
  };

  return { suggestions, showDialog, applying, offerSuggestions, acceptAll, reject, setApplyingState };
}
