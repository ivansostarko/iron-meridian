"""Regenerates the unit tables in docs/04-UNITS.md from units.json.

The doc is a register: it must state what the data actually says, not what it
said when someone last typed it out. This reads the shipped units.json and
rewrites the tables between the marker comments, so the two cannot drift.
"""
import io, json, os, collections

os.chdir(r'd:\Projects\12 Iron Meridian\iron-meridian')

data = json.load(io.open('Assets/StreamingAssets/Data/units.json', encoding='utf-8'))
units = data['units'] if isinstance(data, dict) and 'units' in data else data

by_cat = collections.OrderedDict()
for u in units:
    by_cat.setdefault(u.get('category', 'Uncategorised'), []).append(u)

CAT_BLURB = {
    'CoreGround':
        'Everything that holds ground. These are the formations the front line is '
        'made of — they take and lose terrain, and the automatic boundary is '
        'derived from where they stand.',
    'Drone':
        'Unmanned systems. They see, jam and strike but do not hold ground, and '
        '`UnitModelLibrary.Resolve` deliberately returns **no 3D model** for them '
        'rather than showing a misleading infantryman.',
}

out = []
out.append('<!-- BEGIN GENERATED UNITS -->')
out.append('')
out.append('> **Generated from `Assets/StreamingAssets/Data/units.json`** — '
           '{} unit types in {} categories. Do not hand-edit the tables below; '
           'edit `scripts/generate_units.py`, re-run it, and regenerate this '
           'section.'.format(len(units), len(by_cat)))
out.append('')

# --- summary ---
out.append('| Category | Types | What it is |')
out.append('|---|---|---|')
for cat, us in by_cat.items():
    out.append('| **{}** | {} | {} |'.format(cat, len(us), CAT_BLURB.get(cat, '')))
out.append('')

def fmt(v):
    if isinstance(v, bool):
        return 'yes' if v else '—'
    if isinstance(v, float):
        return ('%g' % v)
    return str(v)

for cat, us in by_cat.items():
    out.append('---')
    out.append('')
    out.append('### {} ({})'.format(cat, len(us)))
    out.append('')
    if cat in CAT_BLURB:
        out.append(CAT_BLURB[cat])
        out.append('')

    out.append('| id | Name | Atk | Hard | Def | Armr | AA | Range km | See km | Speed | Men | Indirect | Anti-UAS | Support |')
    out.append('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    for u in sorted(us, key=lambda x: x['id']):
        out.append('| `{}` | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} |'.format(
            u['id'], u['name'],
            fmt(u['attack']), fmt(u['hardAttack']), fmt(u['defence']),
            fmt(u['armour']), fmt(u['antiAir']),
            fmt(u['weaponRangeKm']), fmt(u['viewRangeKm']),
            fmt(u['speedKmh']), fmt(u['manpower']),
            fmt(u['canIndirectFire']), fmt(u['canCounterUas']), fmt(u['isSupport'])))
    out.append('')

    out.append('<details><summary>Descriptions and sustainment</summary>')
    out.append('')
    out.append('| id | Description | Ammo | Stock | Fuel | Use/km | Food d | Sup/d | Train | Morale | Org |')
    out.append('|---|---|---|---|---|---|---|---|---|---|---|')
    for u in sorted(us, key=lambda x: x['id']):
        out.append('| `{}` | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} |'.format(
            u['id'], u.get('description', ''),
            u['ammoType'], fmt(u['ammoStock']), fmt(u['fuelStock']), fmt(u['fuelUsePerKm']),
            fmt(u['foodDays']), fmt(u['supplyUsePerDay']),
            fmt(u['training']), fmt(u['morale']), fmt(u['organisation'])))
    out.append('')
    out.append('</details>')
    out.append('')

out.append('<!-- END GENERATED UNITS -->')

block = '\n'.join(out)

doc_path = 'docs/04-UNITS.md'
doc = io.open(doc_path, encoding='utf-8').read()

BEGIN = '<!-- BEGIN GENERATED UNITS -->'
END = '<!-- END GENERATED UNITS -->'

if BEGIN in doc and END in doc:
    head = doc[:doc.index(BEGIN)]
    tail = doc[doc.index(END) + len(END):]
    doc = head + block + tail
else:
    # First run: append the generated section under its own heading.
    doc = doc.rstrip() + '\n\n---\n\n## Unit register (generated)\n\n' + block + '\n'

io.open(doc_path, 'w', encoding='utf-8').write(doc)
print('wrote', doc_path, '-', len(units), 'units,', len(by_cat), 'categories')
