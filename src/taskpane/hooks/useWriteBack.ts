import { useState } from 'react';
import type { CellSuggestion } from '../components/WriteSuggestionsDialog';
import { applySuggestions } from '../services/excelWriter';

export function useWriteBack() {
  const [suggestions, setSuggestions] = useState<CellSuggestion[] | null>(null);
  const [showDialog, setShowDialog] = useState(false);
  const [applying, setApplying] = useState(false);

  const offerSuggestions = (sug: CellSuggestion[]) => {
    setSuggestions(sug);
    setShowDialog(true);
  };

  const acceptAll = async () => {
    if (!suggestions) return;
    setApplying(true);
    try {
      await applySuggestions(suggestions);
    } finally {
      setApplying(false);
      setShowDialog(false);
      setSuggestions(null);
    }
  };

  const reject = () => {
    setShowDialog(false);
    setSuggestions(null);
  };

  return { suggestions, showDialog, applying, offerSuggestions, acceptAll, reject };
}
