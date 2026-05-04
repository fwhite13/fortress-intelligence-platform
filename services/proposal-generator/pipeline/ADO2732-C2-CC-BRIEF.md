# CC Brief: ADO#2732 Cycle 2 — Restore ce8a2b5 baseline + apply Fixes 2, 3, 5

## Context

The file `services/proposal-generator/templates/verticals/nbais-wc/master.docx` needs 5 XML fixes.

Commit `ce8a2b5` correctly applied Fix 1 and Fix 4.
Commit `a64c6ab` (HEAD) regressed Fix 1 and Fix 4, and misinterpreted Fixes 2, 3, and 5.

## Step 1 — Restore ce8a2b5 baseline for master.docx

Run:
```bash
cd /home/fredw/projects/fip
git checkout ce8a2b5 -- services/proposal-generator/templates/verticals/nbais-wc/master.docx
```

This restores the state where Fix 1 and Fix 4 are correctly applied.

## Step 2 — Run pre-fix verification (confirm ce8a2b5 baseline is correct)

Write this Python script to `/tmp/ado2732-verify.py` and run it:

```python
import zipfile, lxml.etree as ET, sys

W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'

def is_empty_para(p):
    ns = {'w': W}
    return len(p.findall('w:r', ns)) == 0 and len(p.findall('w:hyperlink', ns)) == 0

docx = '/home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/master.docx'

with zipfile.ZipFile(docx) as z:
    xml = z.read('word/document.xml')

root = ET.fromstring(xml)
ns = {'w': W}
tables = root.findall('.//w:tbl', ns)

print(f"Total tables: {len(tables)}")
issues = []
for i, tbl in enumerate(tables):
    for j, row in enumerate(tbl.findall('w:tr', ns)):
        for k, cell in enumerate(row.findall('w:tc', ns)):
            paras = cell.findall('w:p', ns)
            if paras and is_empty_para(paras[0]):
                issues.append(f"  Table {i}, row {j}, cell {k}: leading empty para")

if issues:
    print("LEADING EMPTY PARA ISSUES:")
    for issue in issues:
        print(issue)
else:
    print("Fix 1: PASS — no leading empty paras")

# Check Fix 4
for i, tbl in enumerate(tables):
    tbl_text = ET.tostring(tbl, encoding='unicode')
    if '{#excludedPersons}' in tbl_text:
        print(f"\nExcluded persons table: table {i}")
        for row in tbl.findall('w:tr', ns):
            for cell in row.findall('w:tc', ns):
                texts = [t.text or '' for t in cell.findall('.//w:t', ns)]
                combined = ''.join(texts)
                if '{#excludedPersons}' in combined or '{name}' in combined:
                    paras = cell.findall('w:p', ns)
                    print(f"  Cell '{combined[:60]}': {len(paras)} paragraph(s)")
                    if len(paras) == 1:
                        print("  Fix 4: PASS")
                    else:
                        print("  Fix 4: FAIL — expected 1 paragraph")
```

Expected output:
- "Fix 1: PASS — no leading empty paras"
- "Fix 4: PASS"

If Fix 1 or Fix 4 fail, STOP and report — do not proceed.

## Step 3 — Apply Fixes 2, 3, and 5 using Python/lxml

Write this script to `/tmp/ado2732-fixes-235.py` and run it:

