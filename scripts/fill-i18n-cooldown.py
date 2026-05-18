"""Fill cooldown-UX i18n keys added in wave 2."""
import json
from pathlib import Path

LOC = Path(__file__).parent.parent / 'src/Shield.Web/src/i18n/locales'

KEYS = {
    'source_detail.bulk_apply_cooldown_tooltip': {
        'en': 'Bulk apply ran recently. Next eligible: {when}.',
        'nl': 'Bulk toepassen is recent uitgevoerd. Volgende mogelijk: {when}.',
        'de': 'Bulk-Apply wurde kuerzlich ausgefuehrt. Naechster Versuch: {when}.',
        'es': 'La aplicacion masiva se ejecuto recientemente. Proximo intento: {when}.',
        'fr': "L'application en masse a ete executee recemment. Prochaine tentative : {when}.",
    },
    'source_detail.bulk_apply_cooldown_badge': {
        'en': 'Cooldown', 'nl': 'Wachttijd', 'de': 'Wartezeit', 'es': 'Enfriamiento', 'fr': 'Pause',
    },
    'source_detail.bulk_apply_cooldown_notice': {
        'en': 'A bulk apply was opened recently. The next manual apply unlocks at {when}, but the server will still let this through if there are open findings newer than the last apply.',
        'nl': 'Er is recent een bulk toegepast. De volgende handmatige run wordt ontgrendeld om {when}, maar de server laat deze toch door als er nieuwe bevindingen zijn sinds de laatste run.',
        'de': 'Vor kurzem wurde bereits ein Bulk-Apply geoeffnet. Der naechste manuelle Lauf wird um {when} freigegeben, der Server laesst diesen aber durch, wenn seitdem neue Funde dazugekommen sind.',
        'es': 'Se abrio recientemente una aplicacion masiva. La proxima ejecucion manual se desbloquea a las {when}, pero el servidor lo dejara pasar si hay hallazgos nuevos desde el ultimo lanzamiento.',
        'fr': 'Une application en masse a ete ouverte recemment. La prochaine execution manuelle se debloque a {when}, mais le serveur laissera passer si de nouvelles decouvertes sont apparues depuis.',
    },
    'source_detail.bulk_apply_cooldown_error': {
        'en': 'Bulk apply on cooldown until {when}. Use "Force" to override.',
        'nl': 'Bulk toepassen wachttijd tot {when}. Gebruik "Forceren" om te overschrijven.',
        'de': 'Bulk-Apply in Wartezeit bis {when}. "Erzwingen" zum Ueberschreiben verwenden.',
        'es': 'Aplicacion masiva en enfriamiento hasta {when}. Usa "Forzar" para anular.',
        'fr': "Application en masse en pause jusqu'a {when}. Utilisez « Forcer » pour outrepasser.",
    },
    'source_detail.bulk_apply_force_btn': {
        'en': 'Force override', 'nl': 'Forceren', 'de': 'Erzwingen', 'es': 'Forzar', 'fr': 'Forcer',
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
