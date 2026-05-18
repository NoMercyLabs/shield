"""Fill all missing i18n keys across en/nl/de/es/fr with native translations."""
import json
from pathlib import Path
from copy import deepcopy

LOC = Path(__file__).parent.parent / 'src/Shield.Web/src/i18n/locales'

# Master translation table: key -> {locale: value}
TRANSLATIONS = {
    # ─── action.confirm ──────────────────────────────────────────
    'action.confirm': {
        'en': 'Confirm', 'nl': 'Bevestigen', 'de': 'Bestaetigen', 'es': 'Confirmar', 'fr': 'Confirmer',
    },

    # ─── finding_detail ──────────────────────────────────────────
    'finding_detail.suppress_reason': {
        'en': 'Suppress reason', 'nl': 'Reden voor onderdrukken',
        'de': 'Grund fuer Unterdrueckung', 'es': 'Motivo de supresion', 'fr': 'Motif de suppression',
    },
    'finding_detail.field_first_seen': {
        'en': 'First seen', 'nl': 'Eerst gezien', 'de': 'Zuerst gesehen', 'es': 'Visto por primera vez', 'fr': 'Vu pour la premiere fois',
    },
    'finding_detail.field_last_seen': {
        'en': 'Last seen', 'nl': 'Laatst gezien', 'de': 'Zuletzt gesehen', 'es': 'Visto por ultima vez', 'fr': 'Vu pour la derniere fois',
    },
    'finding_detail.field_notes': {
        'en': 'Notes', 'nl': 'Notities', 'de': 'Notizen', 'es': 'Notas', 'fr': 'Notes',
    },
    'finding_detail.field_state': {
        'en': 'State', 'nl': 'Status', 'de': 'Status', 'es': 'Estado', 'fr': 'Etat',
    },
    'finding_detail.fix_apply': {
        'en': 'Apply fix', 'nl': 'Fix toepassen', 'de': 'Fix anwenden', 'es': 'Aplicar correccion', 'fr': 'Appliquer le correctif',
    },
    'finding_detail.fix_files_changed': {
        'en': 'Applied fix ({files} file changed). | Applied fix ({files} files changed).',
        'nl': 'Fix toegepast ({files} bestand gewijzigd). | Fix toegepast ({files} bestanden gewijzigd).',
        'de': 'Fix angewendet ({files} Datei geaendert). | Fix angewendet ({files} Dateien geaendert).',
        'es': 'Correccion aplicada ({files} archivo modificado). | Correccion aplicada ({files} archivos modificados).',
        'fr': 'Correctif applique ({files} fichier modifie). | Correctif applique ({files} fichiers modifies).',
    },
    'finding_detail.fix_follow_up': {
        'en': ' - run `{cmd}`', 'nl': ' - voer `{cmd}` uit', 'de': ' - `{cmd}` ausfuehren', 'es': ' - ejecutar `{cmd}`', 'fr': ' - executer `{cmd}`',
    },
    'finding_detail.fix_not_available': {
        'en': 'No known fix is available for this advisory yet.',
        'nl': 'Er is nog geen bekende fix voor deze advisory.',
        'de': 'Fuer dieses Advisory ist noch keine Fix bekannt.',
        'es': 'Aun no hay correccion conocida para este aviso.',
        'fr': "Aucun correctif connu n'est encore disponible pour cet avis.",
    },
    'finding_detail.fix_notes_prefix': {
        'en': 'Notes:', 'nl': 'Notities:', 'de': 'Notizen:', 'es': 'Notas:', 'fr': 'Notes :',
    },
    'finding_detail.fix_open_pr': {
        'en': 'Open pull request', 'nl': 'Open pull request', 'de': 'Pull Request oeffnen', 'es': 'Abrir pull request', 'fr': 'Ouvrir la pull request',
    },
    'finding_detail.fix_source_unsupported': {
        'en': 'Source type does not support automatic fixes yet.',
        'nl': 'Dit brontype ondersteunt nog geen automatische fixes.',
        'de': 'Dieser Quellentyp unterstuetzt noch keine automatischen Fixes.',
        'es': 'Este tipo de fuente aun no admite correcciones automaticas.',
        'fr': "Ce type de source ne prend pas encore en charge les correctifs automatiques.",
    },
    'finding_detail.readonly_notice': {
        'en': 'You have read-only access to this source. Ask an admin for Triage permission to acknowledge, resolve or suppress findings.',
        'nl': 'Je hebt alleen-lezen toegang tot deze bron. Vraag een beheerder om Triage-rechten om bevindingen te bevestigen, op te lossen of te onderdrukken.',
        'de': 'Sie haben nur lesenden Zugriff auf diese Quelle. Bitten Sie einen Admin um Triage-Rechte, um Funde zu bestaetigen, zu loesen oder zu unterdruecken.',
        'es': 'Tienes acceso de solo lectura a esta fuente. Pide a un administrador permiso de Triage para confirmar, resolver o suprimir hallazgos.',
        'fr': "Vous avez un acces en lecture seule a cette source. Demandez a un administrateur l'autorisation Triage pour acquitter, resoudre ou supprimer les decouvertes.",
    },
    'finding_detail.suppress_reason_label': {
        'en': 'Suppress reason', 'nl': 'Reden voor onderdrukken', 'de': 'Grund fuer Unterdrueckung',
        'es': 'Motivo de supresion', 'fr': 'Motif de suppression',
    },
    'finding_detail.triage_error': {
        'en': 'Failed to {verb}.', 'nl': '{verb} mislukt.', 'de': '{verb} fehlgeschlagen.',
        'es': 'Error al {verb}.', 'fr': 'Echec de {verb}.',
    },
    'finding_detail.triage_toast': {
        'en': 'Finding {verb}ed.', 'nl': 'Bevinding {verb}.', 'de': 'Fund {verb}.',
        'es': 'Hallazgo {verb}.', 'fr': 'Decouverte {verb}.',
    },

    # ─── findings.bulk ────────────────────────────────────────────
    'findings.bulk.ack_btn': {
        'en': 'Acknowledge', 'nl': 'Bevestigen', 'de': 'Bestaetigen', 'es': 'Reconocer', 'fr': 'Acquitter',
    },
    'findings.bulk.resolve_btn': {
        'en': 'Resolve', 'nl': 'Oplossen', 'de': 'Loesen', 'es': 'Resolver', 'fr': 'Resoudre',
    },
    'findings.bulk.selected': {
        'en': '{n} selected', 'nl': '{n} geselecteerd', 'de': '{n} ausgewaehlt', 'es': '{n} seleccionados', 'fr': '{n} selectionnes',
    },
    'findings.bulk.suppress_btn': {
        'en': 'Suppress', 'nl': 'Onderdrukken', 'de': 'Unterdruecken', 'es': 'Suprimir', 'fr': 'Supprimer',
    },

    # ─── findings.col_* ───────────────────────────────────────────
    'findings.col_advisory': {
        'en': 'Advisory', 'nl': 'Advisory', 'de': 'Advisory', 'es': 'Aviso', 'fr': 'Avis',
    },
    'findings.col_last_seen': {
        'en': 'Last seen', 'nl': 'Laatst gezien', 'de': 'Zuletzt gesehen', 'es': 'Visto por ultima vez', 'fr': 'Vu en dernier',
    },
    'findings.col_package': {
        'en': 'Package', 'nl': 'Pakket', 'de': 'Paket', 'es': 'Paquete', 'fr': 'Paquet',
    },
    'findings.col_severity': {
        'en': 'Severity', 'nl': 'Ernst', 'de': 'Schweregrad', 'es': 'Severidad', 'fr': 'Severite',
    },
    'findings.col_source': {
        'en': 'Source', 'nl': 'Bron', 'de': 'Quelle', 'es': 'Fuente', 'fr': 'Source',
    },
    'findings.col_state': {
        'en': 'State', 'nl': 'Status', 'de': 'Status', 'es': 'Estado', 'fr': 'Etat',
    },
    'findings.page_of_total': {
        'en': 'Page {page} of {total}', 'nl': 'Pagina {page} van {total}', 'de': 'Seite {page} von {total}',
        'es': 'Pagina {page} de {total}', 'fr': 'Page {page} sur {total}',
    },

    # ─── onboarding ───────────────────────────────────────────────
    'onboarding.channels_already_configured': {
        'en': 'Notification channels already configured.',
        'nl': 'Meldingskanalen zijn al geconfigureerd.',
        'de': 'Benachrichtigungskanaele bereits konfiguriert.',
        'es': 'Canales de notificacion ya configurados.',
        'fr': 'Canaux de notification deja configures.',
    },
    'onboarding.discord_channel_name': {
        'en': 'Discord channel name',
        'nl': 'Discord-kanaalnaam',
        'de': 'Discord-Kanalname',
        'es': 'Nombre del canal de Discord',
        'fr': 'Nom du canal Discord',
    },
    'onboarding.discord_test_error': {
        'en': 'Test message failed to send.',
        'nl': 'Testbericht verzenden mislukt.',
        'de': 'Testnachricht konnte nicht gesendet werden.',
        'es': 'No se pudo enviar el mensaje de prueba.',
        'fr': "L'envoi du message de test a echoue.",
    },
    'onboarding.discord_test_sent': {
        'en': 'Test message sent to Discord.',
        'nl': 'Testbericht naar Discord verzonden.',
        'de': 'Testnachricht an Discord gesendet.',
        'es': 'Mensaje de prueba enviado a Discord.',
        'fr': 'Message de test envoye sur Discord.',
    },
    'onboarding.discord_webhook_label': {
        'en': 'Discord webhook URL',
        'nl': 'Discord webhook-URL',
        'de': 'Discord Webhook-URL',
        'es': 'URL del webhook de Discord',
        'fr': 'URL du webhook Discord',
    },
    'onboarding.done_channels_ok': {
        'en': 'Notification channel ready.',
        'nl': 'Meldingskanaal gereed.',
        'de': 'Benachrichtigungskanal bereit.',
        'es': 'Canal de notificacion listo.',
        'fr': 'Canal de notification pret.',
    },
    'onboarding.done_manage_sources': {
        'en': 'Manage sources',
        'nl': 'Bronnen beheren',
        'de': 'Quellen verwalten',
        'es': 'Gestionar fuentes',
        'fr': 'Gerer les sources',
    },
    'onboarding.done_open_settings': {
        'en': 'Open settings',
        'nl': 'Instellingen openen',
        'de': 'Einstellungen oeffnen',
        'es': 'Abrir ajustes',
        'fr': 'Ouvrir les parametres',
    },
    'onboarding.done_sources_ok': {
        'en': 'Sources connected.',
        'nl': 'Bronnen verbonden.',
        'de': 'Quellen verbunden.',
        'es': 'Fuentes conectadas.',
        'fr': 'Sources connectees.',
    },
    'onboarding.step_channel': {
        'en': 'Notification channel',
        'nl': 'Meldingskanaal',
        'de': 'Benachrichtigungskanal',
        'es': 'Canal de notificacion',
        'fr': 'Canal de notification',
    },
    'onboarding.step_done': {
        'en': 'Done',
        'nl': 'Klaar',
        'de': 'Fertig',
        'es': 'Listo',
        'fr': 'Termine',
    },
    'onboarding.step_source': {
        'en': 'Add a source',
        'nl': 'Bron toevoegen',
        'de': 'Quelle hinzufuegen',
        'es': 'Anadir fuente',
        'fr': 'Ajouter une source',
    },
    'onboarding.step_welcome': {
        'en': 'Welcome',
        'nl': 'Welkom',
        'de': 'Willkommen',
        'es': 'Bienvenido',
        'fr': 'Bienvenue',
    },
    'onboarding.test_send_failed': {
        'en': 'Failed to send test message.',
        'nl': 'Testbericht verzenden mislukt.',
        'de': 'Testnachricht konnte nicht gesendet werden.',
        'es': 'No se pudo enviar el mensaje de prueba.',
        'fr': "Echec d'envoi du message de test.",
    },
    'onboarding.test_sent_confirm': {
        'en': 'Test message sent.',
        'nl': 'Testbericht verzonden.',
        'de': 'Testnachricht gesendet.',
        'es': 'Mensaje de prueba enviado.',
        'fr': 'Message de test envoye.',
    },
    'onboarding.webhook_url_help': {
        'en': 'Paste the full webhook URL from Discord channel settings.',
        'nl': 'Plak de volledige webhook-URL uit Discord-kanaalinstellingen.',
        'de': 'Fuegen Sie die vollstaendige Webhook-URL aus den Discord-Kanaleinstellungen ein.',
        'es': 'Pega la URL completa del webhook desde los ajustes del canal de Discord.',
        'fr': "Collez l'URL complete du webhook depuis les parametres du canal Discord.",
    },
    'onboarding.webhook_url_hint': {
        'en': 'Looks like https://discord.com/api/webhooks/...',
        'nl': 'Ziet eruit als https://discord.com/api/webhooks/...',
        'de': 'Sieht aus wie https://discord.com/api/webhooks/...',
        'es': 'Se ve como https://discord.com/api/webhooks/...',
        'fr': 'Ressemble a https://discord.com/api/webhooks/...',
    },
    'onboarding.welcome_skip_hint': {
        'en': 'You can configure these later in Settings.',
        'nl': 'Je kunt dit later instellen via Instellingen.',
        'de': 'Sie koennen dies spaeter in den Einstellungen konfigurieren.',
        'es': 'Puedes configurarlo mas tarde en Ajustes.',
        'fr': 'Vous pouvez configurer cela plus tard dans les Parametres.',
    },

    # ─── settings_oauth ──────────────────────────────────────────
    'settings_oauth.clear_secret_label': {
        'en': 'Clear stored secret',
        'nl': 'Opgeslagen geheim wissen',
        'de': 'Gespeichertes Geheimnis loeschen',
        'es': 'Borrar secreto guardado',
        'fr': 'Effacer le secret enregistre',
    },
    'settings_oauth.client_id_label': {
        'en': 'Client ID', 'nl': 'Client-ID', 'de': 'Client-ID', 'es': 'ID de cliente', 'fr': 'ID client',
    },
    'settings_oauth.client_secret_label': {
        'en': 'Client secret', 'nl': 'Clientgeheim', 'de': 'Client-Geheimnis', 'es': 'Secreto de cliente', 'fr': 'Secret client',
    },
    'settings_oauth.slack_configure_summary': {
        'en': 'Configure Slack OAuth credentials for signin.',
        'nl': 'Slack OAuth-inloggegevens voor aanmelden configureren.',
        'de': 'Slack OAuth-Anmeldedaten fuer die Anmeldung konfigurieren.',
        'es': 'Configurar credenciales OAuth de Slack para inicio de sesion.',
        'fr': "Configurer les identifiants OAuth Slack pour la connexion.",
    },

    # ─── source_detail ───────────────────────────────────────────
    'source_detail.bulk_apply_allow_major': {
        'en': 'Include major version bumps',
        'nl': 'Major versie-upgrades meenemen',
        'de': 'Major-Versionsspruenge einbeziehen',
        'es': 'Incluir saltos de version mayor',
        'fr': 'Inclure les sauts de version majeure',
    },
    'source_detail.bulk_apply_confirm': {
        'en': 'Open pull request',
        'nl': 'Pull request openen',
        'de': 'Pull Request oeffnen',
        'es': 'Abrir pull request',
        'fr': 'Ouvrir la pull request',
    },
    'source_detail.bulk_apply_confirm_production': {
        'en': 'I understand this targets a production source.',
        'nl': 'Ik begrijp dat dit een productiebron raakt.',
        'de': 'Ich verstehe, dass dies eine Produktionsquelle betrifft.',
        'es': 'Entiendo que esto afecta a una fuente de produccion.',
        'fr': "Je comprends que cela affecte une source de production.",
    },
    'source_detail.bulk_apply_error': {
        'en': 'Bulk apply failed.',
        'nl': 'Bulk toepassen mislukt.',
        'de': 'Massenanwendung fehlgeschlagen.',
        'es': 'La aplicacion masiva fallo.',
        'fr': 'Application en masse echouee.',
    },
    'source_detail.bulk_apply_errors': {
        'en': 'Errors',
        'nl': 'Fouten',
        'de': 'Fehler',
        'es': 'Errores',
        'fr': 'Erreurs',
    },
    'source_detail.bulk_apply_major_bumps': {
        'en': 'Major version bumps',
        'nl': 'Major versie-upgrades',
        'de': 'Major-Versionsspruenge',
        'es': 'Saltos de version mayor',
        'fr': 'Sauts de version majeure',
    },
    'source_detail.bulk_apply_modal_title': {
        'en': 'Bulk apply fixes',
        'nl': 'Fixes in bulk toepassen',
        'de': 'Fixes in Massen anwenden',
        'es': 'Aplicar correcciones en masa',
        'fr': 'Appliquer les correctifs en masse',
    },
    'source_detail.bulk_apply_no_pr': {
        'en': 'No pull request was created.',
        'nl': 'Er is geen pull request aangemaakt.',
        'de': 'Es wurde kein Pull Request erstellt.',
        'es': 'No se creo ningun pull request.',
        'fr': "Aucune pull request n'a ete creee.",
    },
    'source_detail.bulk_apply_nothing': {
        'en': 'Nothing to apply.',
        'nl': 'Niets om toe te passen.',
        'de': 'Nichts anzuwenden.',
        'es': 'Nada que aplicar.',
        'fr': 'Rien a appliquer.',
    },
    'source_detail.bulk_apply_opening_pr': {
        'en': 'Opening pull request...',
        'nl': 'Pull request openen...',
        'de': 'Pull Request wird geoeffnet...',
        'es': 'Abriendo pull request...',
        'fr': 'Ouverture de la pull request...',
    },
    'source_detail.bulk_apply_pr_opened': {
        'en': 'Pull request opened.',
        'nl': 'Pull request geopend.',
        'de': 'Pull Request geoeffnet.',
        'es': 'Pull request abierto.',
        'fr': 'Pull request ouverte.',
    },
    'source_detail.bulk_apply_preview_error': {
        'en': 'Failed to preview bulk apply.',
        'nl': 'Voorvertoning van bulk toepassen mislukt.',
        'de': 'Vorschau der Massenanwendung fehlgeschlagen.',
        'es': 'No se pudo previsualizar la aplicacion masiva.',
        'fr': "Echec de l'apercu de l'application en masse.",
    },
    'source_detail.bulk_apply_warnings': {
        'en': 'Warnings',
        'nl': 'Waarschuwingen',
        'de': 'Warnungen',
        'es': 'Advertencias',
        'fr': 'Avertissements',
    },
    'source_detail.is_production_badge': {
        'en': 'Production',
        'nl': 'Productie',
        'de': 'Produktion',
        'es': 'Produccion',
        'fr': 'Production',
    },
    'source_detail.is_production_error': {
        'en': 'Failed to update production flag.',
        'nl': 'Productievlag bijwerken mislukt.',
        'de': 'Produktions-Flag konnte nicht aktualisiert werden.',
        'es': 'No se pudo actualizar el indicador de produccion.',
        'fr': "Echec de la mise a jour de l'indicateur de production.",
    },
    'source_detail.mark_production_title': {
        'en': 'Mark as production',
        'nl': 'Markeren als productie',
        'de': 'Als Produktion markieren',
        'es': 'Marcar como produccion',
        'fr': 'Marquer comme production',
    },
    'source_detail.unmark_production_title': {
        'en': 'Unmark production',
        'nl': 'Productiemarkering verwijderen',
        'de': 'Produktionsmarkierung entfernen',
        'es': 'Desmarcar produccion',
        'fr': 'Retirer le marquage production',
    },
    'source_detail.cancel_edit': {
        'en': 'Cancel', 'nl': 'Annuleren', 'de': 'Abbrechen', 'es': 'Cancelar', 'fr': 'Annuler',
    },
    'source_detail.col_ecosystem': {
        'en': 'Ecosystem', 'nl': 'Ecosysteem', 'de': 'Oekosystem', 'es': 'Ecosistema', 'fr': 'Ecosysteme',
    },
    'source_detail.col_package': {
        'en': 'Package', 'nl': 'Pakket', 'de': 'Paket', 'es': 'Paquete', 'fr': 'Paquet',
    },
    'source_detail.col_type': {
        'en': 'Type', 'nl': 'Type', 'de': 'Typ', 'es': 'Tipo', 'fr': 'Type',
    },
    'source_detail.col_version': {
        'en': 'Version', 'nl': 'Versie', 'de': 'Version', 'es': 'Version', 'fr': 'Version',
    },
    'source_detail.compare_btn_title': {
        'en': 'Run diff', 'nl': 'Diff uitvoeren', 'de': 'Diff ausfuehren', 'es': 'Ejecutar diff', 'fr': 'Lancer le diff',
    },
    'source_detail.compare_btn_title_disabled': {
        'en': 'Pick two distinct snapshots', 'nl': 'Kies twee verschillende snapshots',
        'de': 'Zwei unterschiedliche Snapshots auswaehlen', 'es': 'Elige dos snapshots distintos',
        'fr': 'Choisissez deux snapshots distincts',
    },
    'source_detail.compare_newer': {
        'en': 'Newer', 'nl': 'Nieuwer', 'de': 'Neuer', 'es': 'Mas reciente', 'fr': 'Plus recent',
    },
    'source_detail.compare_older': {
        'en': 'Older', 'nl': 'Ouder', 'de': 'Aelter', 'es': 'Mas antiguo', 'fr': 'Plus ancien',
    },
    'source_detail.compare_pick_error': {
        'en': 'Pick two distinct snapshots to compare.',
        'nl': 'Kies twee verschillende snapshots om te vergelijken.',
        'de': 'Zwei unterschiedliche Snapshots zum Vergleich auswaehlen.',
        'es': 'Elige dos snapshots distintos para comparar.',
        'fr': 'Choisissez deux snapshots distincts a comparer.',
    },
    'source_detail.delete_confirm': {
        'en': 'Delete source "{name}"? Snapshots and findings for this source will be deleted too.',
        'nl': 'Bron "{name}" verwijderen? Snapshots en bevindingen voor deze bron worden ook verwijderd.',
        'de': 'Quelle "{name}" loeschen? Snapshots und Funde fuer diese Quelle werden ebenfalls geloescht.',
        'es': '¿Eliminar la fuente "{name}"? Tambien se eliminaran los snapshots y hallazgos.',
        'fr': 'Supprimer la source "{name}" ? Les snapshots et decouvertes de cette source seront aussi supprimes.',
    },
    'source_detail.delete_ok': {
        'en': 'Source "{name}" deleted.',
        'nl': 'Bron "{name}" verwijderd.',
        'de': 'Quelle "{name}" geloescht.',
        'es': 'Fuente "{name}" eliminada.',
        'fr': 'Source "{name}" supprimee.',
    },
    'source_detail.dep_direct': {
        'en': 'direct', 'nl': 'direct', 'de': 'direkt', 'es': 'directa', 'fr': 'directe',
    },
    'source_detail.dep_transitive': {
        'en': 'transitive', 'nl': 'transitief', 'de': 'transitiv', 'es': 'transitiva', 'fr': 'transitive',
    },
    'source_detail.diff_added_none': {
        'en': 'No new dependencies.', 'nl': 'Geen nieuwe afhankelijkheden.',
        'de': 'Keine neuen Abhaengigkeiten.', 'es': 'Sin nuevas dependencias.', 'fr': 'Aucune nouvelle dependance.',
    },
    'source_detail.diff_added_title': {
        'en': 'Added ({n})', 'nl': 'Toegevoegd ({n})', 'de': 'Hinzugefuegt ({n})', 'es': 'Anadidas ({n})', 'fr': 'Ajoutees ({n})',
    },
    'source_detail.diff_bumped_none': {
        'en': 'No bumps.', 'nl': 'Geen upgrades.', 'de': 'Keine Versionsspruenge.', 'es': 'Sin cambios de version.', 'fr': 'Aucun changement de version.',
    },
    'source_detail.diff_bumped_title': {
        'en': 'Version changed ({n})', 'nl': 'Versie gewijzigd ({n})',
        'de': 'Version geaendert ({n})', 'es': 'Version cambiada ({n})', 'fr': 'Version modifiee ({n})',
    },
    'source_detail.diff_loading': {
        'en': 'Loading diff...', 'nl': 'Diff laden...', 'de': 'Diff wird geladen...',
        'es': 'Cargando diff...', 'fr': 'Chargement du diff...',
    },
    'source_detail.diff_removed_none': {
        'en': 'No removals.', 'nl': 'Geen verwijderingen.', 'de': 'Keine Entfernungen.',
        'es': 'Sin eliminaciones.', 'fr': 'Aucune suppression.',
    },
    'source_detail.diff_removed_title': {
        'en': 'Removed ({n})', 'nl': 'Verwijderd ({n})', 'de': 'Entfernt ({n})', 'es': 'Eliminadas ({n})', 'fr': 'Supprimees ({n})',
    },
    'source_detail.field_config_json': {
        'en': 'Config (JSON)', 'nl': 'Configuratie (JSON)', 'de': 'Konfiguration (JSON)',
        'es': 'Configuracion (JSON)', 'fr': 'Configuration (JSON)',
    },
    'source_detail.field_enabled': {
        'en': 'Enabled', 'nl': 'Ingeschakeld', 'de': 'Aktiviert', 'es': 'Habilitado', 'fr': 'Active',
    },
    'source_detail.field_name': {
        'en': 'Name', 'nl': 'Naam', 'de': 'Name', 'es': 'Nombre', 'fr': 'Nom',
    },
    'source_detail.field_scan_interval': {
        'en': 'Scan interval (hh:mm:ss)', 'nl': 'Scaninterval (uu:mm:ss)',
        'de': 'Scan-Intervall (hh:mm:ss)', 'es': 'Intervalo de escaneo (hh:mm:ss)', 'fr': 'Intervalle de scan (hh:mm:ss)',
    },
    'source_detail.inventory_empty': {
        'en': 'No packages.', 'nl': 'Geen pakketten.', 'de': 'Keine Pakete.', 'es': 'Sin paquetes.', 'fr': 'Aucun paquet.',
    },
    'source_detail.inventory_error': {
        'en': 'Failed to load inventory.', 'nl': 'Inventaris laden mislukt.',
        'de': 'Inventar konnte nicht geladen werden.', 'es': 'No se pudo cargar el inventario.',
        'fr': "Echec du chargement de l'inventaire.",
    },
    'source_detail.inventory_loading': {
        'en': 'Loading inventory...', 'nl': 'Inventaris laden...', 'de': 'Inventar wird geladen...',
        'es': 'Cargando inventario...', 'fr': "Chargement de l'inventaire...",
    },
    'source_detail.inventory_subtitle': {
        'en': '{count} packages parsed at {when}', 'nl': '{count} pakketten verwerkt om {when}',
        'de': '{count} Pakete um {when} verarbeitet', 'es': '{count} paquetes analizados a las {when}',
        'fr': '{count} paquets analyses a {when}',
    },
    'source_detail.inventory_title': {
        'en': 'Inventory', 'nl': 'Inventaris', 'de': 'Inventar', 'es': 'Inventario', 'fr': 'Inventaire',
    },
    'source_detail.meta_branch': {
        'en': 'Branch', 'nl': 'Branch', 'de': 'Branch', 'es': 'Rama', 'fr': 'Branche',
    },
    'source_detail.meta_branch_default': {
        'en': 'default', 'nl': 'standaard', 'de': 'Standard', 'es': 'predeterminada', 'fr': 'par defaut',
    },
    'source_detail.meta_contents_sha': {
        'en': 'Contents SHA', 'nl': 'Inhoud-SHA', 'de': 'Inhalts-SHA', 'es': 'SHA del contenido', 'fr': 'SHA du contenu',
    },
    'source_detail.meta_github_repo': {
        'en': 'GitHub repository', 'nl': 'GitHub-repository', 'de': 'GitHub-Repository',
        'es': 'Repositorio de GitHub', 'fr': 'Depot GitHub',
    },
    'source_detail.meta_last_error': {
        'en': 'Last error', 'nl': 'Laatste fout', 'de': 'Letzter Fehler', 'es': 'Ultimo error', 'fr': 'Derniere erreur',
    },
    'source_detail.meta_last_scanned': {
        'en': 'Last scanned', 'nl': 'Laatst gescand', 'de': 'Zuletzt gescannt',
        'es': 'Ultimo escaneo', 'fr': 'Dernier scan',
    },
    'source_detail.meta_last_snapshot': {
        'en': 'Last snapshot', 'nl': 'Laatste snapshot', 'de': 'Letzter Snapshot',
        'es': 'Ultimo snapshot', 'fr': 'Dernier snapshot',
    },
    'source_detail.meta_linux_host': {
        'en': 'Linux host', 'nl': 'Linux-host', 'de': 'Linux-Host', 'es': 'Host Linux', 'fr': 'Hote Linux',
    },
    'source_detail.meta_local_folder': {
        'en': 'Local folder', 'nl': 'Lokale map', 'de': 'Lokaler Ordner',
        'es': 'Carpeta local', 'fr': 'Dossier local',
    },
    'source_detail.meta_raw_config': {
        'en': 'Raw config', 'nl': 'Ruwe configuratie', 'de': 'Rohkonfiguration',
        'es': 'Configuracion sin procesar', 'fr': 'Configuration brute',
    },
    'source_detail.meta_scan_interval': {
        'en': 'Scan interval', 'nl': 'Scaninterval', 'de': 'Scan-Intervall',
        'es': 'Intervalo de escaneo', 'fr': 'Intervalle de scan',
    },
    'source_detail.promote_ok': {
        'en': 'Created GitHub source "{name}".', 'nl': 'GitHub-bron "{name}" aangemaakt.',
        'de': 'GitHub-Quelle "{name}" erstellt.', 'es': 'Fuente de GitHub "{name}" creada.',
        'fr': 'Source GitHub "{name}" creee.',
    },
    'source_detail.show_flat': {
        'en': 'Show flat list', 'nl': 'Platte lijst tonen', 'de': 'Flache Liste anzeigen',
        'es': 'Mostrar lista plana', 'fr': 'Afficher la liste plate',
    },
    'source_detail.show_tree': {
        'en': 'Show tree', 'nl': 'Boomstructuur tonen', 'de': 'Baum anzeigen',
        'es': 'Mostrar arbol', 'fr': "Afficher l'arborescence",
    },
    'source_detail.snapshot_items': {
        'en': '{count} items', 'nl': '{count} items', 'de': '{count} Eintraege',
        'es': '{count} elementos', 'fr': '{count} elements',
    },
    'source_detail.update_ok': {
        'en': 'Source updated.', 'nl': 'Bron bijgewerkt.', 'de': 'Quelle aktualisiert.',
        'es': 'Fuente actualizada.', 'fr': 'Source mise a jour.',
    },

    # ─── sources.* ───────────────────────────────────────────────
    'sources.form_config_json': {
        'en': 'Config (JSON)', 'nl': 'Configuratie (JSON)', 'de': 'Konfiguration (JSON)',
        'es': 'Configuracion (JSON)', 'fr': 'Configuration (JSON)',
    },
    'sources.refresh_access_none': {
        'en': "You don't have permission to scan this source.",
        'nl': 'Je hebt geen toestemming om deze bron te scannen.',
        'de': 'Sie haben keine Berechtigung, diese Quelle zu scannen.',
        'es': 'No tienes permiso para escanear esta fuente.',
        'fr': "Vous n'avez pas l'autorisation de scanner cette source.",
    },
    'sources.type_folder': {
        'en': 'Local folder', 'nl': 'Lokale map', 'de': 'Lokaler Ordner',
        'es': 'Carpeta local', 'fr': 'Dossier local',
    },
    'sources.type_github': {
        'en': 'GitHub repository', 'nl': 'GitHub-repository', 'de': 'GitHub-Repository',
        'es': 'Repositorio de GitHub', 'fr': 'Depot GitHub',
    },
    'sources.type_host': {
        'en': 'Linux host', 'nl': 'Linux-host', 'de': 'Linux-Host',
        'es': 'Host Linux', 'fr': 'Hote Linux',
    },

    # ─── screen.settings.section_auto_fix.* (NL-only gap) ────────
    'screen.settings.section_auto_fix.col_mode': {
        'en': 'Mode', 'nl': 'Modus', 'de': 'Modus', 'es': 'Modo', 'fr': 'Mode',
    },
    'screen.settings.section_auto_fix.col_source': {
        'en': 'Source', 'nl': 'Bron', 'de': 'Quelle', 'es': 'Fuente', 'fr': 'Source',
    },
    'screen.settings.section_auto_fix.no_sources': {
        'en': 'No sources configured yet.',
        'nl': 'Nog geen bronnen geconfigureerd.',
        'de': 'Noch keine Quellen konfiguriert.',
        'es': 'Aun no hay fuentes configuradas.',
        'fr': 'Aucune source configuree pour le moment.',
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
    for key, table in TRANSLATIONS.items():
        if get_nested(data, key) is not None:
            continue  # already present, never overwrite
        value = table.get(locale, table['en'])
        set_nested(data, key, value)
        added += 1
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'{locale}: +{added}')
