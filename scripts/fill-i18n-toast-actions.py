"""Fill toast-action i18n keys (clickable PR links in success toasts)."""
import json
from pathlib import Path

LOC = Path(__file__).parent.parent / 'src/Shield.Web/src/i18n/locales'

KEYS = {
    'source_detail.bulk_apply_pr_opened_short': {
        'en': 'Pull request opened.',
        'nl': 'Pull request geopend.',
        'de': 'Pull Request geoeffnet.',
        'es': 'Pull request abierto.',
        'fr': 'Pull request ouverte.',
    },
    'source_detail.bulk_apply_open_pr_btn': {
        'en': 'Open PR', 'nl': 'PR openen', 'de': 'PR oeffnen', 'es': 'Abrir PR', 'fr': 'Ouvrir la PR',
    },
    'finding_detail.fix_pr_opened': {
        'en': 'Pull request opened.',
        'nl': 'Pull request geopend.',
        'de': 'Pull Request geoeffnet.',
        'es': 'Pull request abierto.',
        'fr': 'Pull request ouverte.',
    },
}


def set_nested(obj, dotted_key, value):
    parts = dotted_key.split('.')
    cur = obj
    for p in parts[:-1]:
        if p not in cur or not isinstance(cur[p], dict):
            cur[p] = {}
        cur = cur[p]
    cur[parts[-1]] = value


for locale in ['en', 'nl', 'de', 'es', 'fr']:
    path = LOC / f'{locale}.json'
    data = json.loads(path.read_text(encoding='utf-8'))
    for key, table in KEYS.items():
        set_nested(data, key, table.get(locale, table['en']))
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'{locale}: +{len(KEYS)}')
