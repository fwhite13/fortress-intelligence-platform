# CC Brief: WI#811 cycle 2 — Restore static Office Add-in files

## Context
Repo: ~/projects/fait-for-excel/
The Vite build config was recently fixed (IIFE format, no type="module") but static files that Vite doesn't bundle were lost. Need to restore them so they're included in every build automatically.

## Current State
- `manifest.xml` — exists at repo root (NOT being copied to dist/)
- `assets/icon-16.png`, `assets/icon-32.png`, `assets/icon-80.png` — exist in assets/ (NOT being copied to dist/)
- `commands.html` — does NOT exist anywhere in the repo (may need to be created)
- No `public/` directory exists yet
- Current dist/ only contains: `assets/taskpane-BnbY4rYu.js` and `src/taskpane/index.html`
- vite.config.ts has no configuration to copy static assets

## What Vite's `public/` directory does
Files placed in `public/` are copied verbatim to the dist root on every build. This is the standard Vite mechanism for static assets that aren't imported/bundled.

## Task

### 1. Create the public/ directory and populate it

```bash
cd ~/projects/fait-for-excel
mkdir -p public/assets
```

Copy manifest.xml to public/:
```bash
cp manifest.xml public/
```

Copy icons to public/assets/:
```bash
cp assets/icon-16.png public/assets/
cp assets/icon-32.png public/assets/
cp assets/icon-80.png public/assets/
```

### 2. Check if commands.html is referenced in manifest.xml

Read manifest.xml to see if it references a commands.html file:
```bash
grep -i "commands" manifest.xml
```

If commands.html is referenced, create a minimal commands.html in public/:
```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8" />
    <meta http-equiv="X-UA-Compatible" content="IE=Edge" />
    <title>Commands Page</title>
</head>
<body>
<script type="text/javascript">
    // Office ribbon command handler
    Office.onReady(function() {});
</script>
</body>
</html>
```

If manifest.xml does NOT reference commands.html, skip creating it.

### 3. Verify the manifest.xml references match the paths

Check what paths manifest.xml uses for icons — they likely reference something like:
- `https://.../excel-addin/assets/icon-16.png` 
- OR `https://.../excel-addin/icon-16.png`

Make sure the public/ structure matches. Check:
```bash
grep -E "icon|commands|taskpane" manifest.xml | head -20
```

Adjust where you put the icons (public/ root vs public/assets/) to match what manifest.xml expects.

### 4. Run the build
```bash
cd ~/projects/fait-for-excel
npm run build
```

### 5. Verify dist/ contains all required files

After build, check:
```bash
find dist/ -type f | sort
```

Required files in dist/:
- `manifest.xml` (at dist root)
- `commands.html` (if referenced by manifest)
- Icon PNGs (wherever manifest expects them — root or assets/)
- taskpane HTML file (should exist from previous build fix)

Verify taskpane HTML has NO type="module":
```bash
grep "script\|type" dist/src/taskpane/index.html
```

### 6. Copy dist/ to wwwroot
```bash
WWWROOT="$HOME/projects/fip/fait/src/FortressAI.Web/wwwroot/excel-addin"
rm -rf "$WWWROOT"
mkdir -p "$WWWROOT"
cp -r ~/projects/fait-for-excel/dist/. "$WWWROOT/"
```

### 7. Report back

Show me:
1. The final contents of dist/ (tree or find)
2. The final contents of wwwroot/excel-addin/
3. Confirm manifest.xml, icon PNGs are present
4. Whether commands.html was created or skipped (and why)
5. The first 3 lines of `npm run build` output
6. Content of taskpane HTML script tags (grep for script/type)

## DO NOT
- Do not change vite.config.ts (public/ mechanism handles it automatically)
- Do not modify the rollupOptions or output format
- Do not change the removeModuleTypePlugin
