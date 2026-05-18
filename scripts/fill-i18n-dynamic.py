"""Fill dynamic-template-literal i18n keys (findings.bulk.{verb}_done/_error)."""
import json
from pathlib import Path

LOC = Path(__file__).parent.parent / 'src/Shield.Web/src/i18n/locales'

DYNAMIC = {
    'findings.bulk.ack_done': {
        'en': 'Acknowledged {n} findings. | Acknowledged {n} finding. | Acknowledged {n} findings.',
        'nl': '{n} bevindingen bevestigd. | {n} bevinding bevestigd. | {n} bevindingen bevestigd.',
        'de': '{n} Funde bestaetigt. | {n} Fund bestaetigt. | {n} Funde bestaetigt.',
        'es': '{n} hallazgos reconocidos. | {n} hallazgo reconocido. | {n} hallazgos reconocidos.',
        'fr': '{n} decouvertes acquittees. | {n} decouverte acquittee. | {n} decouvertes acquittees.',
    },
    'findings.bulk.resolve_done': {
        'en': 'Resolved {n} findings.',
        'nl': '{n} bevindingen opgelost.',
        'de': '{n} Funde geloest.',
        'es': '{n} hallazgos resueltos.',
        'fr': '{n} decouvertes resolues.',
    },
    'findings.bulk.suppress_done': {
        'en': 'Suppressed {n} findings.',
        'nl': '{n} bevindingen onderdrukt.',
        'de': '{n} Funde unterdrueckt.',
        'es': '{n} hallazgos suprimidos.',
        'fr': '{n} decouvertes supprimees.',
    },
    'findings.bulk.ack_error': {
        'en': 'Failed to acknowledge findings.',
        'nl': 'Bevindingen bevestigen mislukt.',
        'de': 'Funde konnten nicht bestaetigt werden.',
        'es': 'No se pudieron reconocer los hallazgos.',
        'fr': "Echec de l'acquittement des decouvertes.",
    },
    'findings.bulk.resolve_error': {
        'en': 'Failed to resolve findings.',
        'nl': 'Bevindingen oplossen mislukt.',
        'de': 'Funde konnten nicht geloest werden.',
        'es': 'No se pudieron resolver los hallazgos.',
        'fr': 'Echec de la resolution des decouvertes.',
    },
    'findings.bulk.suppress_error': {
        'en': 'Failed to suppress findings.',
        'nl': 'Bevindingen onderdrukken mislukt.',
        'de': 'Funde konnten nicht unterdrueckt werden.',
        'es': 'No se pudieron suprimir los hallazgos.',
        'fr': 'Echec de la suppression des decouvertes.',
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


def get_nested(obj, dotted_key):
    cur = obj
    for p in dotted_key.split('.'):
        if not isinstance(cur, dict) or p not in cur:
            return None
        cur = cur[p]
    return cur


for locale in ['en', 'nl', 'de', 'es', 'fr']:
    path = LOC / f'{locale}.json'
    data = json.loads(path.read_text(encoding='utf-8'))
    added = 0
    for key, table in DYNAMIC.items():
        if get_nested(data, key) is not None:
            continue
        value = table.get(locale, table['en'])
        set_nested(data, key, value)
        added += 1
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'{locale}: +{added}')
