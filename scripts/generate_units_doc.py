"""Regenerates the unit tables in docs/04-UNITS.md from units.json.

The doc is a register: it must state what the data actually says, not what it
said when someone last typed it out. This reads the shipped units.json and
rewrites the tables between the marker comments, so the two cannot drift.
"""
import io, json, os, collections

os.chdir(r'd:\Projects\12 Iron Meridian\iron-meridian')

data = json.load(io.open('Assets/StreamingAssets/Data/units.json', encoding='utf-8'))
units = data['units'] if isinstance(data, dict) and 'units' in data else data

# The register is organised by BRANCH (the arm of service the player sees), not
# by category (how the unit behaves). Category is a per-unit column instead —
# with 117 types, "which arm is this" is the question the doc has to answer
# first, and the CoreGround/Drone/Air/Naval split answers a different one.
BRANCH_ORDER = ['Infantry', 'Armour', 'Mechanised', 'Artillery', 'AntiAircraft',
                'Air', 'Navy', 'Logistics', 'Other']

BRANCH_NAME = {'AntiAircraft': 'Anti-Aircraft'}

BRANCH_BLURB = {
    'Infantry':
        'Dismounted manoeuvre formations. They fight on their feet, carry no '
        'fuel bill, and hold the ground nothing else can reach.',
    'Armour':
        'Tanks, armoured cavalry and the anti-armour arm that exists to kill '
        'them. The breakthrough and the counter to it.',
    'Mechanised':
        'Infantry that rides to the fight. Armour and speed come from the '
        'vehicle, and so does the fuel consumption.',
    'Artillery':
        'The fires branch: everything that shoots at what it cannot see, plus '
        'the observers, radars and cells that tell it where to shoot.',
    'AntiAircraft':
        'Ground-based air and missile defence, with the radars and command and '
        'control that make it a system rather than a pile of launchers.',
    'Air':
        'Crewed aviation and unmanned systems. Neither holds ground; the '
        'crewed types are `category: Air` and the unmanned ones `category: Drone`.',
    'Navy':
        'Vessels. Weeks of fuel and rations aboard, and no terrain to take.',
    'Logistics':
        'Sustainment. Negligible combat value and the reason the rest of the '
        'order of battle is in the field.',
    'Other':
        'Combat support that belongs to no single arm — engineers, signals, '
        'ISR, information, cyber. All of it `isSupport`, and meant to be.',
}

CAT_BLURB = {
    'CoreGround':
        'Stands on the ground. Holds terrain, and `UnitModelLibrary.Resolve` '
        'gives it a ground model (the stand-in rifleman where no equipment of '
        'its own has been imported).',
    'Drone':
        'Unmanned air systems. They see, jam and strike but hold no ground, and '
        'get no infantryman.',
    'Air':
        'Crewed aircraft and helicopters. No terrain, no ground model.',
    'Naval':
        'Vessels. No terrain, no ground model.',
}

by_branch = collections.OrderedDict()
for name in BRANCH_ORDER:
    us = [u for u in units if u.get('branch') == name]
    if us:
        by_branch[name] = us

# Anything with a branch the order above does not know about would otherwise
# vanish from the register silently.
unknown = [u for u in units if u.get('branch') not in BRANCH_ORDER]
if unknown:
    raise SystemExit('units with an unknown branch: ' +
                     ', '.join(u['id'] for u in unknown))

by_cat = collections.OrderedDict()
for u in units:
    by_cat.setdefault(u.get('category', 'Uncategorised'), []).append(u)

def branch_label(name):
    return BRANCH_NAME.get(name, name)

out = []
out.append('<!-- BEGIN GENERATED UNITS -->')
out.append('')
out.append('> **Generated from `Assets/StreamingAssets/Data/units.json`** — '
           '{} unit types across {} branches and {} behaviour categories. Do not '
           'hand-edit the tables below; edit `scripts/generate_units.py`, re-run '
           'it, and run `scripts/generate_units_doc.py` to regenerate this '
           'section.'.format(len(units), len(by_branch), len(by_cat)))
out.append('')

# --- summary: branches ---
out.append('### Branches — the arm of service')
out.append('')
out.append('| Branch | Types | What it is |')
out.append('|---|---|---|')
for name, us in by_branch.items():
    out.append('| **{}** | {} | {} |'.format(branch_label(name), len(us),
                                             BRANCH_BLURB.get(name, '')))
out.append('')

# --- summary: categories ---
out.append('### Categories — how the unit behaves')
out.append('')
out.append('| Category | Types | What it means |')
out.append('|---|---|---|')
for cat in ('CoreGround', 'Drone', 'Air', 'Naval'):
    us = by_cat.get(cat)
    if us:
        out.append('| `{}` | {} | {} |'.format(cat, len(us), CAT_BLURB.get(cat, '')))
out.append('')

def fmt(v):
    if isinstance(v, bool):
        return 'yes' if v else '—'
    if isinstance(v, float):
        return ('%g' % v)
    return str(v)

for name, us in by_branch.items():
    out.append('---')
    out.append('')
    out.append('### {} ({})'.format(branch_label(name), len(us)))
    out.append('')
    if name in BRANCH_BLURB:
        out.append(BRANCH_BLURB[name])
        out.append('')

    out.append('| id | Name | Cat | Atk | Hard | Def | Armr | AA | Range km | See km | Speed | Men | Indirect | Anti-UAS | Support |')
    out.append('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    for u in sorted(us, key=lambda x: x['id']):
        out.append('| `{}` | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} |'.format(
            u['id'], u['name'], u['category'],
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
print('wrote', doc_path, '-', len(units), 'units,', len(by_branch), 'branches,',
      len(by_cat), 'categories')
