"""Fill i18n keys for the apply-updates buttons + result toasts."""
import json
from pathlib import Path

LOC = Path(__file__).parent.parent / 'src/Shield.Web/src/i18n/locales'

KEYS = {
    'updates_view.apply_latest_btn': {
        'en': 'Apply latest',
        'nl': 'Latest toepassen',
        'de': 'Latest anwenden',
        'es': 'Aplicar latest',
        'fr': 'Appliquer latest',
    },
    'updates_view.apply_minor_btn': {
        'en': 'Apply minor only',
        'nl': 'Alleen minor toepassen',
        'de': 'Nur Minor anwenden',
        'es': 'Aplicar solo minor',
        'fr': 'Appliquer minor seulement',
    },
    'updates_view.apply_ok': {
        'en': '{prs} PR(s) opened, {failed} failed.',
        'nl': '{prs} PR(s) geopend, {failed} mislukt.',
        'de': '{prs} PR(s) geoeffnet, {failed} fehlgeschlagen.',
        'es': '{prs} PR(s) abiertos, {failed} fallaron.',
        'fr': '{prs} PR(s) ouverte(s), {failed} en echec.',
    },
    'updates_view.apply_no_prs': {
        'en': 'No pull requests were opened.',
        'nl': 'Er zijn geen pull requests geopend.',
        'de': 'Es wurden keine Pull Requests geoeffnet.',
        'es': 'No se abrieron pull requests.',
        'fr': "Aucune pull request n'a ete ouverte.",
    },
    'updates_view.apply_error': {
        'en': 'Failed to apply updates.',
        'nl': 'Updates toepassen mislukt.',
        'de': 'Updates konnten nicht angewendet werden.',
        'es': 'No se pudieron aplicar las actualizaciones.',
        'fr': "Echec de l'application des mises a jour.",
    },
    'updates_view.open_first_pr': {
        'en': 'Open first PR',
        'nl': 'Eerste PR openen',
        'de': 'Erste PR oeffnen',
        'es': 'Abrir primer PR',
        'fr': 'Ouvrir la premiere PR',
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