```python
import zipfile, lxml.etree as ET, shutil, os

W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
ns = {'w': W}

docx = '/home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/master.docx'

# Extract word/document.xml
tmpdir = '/tmp/ado2732-fix-tmp'
os.makedirs(tmpdir, exist_ok=True)

with zipfile.ZipFile(docx) as z:
    xml = z.read('word/document.xml')

root = ET.fromstring(xml)
tables = root.findall('.//w:tbl', ns)

print(f"Total tables: {len(tables)}")

def get_cell_text(cell):
    texts = [t.text or '' for t in cell.findall('.//w:t', ns)]
    return ''.join(texts)

def consolidate_paras(cell, target_text, fix_name):
    """Move all w:r runs from para[1] into para[0], then remove para[1]."""
    paras = cell.findall('w:p', ns)
    if len(paras) < 2:
        print(f"  {fix_name}: SKIP — cell '{target_text[:50]}' only has {len(paras)} para(s), already consolidated?")
        return
    para0 = paras[0]
    para1 = paras[1]
    # Move all w:r children from para1 to end of para0
    runs = para1.findall('w:r', ns)
    if not runs:
        print(f"  {fix_name}: WARNING — para1 has no w:r runs; checking for other children")
        # Move any children that aren't w:pPr
        for child in list(para1):
            tag = child.tag.split('}')[-1] if '}' in child.tag else child.tag
            if tag != 'pPr':
                para0.append(child)
    else:
        for run in runs:
            para0.append(run)
    # Remove para1
    cell.remove(para1)
    # Verify
    paras_after = cell.findall('w:p', ns)
    print(f"  {fix_name}: Applied — '{target_text[:50]}' now has {len(paras_after)} para(s)")

# Fix 2: Table 7 — {#classSchedule}+{state} onto single para
# Find Table 7 (index 7, 0-based)
if len(tables) > 7:
    tbl7 = tables[7]
    for row in tbl7.findall('w:tr', ns):
        for cell in row.findall('w:tc', ns):
            cell_text = get_cell_text(cell)
            if '{#classSchedule}' in cell_text:
                consolidate_paras(cell, cell_text, 'Fix 2 ({#classSchedule}+{state})')
else:
    print(f"  Fix 2: FAIL — fewer than 8 tables (found {len(tables)})")

# Fix 3: Table 7 — {classEstPremium}+{/classSchedule} onto single para
if len(tables) > 7:
    tbl7 = tables[7]
    for row in tbl7.findall('w:tr', ns):
        for cell in row.findall('w:tc', ns):
            cell_text = get_cell_text(cell)
            if '{classEstPremium}' in cell_text:
                consolidate_paras(cell, cell_text, 'Fix 3 ({classEstPremium}+{/classSchedule})')
else:
    print(f"  Fix 3: FAIL — fewer than 8 tables (found {len(tables)})")

# Fix 5: Table 8 — Form D-43+{/excludedPersons} onto single para
if len(tables) > 8:
    tbl8 = tables[8]
    for row in tbl8.findall('w:tr', ns):
        for cell in row.findall('w:tc', ns):
            cell_text = get_cell_text(cell)
            if 'Form D-43' in cell_text:
                consolidate_paras(cell, cell_text, 'Fix 5 (Form D-43+{/excludedPersons})')
else:
    print(f"  Fix 5: FAIL — fewer than 9 tables (found {len(tables)})")

# Write the modified XML back to a temp file
xml_out = ET.tostring(root, xml_declaration=True, encoding='UTF-8', standalone=True)
tmp_xml = os.path.join(tmpdir, 'document.xml')
with open(tmp_xml, 'wb') as f:
    f.write(xml_out)

# Update the zip in-place
import subprocess
# Copy docx to a safe temp location first
tmp_docx = '/tmp/ado2732-master-tmp.docx'
shutil.copy2(docx, tmp_docx)

# Use zipfile to update the entry
with zipfile.ZipFile(tmp_docx, 'r') as zin:
    names = zin.namelist()

# Re-pack: extract all to temp, replace document.xml, repack
extract_dir = '/tmp/ado2732-extract'
if os.path.exists(extract_dir):
    shutil.rmtree(extract_dir)
os.makedirs(extract_dir)

with zipfile.ZipFile(tmp_docx, 'r') as zin:
    zin.extractall(extract_dir)

# Replace document.xml
shutil.copy2(tmp_xml, os.path.join(extract_dir, 'word', 'document.xml'))

# Repack
output_docx = '/tmp/ado2732-master-fixed.docx'
with zipfile.ZipFile(output_docx, 'w', compression=zipfile.ZIP_DEFLATED) as zout:
    for root_dir, dirs, files in os.walk(extract_dir):
        for file in files:
            file_path = os.path.join(root_dir, file)
            arcname = os.path.relpath(file_path, extract_dir)
            zout.write(file_path, arcname)

print(f"\nFixed docx written to: {output_docx}")
print("Copy to original location with: cp /tmp/ado2732-master-fixed.docx /home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/master.docx")
```

