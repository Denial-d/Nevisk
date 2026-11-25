"""Builds nevishk_lore.json for Unity from the extracted Miro board data."""
import json, re, html, sys
import numpy as np

# Usage: python build_lore.py <out.json> <source dir with board.svg, clean.txt, stencils.json>
OUT = sys.argv[1]
SP = sys.argv[2].rstrip('/') + '/'

board = open(SP + 'board.svg', encoding='utf-8').read()
clean = open(SP + 'clean.txt', encoding='utf-8').read()
stencils = json.load(open(SP + 'stencils.json', encoding='utf-8'))

# ---------- helpers ----------
def doc(id_):
    i = clean.find('##### [doc] id=' + id_)
    j = clean.find('\n##### ', i + 10)
    body = clean[i:j if j > 0 else None].split('\n', 1)[1]
    return md_to_tmp(body)

def md_to_tmp(t):
    t = t.replace('\\#', '#').replace('\\+', '+').replace('\\\\', '\n')
    lines = []
    for ln in t.split('\n'):
        s = ln.strip()
        if s in ('#', ''):
            continue
        m = re.match(r'^#{1,3}\s*(.*)$', s)
        if m:
            s = '<b><size=115%>' + m.group(1) + '</size></b>'
        lines.append(s)
    t = '\n\n'.join(lines)
    t = re.sub(r'\*\*(.+?)\*\*', r'<b>\1</b>', t)
    return t.strip()

def html_to_tmp(h):
    h = re.sub(r'<span style="color: rgb\(255, 220, 74\);">(.*?)</span>', r'<color=#FFDC4A>\1</color>', h, flags=re.S)
    h = re.sub(r'<strong style="color: rgb\(189, 10, 10\);">(.*?)</strong>', r'<color=#E03A3A><b>\1</b></color>', h, flags=re.S)
    h = re.sub(r'<strong style="color: rgb\(255, 220, 74\);">(.*?)</strong>', r'<color=#FFDC4A><b>\1</b></color>', h, flags=re.S)
    h = re.sub(r'<u style="color: rgb\(255, 220, 74\);">(.*?)</u>', r'<color=#FFDC4A><u>\1</u></color>', h, flags=re.S)
    h = re.sub(r'<u style="[^"]*">', '<u>', h)
    h = re.sub(r'<strong style="[^"]*">', '<b>', h)
    h = re.sub(r'<span style="[^"]*">', '', h)
    h = re.sub(r'</span>', '', h)
    h = h.replace('<strong>', '<b>').replace('</strong>', '</b>').replace('<em>', '<i>').replace('</em>', '</i>')
    h = re.sub(r'<br[^>]*>', '\n', h)
    h = re.sub(r'</p>\s*<p>', '\n\n', h)
    h = re.sub(r'</?p>', '', h)
    h = html.unescape(h)
    h = re.sub(r'[ \t]+', ' ', h)
    h = re.sub(r'\n[ ]+', '\n', h)
    h = re.sub(r'\n{3,}', '\n\n', h)
    return h.strip()

def st(id_):
    return html_to_tmp(stencils[id_])

def section(text, start, end=None):
    i = text.find(start)
    assert i >= 0, start
    j = text.find(end, i + 1) if end else -1
    return text[i: j if j > 0 else None].strip()

# ---------- map geometry ----------
# Map B (the copy that sits on the base image) -> base image pixels (1280x581), least-squares fit
P = [((7780,1456),(262,244)),((8437,1533),(368,229)),((7733,1813),(318,293)),((10286,623),(657,58)),
     ((10603,843),(715,99)),((11093,3545),(775,477)),((11726,3378),(877,487)),((10165,2422),(627,362)),
     ((10527,2837),(698,407)),((8851,2321),(440,333)),((8585,3043),(400,422)),((8983,1735),(463,236)),
     ((9263,1880),(498,255)),((11389,3010),(822,426))]
