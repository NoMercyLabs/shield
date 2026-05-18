"""Fill i18n keys for the Updates feature (wave 4)."""
import json
from pathlib import Path

LOC = Path(__file__).parent.parent / 'src/Shield.Web/src/i18n/locales'

KEYS = {
    'nav.updates': {
        'en': 'Updates', 'nl': 'Updates', 'de': 'Updates', 'es': 'Actualizaciones', 'fr': 'Mises a jour',
    },
    'updates_view.title': {
        'en': 'Updates', 'nl': 'Updates', 'de': 'Updates', 'es': 'Actualizaciones', 'fr': 'Mises a jour',
    },
    'updates_view.subtitle': {
        'en': 'Outdated direct dependencies across every source.',
        'nl': 'Verouderde directe afhankelijkheden over alle bronnen.',
        'de': 'Veraltete direkte Abhaengigkeiten in allen Quellen.',
        'es': 'Dependencias directas obsoletas en todas las fuentes.',
        'fr': 'Dependances directes obsoletes dans toutes les sources.',
    },
    'updates_view.refresh_all_btn': {
        'en': 'Refresh all',
        'nl': 'Alles vernieuwen',
        'de': 'Alles aktualisieren',
        'es': 'Actualizar todo',
        'fr': 'Tout actualiser',
    },
    'updates_view.refresh_ok': {
        'en': 'Refreshed — {n} updates found.',
        'nl': 'Vernieuwd — {n} updates gevonden.',
        'de': 'Aktualisiert — {n} Updates gefunden.',
        'es': 'Actualizado — {n} actualizaciones encontradas.',
        'fr': 'Actualise — {n} mises a jour trouvees.',
    },
    'updates_view.refresh_error': {
        'en': 'Failed to refresh updates.',
        'nl': 'Updates vernieuwen mislukt.',
        'de': 'Updates konnten nicht aktualisiert werden.',
        'es': 'No se pudo actualizar.',
        'fr': "Echec de l'actualisation des mises a jour.",
    },
    'updates_view.loading': {
        'en': 'Loading updates...',
        'nl': 'Updates laden...',
        'de': 'Updates werden geladen...',
        'es': 'Cargando actualizaciones...',
        'fr': 'Chargement des mises a jour...',
    },
    'updates_view.empty_title': {
        'en': 'No outdated dependencies.',
        'nl': 'Geen verouderde afhankelijkheden.',
        'de': 'Keine veralteten Abhaengigkeiten.',
        'es': 'No hay dependencias obsoletas.',
        'fr': 'Aucune dependance obsolete.',
    },
    'updates_view.empty_hint': {
        'en': 'Trigger a refresh to scan every enabled GitHub source against the package registry cache.',
        'nl': 'Klik op vernieuwen om alle ingeschakelde GitHub-bronnen tegen de pakketregistercache te scannen.',
        'de': 'Klicken Sie auf Aktualisieren, um alle aktivierten GitHub-Quellen gegen den Paketregister-Cache zu pruefen.',
        'es': 'Pulsa actualizar para escanear todas las fuentes de GitHub habilitadas contra la cache del registro de paquetes.',
        'fr': "Appuyez sur actualiser pour scanner toutes les sources GitHub activees par rapport au cache du registre de paquets.",
    },
    'updates_view.outdated_count': {
        'en': '{n} outdated',
        'nl': '{n} verouderd',
        'de': '{n} veraltet',
        'es': '{n} obsoletas',
        'fr': '{n} obsoletes',
    },
    'updates_view.col_package': {
        'en': 'Package', 'nl': 'Pakket', 'de': 'Paket', 'es': 'Paquete', 'fr': 'Paquet',
    },
    'updates_view.col_ecosystem': {
        'en': 'Ecosystem', 'nl': 'Ecosysteem', 'de': 'Oekosystem', 'es': 'Ecosistema', 'fr': 'Ecosysteme',
    },
    'updates_view.col_current': {
        'en': 'Current', 'nl': 'Huidig', 'de': 'Aktuell', 'es': 'Actual', 'fr': 'Actuelle',
    },
    'updates_view.col_latest': {
        'en': 'Latest', 'nl': 'Nieuwste', 'de': 'Neueste', 'es': 'Ultima', 'fr': 'Derniere',
    },
    'updates_view.col_published': {
        'en': 'Published', 'nl': 'Gepubliceerd', 'de': 'Veroeffentlicht', 'es': 'Publicado', 'fr': 'Publiee',
    },
    'updates_view.col_flags': {
        'en': 'Flags', 'nl': 'Vlaggen', 'de': 'Flaggen', 'es': 'Banderas', 'fr': 'Drapeaux',
    },
    'updates_view.flag_major': {
        'en': 'Major', 'nl': 'Major', 'de': 'Major', 'es': 'Mayor', 'fr': 'Majeure',
    },
    'updates_view.flag_too_young': {
        'en': 'Young', 'nl': 'Jong', 'de': 'Jung', 'es': 'Nueva', 'fr': 'Recente',
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
