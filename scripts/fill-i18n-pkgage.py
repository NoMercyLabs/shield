"""Fill 48h-safeguard i18n keys added in wave 3."""
import json
from pathlib import Path

LOC = Path(__file__).parent.parent / 'src/Shield.Web/src/i18n/locales'

KEYS = {
    'source_detail.field_min_package_age': {
        'en': 'Minimum package age (hours)',
        'nl': 'Minimum pakketleeftijd (uren)',
        'de': 'Mindestalter des Pakets (Stunden)',
        'es': 'Edad minima del paquete (horas)',
        'fr': 'Age minimal du paquet (heures)',
    },
    'source_detail.field_min_package_age_hint': {
        'en': 'Warns when a suggested fix version is younger than this. Advisory-listed fixes ship anyway. 0 disables the warning. Default 48.',
        'nl': 'Waarschuwt wanneer een voorgestelde fix-versie jonger is dan dit. Advisory-fixes worden alsnog toegepast. 0 schakelt de waarschuwing uit. Standaard 48.',
        'de': 'Warnt, wenn eine vorgeschlagene Fix-Version juenger als dieser Wert ist. Advisory-Fixes werden trotzdem ausgeliefert. 0 deaktiviert die Warnung. Standard 48.',
        'es': 'Avisa cuando la version de correccion sugerida es mas joven que esto. Las correcciones de aviso se aplican igualmente. 0 desactiva el aviso. Predeterminado 48.',
        'fr': "Avertit lorsqu'une version corrective suggeree est plus recente que cela. Les correctifs d'avis sont expedies quand meme. 0 desactive l'avertissement. Par defaut 48.",
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