A_ = np.array([[x, y, 1] for (x, y), _ in P]); B_ = np.array([p for _, p in P])
M_B2IMG = np.linalg.lstsq(A_, B_, rcond=None)[0]
IMG_W, IMG_H = 1280, 581

def stickies():
    res = []
    # Attributes may be "double" or 'single' quoted (Miro uses single quotes when the text has a " in it).
    for m in re.finditer(r'<rect ((?:[^>"\']|"[^"]*"|\'[^\']*\')*data-type="sticky"(?:[^>"\']|"[^"]*"|\'[^\']*\')*)/>', board):
        a = {k: (v1 or v2) for k, v1, v2 in re.findall(r'([\w-]+)=(?:"([^"]*)"|\'([^\']*)\')', m.group(1))}
        c = re.sub('<[^>]+>', ' ', html.unescape(a.get('data-content', ''))).strip()
        c = re.sub(r'\s+', ' ', c)
        x, y, w, h = (float(a[k]) for k in ('x', 'y', 'width', 'height'))
        res.append(dict(x=x + w / 2, y=y + h / 2, w=w, color=a.get('data-color'), text=c))
    return res

S = stickies()
mapA = {s['text']: s for s in S if -4100 < s['x'] < 3600 and -3200 < s['y'] < 2800 and s['text'] and len(s['text']) < 60}
mapB_list = [s for s in S if 7400 < s['x'] < 13500 and -400 < s['y'] < 3700 and s['text'] and len(s['text']) < 60]
mapB = {s['text']: s for s in mapB_list}

# Map A -> map B affine from shared landmark names
shared = [n for n in mapA if n in mapB]
A2 = np.array([[mapA[n]['x'], mapA[n]['y'], 1] for n in shared])
B2 = np.array([[mapB[n]['x'], mapB[n]['y']] for n in shared])
M_A2B = np.linalg.lstsq(A2, B2, rcond=None)[0]

def to_uv(bx, by):
    px, py = np.array([bx, by, 1]) @ M_B2IMG
    return [round(float(px) / IMG_W, 4), round(float(py) / IMG_H, 4)]

def uv_from_A(name):
    s = mapA[name]; bx, by = np.array([s['x'], s['y'], 1]) @ M_A2B
    return to_uv(bx, by)

def uv_from_B(name):
    s = mapB[name]; return to_uv(s['x'], s['y'])

CATEGORY = {  # board legend
    'pink': 'production', 'light_green': 'infrastructure', 'black': 'culture',
    'violet': 'education', 'gray': 'gathering', 'dark_green': 'infrastructure'}

# ---------- documents ----------
USH = doc('3458764688807107482')
NT = doc('3458764688807491132')
CORPS = doc('3458764688807491538')
KAM = doc('3458764688807780309')
KR = doc('3458764691372171202')          # Красный электровой (added 2026-11-23)

documents = [
    dict(id='doc_rassvet_journals', title='Записки из лабораторных журналов НИИ «Рассвет»',
         kind='archive', author='', text=doc('3458764685864581342')),
    dict(id='doc_chronotope', title='Хронотоп Невишка (предварительная версия)',
         kind='history', author='', text=doc('3458764686384104754')),
    dict(id='doc_psy_threads', title='Проект «Психореализм»: Нити, Чернота, Сектор 3',
         kind='archive', author='', text=doc('3458764688806261204')),
    dict(id='doc_psy_memory', title='Проект «Психореализм»: журнал руководителя',
         kind='archive', author='', text=doc('3458764688806732190')),
    dict(id='doc_sects', title='Секты ущелья: последователи Амуна, исследователи, Хранители',
         kind='factions', author='', text=doc('3458764688806261711')),
    dict(id='doc_ushchelye_places', title='Ущелье: места', kind='places', author='Настя', text=USH),
    dict(id='doc_ushchelye_official', title='Ущелье: официальная версия', kind='history', author='Настя',
         text=doc('3458764688807309939')),
    dict(id='doc_ushchelye_unofficial', title='Ущелье: неофициальная версия', kind='history', author='Настя',
         text=doc('3458764688807310795')),
    dict(id='doc_nevishk_tech', title='ОЭЗ «Невишк-Тех»: лор от Сала', kind='notes', author='Сало', text=NT),
    dict(id='doc_corporations', title='Корпорации Невишк-Теха: краткое содержание', kind='factions',
         author='Сало', text=CORPS),
    dict(id='doc_balaur_log', title='Журнал дежурств: водно-спасательная станция №2', kind='archive', author='',
         text='\n\n'.join([st('3458764688763297607'), st('3458764688763132887'), st('3458764688763297850'),
                           st('3458764688763514966')])),
    dict(id='doc_krasnyi', title='Красный электровой: история и заметки', kind='places', author='Лёха', text=KR),
    dict(id='doc_psychorealism_terms', title='Психореализм: термины', kind='glossary', author='',
         text=next(s['text'] for s in S if s['text'].startswith('Психореализм, концепция'))),
]

