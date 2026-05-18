"""Fill i18n keys for the live-progress UpdatesView."""
import json
from pathlib import Path

LOC = Path(__file__).parent.parent / 'src/Shield.Web/src/i18n/locales'

KEYS = {
    'updates_view.apply_queued': {
        'en': 'Apply job queued — progress will stream here.',
        'nl': 'Toepasjob in wachtrij — voortgang verschijnt hier.',
        'de': 'Anwendungs-Job in der Warteschlange — Fortschritt erscheint hier.',
        'es': 'Trabajo de aplicacion en cola — el progreso aparecera aqui.',
        'fr': "Tache d'application mise en file — la progression apparaitra ici.",
    },
    'updates_view.job_running': {
        'en': 'Apply running... ({done} sources done)',
        'nl': 'Toepassen bezig... ({done} bronnen klaar)',
        'de': 'Anwendung laeuft... ({done} Quellen fertig)',
        'es': 'Aplicando... ({done} fuentes hechas)',
        'fr': 'Application en cours... ({done} sources terminees)',
    },
    'updates_view.job_finished': {
        'en': 'Apply finished — {done} sources processed.',
        'nl': 'Toepassen klaar — {done} bronnen verwerkt.',
        'de': 'Anwendung fertig — {done} Quellen verarbeitet.',
        'es': 'Aplicacion completada — {done} fuentes procesadas.',
        'fr': 'Application terminee — {done} sources traitees.',
    },
    'updates_view.open_pr': {
        'en': 'Open PR ({n} bumps)',
        'nl': 'PR openen ({n} updates)',
        'de': 'PR oeffnen ({n} Updates)',
        'es': 'Abrir PR ({n} cambios)',
        'fr': 'Ouvrir la PR ({n} mises a jour)',
    },
    'updates_view.no_changes': {
        'en': 'No changes',
        'nl': 'Geen wijzigingen',
        'de': 'Keine Aenderungen',
        'es': 'Sin cambios',
        'fr': 'Aucun changement',
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
