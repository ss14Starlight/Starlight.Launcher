# Page

local-server-page-warning-banner = I server locali eseguono codice non protetto e non verificato proveniente da qualsiasi fonte tu configuri qui sotto. Sei l'unico responsabile di ciò che scarichi ed esegui - Starlight non è responsabile di eventuali danni.

# One-time policy gate

local-server-policy-alert-title = Avviso server locale
local-server-policy-alert-description =
    La pagina Server locale scarica ed esegue una build del server da un URL di manifest che fornisci tu, senza alcuna protezione.
    Continuando, accetti di essere l'unico responsabile di qualsiasi build tu scelga di eseguire, e che STARLIGHT non garantisce
    il corretto funzionamento del software né che non causi danni al tuo hardware.

# Sources

local-server-sources-title = Fonti
local-server-sources-option-title = Fonti dei manifest
local-server-sources-option-description = URL dei manifest da cui scaricare le build del server locale.
local-server-sources-empty = Nessuna fonte configurata. Premi + per aggiungerne una.
local-server-sources-add-tooltip = Aggiungi fonte
local-server-sources-name-label = Nome
local-server-sources-url-label = URL del manifest

local-server-source-warning-title = Avviso nuova fonte
local-server-source-warning-body = Stai per aggiungere una nuova fonte di build del server non isolate e non verificate. Aggiungi solo fonti di cui ti fidi.
local-server-source-warning-hint = Aggiunta di una nuova fonte per il server locale.
local-server-source-warning-cancel = Annulla
local-server-source-warning-confirm = Ho capito, aggiungi

# Launch

local-server-launch-title = Avvio
local-server-source-select-label = Fonte
local-server-refresh-button = Aggiorna
local-server-latest-build-info = Ultima build { $hash } ({ $time }) - { $size } per la tua piattaforma.
local-server-unsupported-platform = Nessuna build del server disponibile per la tua piattaforma in questo manifest.
local-server-start-button = Avvia
local-server-stop-button = Ferma
local-server-connect-button = Connetti
local-server-connecting-title = Connessione
local-server-open-folder-button = Apri cartella

local-server-clear-description = Rimuove dal disco tutte le build del server locale scaricate ed estratte.
local-server-clear-button = Elimina server installati
local-server-clear-confirm-title = Elimina server installati
local-server-clear-confirm-text = Questo ferma il server locale in esecuzione (se presente) ed elimina dal disco tutte le build scaricate. Dovrai scaricarle di nuovo per eseguirle.
local-server-clear-confirm-yes = Elimina
local-server-clear-confirm-cancel = Annulla
local-server-clear-done = Server locali installati eliminati.

local-server-status-idle = Inattivo
local-server-status-fetching = Recupero del manifest...
local-server-status-downloading = Download in corso... { $percent }
local-server-status-extracting = Estrazione...
local-server-status-starting = Avvio...
local-server-status-running = In esecuzione
local-server-status-stopping = Arresto...
local-server-status-stopped = Fermato
local-server-status-error = Errore

# Server configuration

local-server-config-title = Configurazione del server
local-server-config-no-source = Seleziona una fonte qui sopra per configurare il suo server_config.toml.
local-server-config-basic-title = Opzioni base
local-server-config-custom-title = Opzioni personalizzate
local-server-config-custom-empty = Nessuna CVar personalizzata aggiunta.
local-server-config-add-tooltip = Aggiungi CVar
local-server-config-group-placeholder = Gruppo
local-server-config-name-placeholder = Nome
local-server-config-value-placeholder = Valore
local-server-config-type-string = Stringa
local-server-config-type-int = Intero
local-server-config-type-float = Decimale
local-server-config-type-bool = Booleano
local-server-config-save-button = Salva configurazione
local-server-config-saved = Configurazione del server salvata.
local-server-config-hint = Le modifiche vengono scritte in server_config.toml al prossimo avvio di questo server.

# Console

local-server-console-title = Console
local-server-console-empty = Ancora nessun output.
local-server-console-clear-tooltip = Pulisci console
local-server-console-autoscroll-tooltip = Attiva/disattiva scorrimento automatico
local-server-console-input-placeholder = Scrivi un comando del server...
local-server-console-send-failed = Impossibile inviare il comando - il server non è in esecuzione.