# ---------- districts ----------
# id, board name (map A sticky), display name, owner, status, summary, lore, docs
districts = [
    dict(id='novye_kamyshi', board='1) Новые Камыши', name='Новые Камыши', number=1, color='#2E7D32',
         owner='Сид', status='Complete', summary='Академ-городок',
         text=section(KAM, 'Район "Новые Камыши"').replace('Район "Новые Камыши"', '').strip(),
         docs=['doc_rassvet_journals', 'doc_chronotope']),
    dict(id='krasnyi_elektrovoi', board='2) Красный электровой', name='Красный электровой', number=2,
         color='#C62828', owner='Лёха', status='In Progress', summary='Бывший Tarpo',
         text=section(KR, 'Изначально К. Э.', 'А теперь заметки'), docs=['doc_krasnyi']),
    dict(id='baltag', board='3)Балтаг', name='Балтаг', number=3, color='#F9A825', owner='Илья',
         status='Complete', summary='Город блядей и воинских частей', text='', docs=[]),
    dict(id='mednye_luzhi', board='4)Медные лужи', name='Медные лужи', number=4, color='#EF6C00',
         owner='Макс', status='In Progress', summary='Ботаника', text='', docs=[]),
    dict(id='ushchelye', board='5)Ущелье', name='Ущелье', number=5, color='#9E9E9E', owner='Настя',
         status='Complete', summary='Пространство под виадуком',
         text=section(clean[clean.find('##### [doc] id=3458764688807309939'):], 'Много лет назад', 'Со временем предприятие'),
         docs=['doc_ushchelye_places', 'doc_ushchelye_official', 'doc_ushchelye_unofficial', 'doc_sects',
               'doc_psy_threads', 'doc_psy_memory']),
    dict(id='nevishk_tech', board='6)Невишк-тех', name='Невишк-Тех', number=6, color='#00ACC1', owner='Сало',
         status='Complete', summary='Особая экономическая зона',
         text=section(NT, 'Короче, в западной части', 'Хорошо бы'),
         docs=['doc_nevishk_tech', 'doc_corporations']),
    dict(id='loom_of_echoes', board='7)Loom of Echoes', name='Loom of Echoes', number=7, color='#F48FB1',
         owner='Даник', status='In Progress', summary='', text='', docs=[]),
    dict(id='novyi_tsentr', board='8)Новый центр', name='Новый центр', number=8, color='#81C784', owner='',
         status='In Progress', summary='Здесь происходит всё и ничего одновременно.',
         text=st('3458764688624123214') + '\n\n' + st('3458764688631061386'), docs=['doc_balaur_log']),
    dict(id='imperial', board='9)Империал', name='Империал', number=9, color='#FFF59D', owner='Макс',
         status='In Progress', summary='Старый центр', text='',
         docs=['doc_psy_threads', 'doc_psy_memory']),
]
for d in districts:
    d['uv'] = uv_from_A(d.pop('board'))
