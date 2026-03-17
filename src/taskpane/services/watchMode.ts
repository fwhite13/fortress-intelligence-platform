/**
 * Watch mode write-in-progress flag.
 *
 * All FAIT write operations set this flag to true before writing and false after.
 * The worksheet.onChanged handler checks this flag and suppresses FAIT triggers
 * when a FAIT write is in progress (loop prevention).
 *
 * Module-level singleton — safe in single-threaded JS/React environment.
 */

let _isFaitWriting = false;

/** Set to true before any FAIT write; false immediately after (in a finally block). */
export function setFaitWriting(val: boolean): void {
  _isFaitWriting = val;
}

/** Returns true if FAIT is currently in the middle of a write operation. */
export function isFaitWriting(): boolean {
  return _isFaitWriting;
}
