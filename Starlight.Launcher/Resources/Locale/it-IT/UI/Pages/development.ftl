main-layout-development-tab = Sviluppo

settings-menu-developer-mode-option = Modalità sviluppatore
settings-menu-developer-mode-option-description = Mostra la pagina Sviluppo con opzioni di avvio, profiler, dump dei crash, strumenti di diagnostica .NET e console del gioco. Le sue opzioni valgono solo finché la modalità sviluppatore è attiva.

development-page-disabled = La modalità sviluppatore è disattivata. Attivala in Impostazioni -> Generale per usare questa pagina.

development-tab-launch = Avvio
development-tab-profiling = Profilazione
development-tab-diagnostics = Diagnostica
development-tab-console = Console

development-log-level-option = Livello dei log del client
development-log-level-option-description = Livello minimo dei log del gioco (--loglevel root=...). Verbose mostra tutto ed è molto rumoroso.
development-log-level-default = Predefinito del motore

development-renderdoc-option = RenderDoc
development-renderdoc-option-description = Carica RenderDoc { $version } nel gioco per catturare i frame con F12 / Stamp o il comando di console renderdoc_capture. Scaricato al primo avvio. Serve un motore client con supporto a display.renderdoc.
development-renderdoc-open-option = Interfaccia RenderDoc
development-renderdoc-open-option-description = Apri RenderDoc per vedere le catture. Il gioco in esecuzione compare in File -> Attach to Running Instance; il comando di console renderdoc_ui lo apre già collegato.
development-renderdoc-open-button = Apri
development-renderdoc-open-failed = Impossibile aprire RenderDoc, vedi il log del launcher.
development-renderdoc-unsupported = RenderDoc non ha una build per questa piattaforma.

development-crash-dumps-category = Dump dei crash
development-running-game-category = Gioco in esecuzione
development-tools-category = Strumenti di diagnostica .NET

development-console-filter = Filtro
development-console-errors-only = Solo errori
development-console-copy = Copia le righe mostrate
development-console-copied = Copiato negli appunti.
development-console-copy-failed = Impossibile copiare negli appunti.
development-console-open-logs = Apri la cartella dei log del client
development-console-empty = L'output del gioco comparirà qui quando il gioco sarà avviato.

development-tool-trace-description = Registra una traccia delle prestazioni del gioco in esecuzione: stack della CPU, eventi GC e JIT. Il formato Speedscope si apre su speedscope.app, NetTrace in PerfView o Visual Studio.
development-tool-trace-profile = Profilo
development-tool-trace-profile-cpu = CPU (stack campionati)
development-tool-trace-profile-gc-verbose = GC + allocazioni
development-tool-trace-profile-gc-collect = Solo GC (basso impatto)
development-tool-trace-profile-common = Eventi del runtime
development-tool-trace-format = Formato extra
development-tool-duration = Secondi
development-tool-record = Registra
development-tool-counters-description = Registra ogni secondo i contatori del runtime (GC, allocazioni, thread pool, eccezioni...) in un file CSV.
development-tool-gcdump-description = Istantanea dell'heap gestito: quali tipi occupano la memoria. Molto più piccola di un dump completo, si apre in Visual Studio o PerfView. Il gioco si ferma mentre l'heap viene percorso.
development-tool-stack-description = Stampa lo stack gestito di ogni thread del gioco. Usalo mentre il gioco è bloccato.
development-tool-monitor-description = Avvia dotnet-monitor su localhost: un'API REST per dump, tracce, log e metriche del gioco, utilizzabile anche da script.
development-tool-monitor-open = Apri API
development-tool-collect = Raccogli
development-tool-start = Avvia
development-tool-stop = Ferma
development-tool-runs = Esecuzioni
development-tool-open-captures = Apri la cartella delle catture
development-tool-no-runs = Ancora nulla di registrato. Al primo uso lo strumento viene scaricato, e .NET se serve.
development-tool-status-installing = Installazione
development-tool-status-running = In esecuzione
development-tool-status-completed = Fatto
development-tool-status-failed = Errore
development-tool-status-stopped = Fermato
development-tool-unsupported = Questo strumento non ha una build per questa piattaforma.
development-tool-already-running = dotnet-monitor è già in esecuzione.
client-diagnostics-pid = PID { $pid }