# Новый центр sits between Новые Камыши and Империал on the base image; nudge from map-A projection
# Hand-tuned centres for districts drawn on the base image (pixel coords / image size)
HAND = {'novye_kamyshi': (690, 95), 'krasnyi_elektrovoi': (930, 110), 'nevishk_tech': (300, 250),
        'imperial': (505, 235), 'ushchelye': (420, 340), 'mednye_luzhi': (640, 400),
        'loom_of_echoes': (470, 95), 'novyi_tsentr': (720, 180), 'baltag': (900, 280)}
for d in districts:
    if d['id'] in HAND:
        px, py = HAND[d['id']]; d['uv'] = [round(px / IMG_W, 4), round(py / IMG_H, 4)]

# ---------- district photos (Resources/Images; captions from the Miro file names) ----------
IMAGES = {
    'ushchelye': [('ush_plity', 'Безмолвные плиты'), ('ush_vodopad', 'Водопад'), ('ush_podezd', 'Подъезд'),
                  ('ush_alleya', 'Аллея'), ('ush_alleya2', 'Аллея'), ('ush_fontan', 'Фонтан')],
    'nevishk_tech': [('nt_towers', 'Невишк-Тех')],
    'novyi_tsentr': [('nc_prime', 'Невишк Prime City'), ('nc_kofe', 'Кофемырлия'), ('nc_balaur', 'Балаур')],
}
for d in districts:
    d['images'] = [dict(file=f, caption=c) for f, c in IMAGES.get(d['id'], [])]

# ---------- landmarks ----------
def nearest_district(uv):
    return min(districts, key=lambda d: (d['uv'][0] - uv[0]) ** 2 + (d['uv'][1] - uv[1]) ** 2)['id']