Run the script:
```bash
python3 /tmp/ado2732-fixes-235.py
cp /tmp/ado2732-master-fixed.docx /home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/master.docx
```

## Step 4 — Post-fix verification

Write and run `/tmp/ado2732-verify-post.py`:

```python
import zipfile, lxml.etree as ET

W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
ns = {'w': W}

def is_empty_para(p):
    return len(p.findall('w:r', ns)) == 0 and len(p.findall('w:hyperlink', ns)) == 0

def get_cell_text(cell):
    return ''.join(t.text or '' for t in cell.findall('.//w:t', ns))

docx = '/home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/master.docx'
with zipfile.ZipFile(docx) as z:
    xml = z.read('word/document.xml')

root = ET.fromstring(xml)
tables = root.findall('.//w:tbl', ns)
print(f"Total tables: {len(tables)}")

# Fix 1: no leading empty paras in rec tables
issues = []
for i, tbl in enumerate(tables):
    for j, row in enumerate(tbl.findall('w:tr', ns)):
        for k, cell in enumerate(row.findall('w:tc', ns)):
            paras = cell.findall('w:p', ns)
            if paras and is_empty_para(paras[0]):
                issues.append(f"Table {i}, row {j}, cell {k}")
if issues:
    print(f"Fix 1: FAIL — {len(issues)} leading empty paras: {issues[:5]}")
else:
    print("Fix 1: PASS — 0 leading empty paras")

# Fix 4: {#excludedPersons}{name} — 1 para
for i, tbl in enumerate(tables):
    tbl_text = ET.tostring(tbl, encoding='unicode')
    if '{#excludedPersons}' in tbl_text:
        for row in tbl.findall('w:tr', ns):
            for cell in row.findall('w:tc', ns):
                ct = get_cell_text(cell)
                if '{#excludedPersons}' in ct or '{name}' in ct:
                    pc = len(cell.findall('w:p', ns))
                    status = "PASS" if pc == 1 else f"FAIL (got {pc})"
                    print(f"Fix 4: {status} — '{ct[:60]}' — {pc} para(s)")

# Fix 2: {#classSchedule} cell — 1 para
if len(tables) > 7:
    for row in tables[7].findall('w:tr', ns):
        for cell in row.findall('w:tc', ns):
            ct = get_cell_text(cell)
            if '{#classSchedule}' in ct:
                pc = len(cell.findall('w:p', ns))
                status = "PASS" if pc == 1 else f"FAIL (got {pc})"
                print(f"Fix 2: {status} — '{ct[:60]}' — {pc} para(s)")

# Fix 3: {classEstPremium} cell — 1 para
if len(tables) > 7:
    for row in tables[7].findall('w:tr', ns):
        for cell in row.findall('w:tc', ns):
            ct = get_cell_text(cell)
            if '{classEstPremium}' in ct:
                pc = len(cell.findall('w:p', ns))
                status = "PASS" if pc == 1 else f"FAIL (got {pc})"
                print(f"Fix 3: {status} — '{ct[:60]}' — {pc} para(s)")

# Fix 5: Form D-43 cell — 1 para
if len(tables) > 8:
    for row in tables[8].findall('w:tr', ns):
        for cell in row.findall('w:tc', ns):
            ct = get_cell_text(cell)
            if 'Form D-43' in ct:
                pc = len(cell.findall('w:p', ns))
                status = "PASS" if pc == 1 else f"FAIL (got {pc})"
                print(f"Fix 5: {status} — '{ct[:60]}' — {pc} para(s)")
```

All 5 checks must show PASS before proceeding.

## Important Notes
- Use lxml — do NOT use string replacement
- The docx is at: `services/proposal-generator/templates/verticals/nbais-wc/master.docx`
- Table indices are 0-based (Table 7 = index 7, Table 8 = index 8)
- If any PASS fails, debug and fix before proceeding
- After all verifications pass, just print "ALL FIXES VERIFIED — READY TO COMMIT"