LORE = {  # landmark name on board -> (display name, text, docs, district override)
    'Безмолвные плиты': ('Безмолвные плиты', section(USH, 'Безмолвные плиты так называли', 'Старая аллея начиналась'),
                         ['doc_ushchelye_places', 'doc_sects'], 'ushchelye'),
    'Тунель': ('Туннель у водосброса', section(USH, 'Туннель у водосброса', 'Старое кострище.'),
               ['doc_psy_threads'], 'ushchelye'),
    'Старое кострище': ('Старое кострище', section(USH, 'Старое кострище.'), ['doc_sects'], 'ushchelye'),
    'Мёртвый завод': ('Мёртвый завод', section(USH, 'Неподалёку от леса и старого моста', 'Недалеко от старой аллеи'),
                      ['doc_ushchelye_unofficial'], 'ushchelye'),
    'Пещера': ('Пещера', section(USH, 'В глубине леса находилась', 'Самое странное —'), [], 'ushchelye'),
    'Насосная башня': ('Насосная башня', section(USH, 'Самое странное —', 'Туннель у водосброса'),
                       ['doc_psy_threads'], 'ushchelye'),
    'Гидросброс': ('Гидросброс (водопад)', section(USH, 'Водопад начинался', 'Чем дальше от водопада'),
                   ['doc_psy_memory', 'doc_ushchelye_unofficial'], 'ushchelye'),
    'мост': ('Мост', '', [], 'ushchelye'),
    'Академическая усадьба': ('Академическая усадьба / НИИ «Рассвет»',
                              section(KAM, 'Когда-то здесь располагалась', 'В целом это место'),
                              ['doc_rassvet_journals', 'doc_chronotope'], 'novye_kamyshi'),
    'Муты': ('Муты', section(clean[clean.find('##### [doc] id=3458764686384104754'):],
                              'Расселение притесняемой группы', 'Прибытие барина'),
             ['doc_chronotope'], 'novye_kamyshi'),
    'Виброприбор': ('Виброприбор', section(CORPS, 'Корпорация: «Виброприбор»', 'Корпорация: «Creer Solutions»'),
                    ['doc_corporations', 'doc_nevishk_tech'], 'nevishk_tech'),
    'Creer Solutions': ('Creer Solutions', section(CORPS, 'Корпорация: «Creer Solutions»', 'Второстепенные силы'),
                        ['doc_corporations', 'doc_nevishk_tech'], 'nevishk_tech'),
    'Масс-Привод Трест': ('МассПриводТрест', section(NT, 'Есть предприятие, которое производит привод',
                                                     'Компании поменьше'), ['doc_corporations'], 'nevishk_tech'),
    'Cyber-Agro': ('Cyber-Agro', section(NT, 'АгроТех', 'Им симуляции нужны'), ['doc_corporations'], 'nevishk_tech'),
    'Мемориал': ('Мемориал «Народного тестировщика»', section(NT, 'Есть инициативный проект "Euphoria"',
                                                             'Однако за два года'),
                 ['doc_nevishk_tech'], 'nevishk_tech'),
    'Невишк PRIME CITY': ('Невишк Prime City', st('3458764688624705747'), ['doc_balaur_log'], 'novyi_tsentr'),
    'Парк ЛаИзвор': ('Парк Ла Извор', st('3458764688748990325'), ['doc_balaur_log'], 'novyi_tsentr'),
    'Балаур': ('Балаур', st('3458764688757936082'), ['doc_balaur_log'], 'novyi_tsentr'),
    'Брынкуш': ('Брынкуш', '', [], 'novyi_tsentr'),
    'Старый центр': ('Старый центр', '', [], 'imperial'),
    # Красный электровой (document added 2026-11-23)
    'ТЭЦ-1': ('ТЭЦ-1', section(KR, 'Отвлеклись от названия', 'Красный электровой ещё со времён'),
              ['doc_krasnyi'], 'krasnyi_elektrovoi'),
    'Мост идиотов': ('Мост идиотов (мост им. Батонова)', section(KR, 'Бульвар Просвещения ведёт', 'Ещё один крупный мост'),
                     ['doc_krasnyi'], 'krasnyi_elektrovoi'),
    'Седой мост': ('Седой мост', section(KR, 'Ещё один крупный мост', 'Помимо этого'), ['doc_krasnyi'], 'krasnyi_elektrovoi'),
    'Хромая жаба': ('«Хромая жаба»', section(KR, 'Есть в этом районе довольно примечательное', 'Когда-то в Tarpo'),
                    ['doc_krasnyi'], 'krasnyi_elektrovoi'),
    'Хутор': ('Недохутор', section(KR, 'Когда-то в Tarpo'), ['doc_krasnyi'], 'krasnyi_elektrovoi'),
    'БЛЯ': ('Б.Л.Я. — Большой лекторий яблоководства',
            section(KR, 'Позже на месте основных столкновений', 'До присоединения') + '\n\n' +
            section(KR, 'Несмотря на описанную мирность', '2. Основные'), ['doc_krasnyi'], 'krasnyi_elektrovoi'),
    'ЕБНУ': ('Е.Б.Н.У. — Единое бюджетное нефте-газовое училище',
             section(KR, 'Позже на месте основных столкновений', 'До присоединения') + '\n\n' +
             section(KR, 'Несмотря на описанную мирность', '2. Основные'), ['doc_krasnyi'], 'krasnyi_elektrovoi'),
    'Бульвар Просвещения': ('Бульвар Просвещения',
                            section(KR, 'Позже на месте основных столкновений', 'До присоединения') + '\n\n' +
                            section(KR, 'Основные бульвары', 'Был такой архитектор'), ['doc_krasnyi'], 'krasnyi_elektrovoi'),
    'Улица Робеспьера': ('Улица Максимилиана Робеспьера',
                         section(KR, 'Позже на месте основных столкновений', 'До присоединения') + '\n\n' +
                         section(KR, 'Несмотря на описанную мирность', '2. Основные'), ['doc_krasnyi'], 'krasnyi_elektrovoi'),
    'Аллея: "Память"': ('Аллея «Память»', section(USH, 'Старая аллея начиналась', 'Водопад начинался'),
                        ['doc_ushchelye_places'], 'ushchelye'),
    'Фонтан: "забвение"': ('Фонтан «Забвение»', section(USH, 'Недалеко от старой аллеи', 'Мусор был'),
                           ['doc_ushchelye_places'], 'ushchelye'),
}

landmarks = []
seen = set()
for s in mapB_list:
    name = s['text']
    key = re.sub(r'\s+', ' ', name).strip()
    if key.isdigit() or (key == 'Пещера' and s['y'] < 0):
        continue
    uv = to_uv(s['x'], s['y'])
    disp, text, docs, dist = LORE.get(key, (key, '', [], None))
    landmarks.append(dict(
        id='lm_' + str(len(landmarks)).zfill(3), name=disp, category=CATEGORY.get(s['color'], 'gathering'),
        district=dist or nearest_district(uv), uv=uv, text=text, docs=docs))

# Landmark photos, keyed by display name so they survive id renumbering.
LANDMARK_IMAGES = {
    "Виброприбор": [
        [
            "nt_towers",
            "Виброприбор"
        ]
    ],
    "Безмолвные плиты": [
        [
            "ush_plity",
            "Безмолвные плиты"
        ],
        [
            "ush_podezd",
            "Подъезд"
        ]
    ],
    "Аллея «Память»": [
        [
            "ush_alleya",
            "Аллея"
        ],
        [
            "ush_alleya2",
            "Аллея"
        ]
    ],
    "Гидросброс (водопад)": [
        [
            "ush_vodopad",
            "Водопад"
        ]
    ],
    "Фонтан «Забвение»": [
        [
            "ush_fontan",
            "Фонтан"
        ]
    ],
    "Невишк Prime City": [
        [
            "nc_prime",
            "Невишк Prime City"
        ],
        [
            "nc_kofe",
            "Кофемырлия, первый этаж «Обсидиана»"
        ]
    ],
    "Балаур": [
        [
            "nc_balaur",
            "Балаур"
        ]
    ]
}
for l in landmarks:
    l['images'] = [dict(file=f, caption=c) for f, c in LANDMARK_IMAGES.get(l['name'], [])]

# ---------- hand-written district lore (Tools/nevishk/lore_*.json) ----------
# Districts the board has no text for, written directly: district fields are replaced, places appended.
import glob as _glob, os as _os
def merge_extra(districts, landmarks, folder):
    for f in sorted(_glob.glob(_os.path.join(folder, 'lore_*.json'))):
        extra = json.load(open(f, encoding='utf-8'))
        dx = extra.get('district')
        if dx:
            for d in districts:
                if d['id'] == dx['id']:
                    d.update({k: v for k, v in dx.items() if k != 'id'})
        ids = {l['id'] for l in landmarks}
        for l in extra.get('landmarks', []):
            if l['id'] in ids: continue
            l = dict(l); l.setdefault('district', dx['id'] if dx else None)
            l.setdefault('docs', []); l.setdefault('images', [])
            landmarks.append(l)
merge_extra(districts, landmarks, _os.path.dirname(_os.path.abspath(__file__)))

data = dict(
    city=dict(name='Невишк', tagline='Невишк — столица республики Тамаш', mapImage='nevishk_basemap',
              mapPixelSize=[IMG_W, IMG_H]),
    categories=[
        dict(id='production', name='Производства', color='#E57399'),
        dict(id='infrastructure', name='Инфраструктура', color='#6CC07A'),
        dict(id='culture', name='Объекты культуры', color='#2B2B2B'),
        dict(id='education', name='Образовательные учреждения', color='#8E6CCF'),
        dict(id='gathering', name='Собирательные локации', color='#A0A0A0')],
    districts=districts, landmarks=landmarks, documents=documents)

json.dump(data, open(OUT, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('districts', len(districts), 'landmarks', len(landmarks), 'docs', len(documents))
print('with lore:', sum(1 for l in landmarks if l['text']))
for d in districts: print(d['name'], d['uv'])
