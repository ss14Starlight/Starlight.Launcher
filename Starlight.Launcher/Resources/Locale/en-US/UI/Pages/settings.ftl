# Categories

settings-menu-general-category = General
settings-menu-servers-category = Servers
settings-menu-appearance-category = Appearance
settings-menu-server-list-category = Server List
settings-menu-tray-category = Tray
settings-menu-development-category = Development
settings-menu-discord-rpc-category = Rich Presence
settings-menu-ignored-category = Ignored

# General

settings-menu-reset-option = Reset Settings
settings-menu-reset-confirm-title = Reset Settings to Defaults
settings-menu-reset-confirm-text = Are you sure you want to reset the settings to their defaults?
settings-menu-reset-confirm-yes = Yes
settings-menu-reset-confirm-cancel = Cancel

settings-menu-language-option = Language
settings-menu-language-option-description = Select the language for the app.
settings-menu-language-option-system = System

settings-menu-multiple-clients-option = Prevent Multiple Clients
settings-menu-multiple-clients-option-description = Prevents launch of multiple clients at once.

settings-menu-prereleases-option = Pre-release Updates
settings-menu-prereleases-option-description = Offer test versions of the launcher. They may be unstable.

settings-menu-data-folder-option = Data Folder
settings-menu-data-folder-option-description = Select folder which will be used to store data.
settings-menu-data-folder-no-access-title = No access to the folder
settings-menu-data-folder-no-access-text = The launcher can't write to "{ $path }". This folder probably requires administrator rights, so downloading and updating game files will fail. Choose another folder.

settings-menu-logs-folder-option = Logs Folder
settings-menu-logs-folder-option-description = Select folder which will be used to store logs.

settings-menu-logs-split-mode-option = Logs Split Mode
settings-menu-logs-split-mode-option-description = Determines how logs are split into separate files.

settings-menu-logs-combine-streams-option = Combine Logs Streams
settings-menu-logs-combine-streams-option-description = Combines stdout and stderr streams into a single log file.

settings-menu-log-retain-amount-option = Log Retention Amount
settings-menu-log-retain-amount-option-description = Determines how many log files to keep.

settings-menu-update-option = Check for update
settings-menu-update-latest = You are already on the latest version.
settings-menu-update-found = version { $latest } is now available!
settings-menu-update-download = Download

# Servers

settings-menu-hub-list-option = Hub List
settings-menu-hub-list-option-description = Manage the list of hubs.
settings-menu-hub-list-option-add-tooltip = Add hub
settings-menu-hub-list-option-empty-values = No hubs configured. Click + to add one.
settings-menu-hub-list-option-text-label = Hub URL

settings-menu-auth-list-option = Auth Servers List
settings-menu-auth-list-option-description = Manage the list of auth and select current auth server.
settings-menu-auth-list-option-add-tooltip = Add auth server
settings-menu-auth-list-option-empty-values = No auth servers configurated. Click + to add one.
settings-menu-auth-list-option-text-label = Auth Server URL

settings-menu-deauth-onchange-option = Deauth On Change
settings-menu-deauth-onchange-option-description = Should we deauth launcher after changing auth server?

settings-cdns-option = Cdns
settings-cdns-option-description = Manage the list of cdn servers used for downloading client engine.
settings-cdns-option-add = Add CDN
settings-cdns-option-empty = No CDNs configured. Click + to add one.
settings-cdns-option-name-label = CDN Name
settings-cdns-option-important-tooltip = Important
settings-cdns-option-enabled-tooltip = Enabled
settings-cdns-option-key-label = CDN Public Key
settings-cdns-option-mirrors-label = CDN Mirrors
settings-cdns-option-add-mirror = Add CDN Mirror
settings-cdns-option-important-warning-title = Important Change Warning
settings-cdns-option-important-warning-body = 
    You are about to change a CDN server marked as important. 
    This action will break some futures of application.
    Please ensure that you know what you're doing.
settings-cdns-option-important-warning-hint = 
    This will break discord auth.
    Proceed with caution.
settings-cdns-option-important-warning-cancel = Cancel
settings-cdns-option-important-warning-confirm = Confirm
settings-cdns-option-key-unlock-tooltip = Unlock public key editing
settings-cdns-option-key-lock-tooltip = Lock public key editing
settings-cdns-option-key-unlock-warning-title = Edit CDN public keys?
settings-cdns-option-key-unlock-warning = Public keys decide which engine builds the launcher trusts. Only add keys you got from the CDN owner directly: a wrong key breaks engine downloads, and a key from someone you don't trust lets them run their own code on your computer. Changing the key of the Starlight CDN breaks Discord and Steam login.

# Appearance

settings-menu-theme-option = Theme
settings-menu-theme-option-description = Select the theme for the app.

settings-menu-navigation-bar-option = Navigation Position
settings-menu-navigation-bar-option-description = Select the position of the navigation bar.

settings-menu-animated-background-option = Animated background
settings-menu-animated-background-option-description = Drifting stars in the background and camera flights between pages. Turning it off saves CPU while the launcher is open.

settings-menu-bottom-search-option = Bottom Search
settings-menu-bottom-search-option-description = Place search bar at the bottom of the toolbar(when tags bar and search bar together).

settings-menu-bottom-search-bar-option = Bottom Search Bar
settings-menu-bottom-search-bar-option-description = Place search bar at the bottom of the server list.

settings-menu-bottom-tags-bar-option = Bottom Tags Bar
settings-menu-bottom-tags-bar-option-description = Place tags bar at the bottom of the server list.

settings-menu-tags-bar-open-option = Tags Bar Open
settings-menu-tags-bar-open-option-description = Should we keep tags bar open by default?
settings-menu-round-info-collapsed-option = Round Info In Row
settings-menu-round-info-collapsed-option-description = Show the map, game mode and round id in the server row without expanding it.

# Tray

settings-menu-tray-collapse-start-option = Collapse on Start
settings-menu-tray-collapse-start-option-description = Start the app minimized to the system tray.

settings-menu-tray-collapse-run-option = Collapse on Run
settings-menu-tray-collapse-run-option-description = Minimize the app to the system tray when running game.

settings-menu-tray-uncollapse-end-option = Uncollapse on End
settings-menu-tray-uncollapse-end-option-description = Expand the app from system tray when closing game.

settings-menu-tray-collapse-close-option = Collapse on Close
settings-menu-tray-collapse-close-option-description = Minimize the app to the system tray when closing it.

settings-menu-tray-collapse-minimize-option = Collapse on Minimize
settings-menu-tray-collapse-minimize-option-description = Minimize the app to the system tray when minimizing it.

# Discord RPC

settings-menu-discord-presence-option = Discord Presence
settings-menu-discord-presence-option-description = Show status in your Discord.

settings-menu-discord-presence-buttons-option = Show presence buttons
settings-menu-discord-presence-buttons-option-description = Should we show "Download" and other "ad" buttons in Discord Rich Presence?

settings-menu-discord-presence-states-option = Presence States
settings-menu-discord-presence-states-option-description = You can manage different states and it's priority to change your Discord Presence.

# Development(Advanced)

settings-menu-development-gles-option = Compatability Mode (GLES2)
settings-menu-development-gles-option-description = Enable compatibility mode for devices with older graphics hardware. This may reduce performance on modern systems.

settings-menu-development-signing-option = Disable Engine Signature Verification
settings-menu-development-signing-option-description = Disable signature verification for engine files. This is intended for development purposes and may pose security risks if enabled in production environments.(Works only with Debug configuration)

settings-menu-development-engine-override-option = Engine Override
settings-menu-development-engine-override-option-description = Override the default engine executable path. This is intended for development purposes and may cause issues if set incorrectly.

settings-menu-development-engine-override-path-option = Engine Override Path
settings-menu-development-engine-override-path-option-description = The file path to the engine executable to use when Engine Override is enabled. This should point to a valid Engine executable file.
settings-menu-development-engine-override-path-option-helper = Write Path To Engine Executable
settings-menu-development-engine-override-path-option-label = Engine Path

settings-menu-development-robust-manifest-cache-time-option = Robust Manifest Cache Time
settings-menu-development-robust-manifest-cache-time-option-description = Set the time for which to cache manifest data.

settings-menu-development-max-versions-option = Max Versions
settings-menu-development-max-versions-option-description = Set the maximum number of versions to keep in the version history. Older versions will be removed when this limit is exceeded.

settings-menu-development-max-fork-versions-option = Max Fork Versions
settings-menu-development-max-fork-versions-option-description = Set the maximum number of fork versions to keep in the version history. Older fork versions will be removed when this limit is exceeded.

# Development tab alert

settings-development-tab-alert-title = Development tab alert
settings-development-tab-alert-description = 
    You're trying to access the tab created only for advanced users or development. 
    To proceed, you must agree to the condition that after changing any settings, 
    STARLIGHT does not guarantee that the software will function properly, nor does it guarantee that it will not cause damage to your equipment.

# Ignored

settings-menu-ignored-servers-option = Ignored servers
settings-menu-ignored-servers-option-description = Allows to remove some servers from ignore list.

settings-ignored-servers-option-remove = Remove
# Data

settings-menu-data-category = Data
settings-menu-clear-button = Clear
settings-menu-clear-confirm-yes = Delete
settings-menu-clear-engines-option = Downloaded Engines
settings-menu-clear-engines-option-description = Deletes all downloaded engine versions and modules. They will be downloaded again the next time you join a server.
settings-menu-clear-engines-confirm-title = Delete downloaded engines?
settings-menu-clear-engines-confirm-text = All downloaded engine versions and modules will be deleted. Joining a server will take longer while the engine is downloaded again. This can't be undone.
settings-menu-clear-engines-success = Downloaded engines deleted.
settings-menu-clear-content-option = Game Content
settings-menu-clear-content-option-description = Deletes all downloaded server content. It will be downloaded again the next time you join a server.
settings-menu-clear-content-confirm-title = Delete game content?
settings-menu-clear-content-confirm-text = All downloaded content of every server will be deleted. Joining a server will take longer while its content is downloaded again. This can't be undone.
settings-menu-clear-content-success = Game content deleted.
settings-menu-clear-busy = Wait for the current download to finish and try again.
settings-menu-clear-client-running = Close the running game and try again.
settings-menu-clear-failed = Couldn't delete the data. See the launcher logs for details.

settings-menu-development-client-category = Game client
settings-menu-development-tracy-option = Tracy Profiler
settings-menu-development-tracy-option-description = Launch the game with the Tracy profiler enabled (prof.tracy.enabled). The profiler must be version 0.13.1, newer or older versions won't connect. Costs some performance.
settings-menu-development-tracy-localhost-option = Tracy: localhost only
settings-menu-development-tracy-localhost-option-description = Only accept Tracy connections from this computer. When disabled, Tracy listens on all network interfaces.
settings-menu-development-tracy-launch-option = Tracy: open profiler automatically
settings-menu-development-tracy-launch-option-description = Download Tracy 0.13.1 on first launch and open it already connected to the game. Windows only, on Linux build the profiler yourself.
settings-menu-development-crash-dumps-option = Crash dumps
settings-menu-development-crash-dumps-option-description = Write a memory dump when the game crashes, including native crashes (access violation / SIGSEGV). Open it with dotnet-dump analyze, WinDbg or Visual Studio. A dump can take over a gigabyte. On Linux and macOS a readable .crashreport.json with every thread's stack is written next to it.
settings-menu-development-crash-dumps-retain-option = Crash dumps to keep
settings-menu-development-crash-dumps-retain-option-description = Older dumps are deleted before each launch.
settings-menu-development-crash-dumps-folder-option = Crash dumps folder
settings-menu-development-crash-dumps-folder-option-description = The client log also lists the dump written for a crash.
settings-menu-development-crash-dumps-folder-button = Open
settings-menu-development-cvars-option = Client CVar Overrides
settings-menu-development-cvars-option-description = Extra cvars passed to the game client on launch. Cvars the server replicates to clients are still overridden by the server.
settings-menu-development-cvars-option-helper = One key=value per line or separated by ";", lines starting with # are ignored

# Audio

settings-menu-audio-category = Audio
settings-menu-audio-output-mode-option = Speaker layout
settings-menu-audio-output-mode-option-description = Which speaker setup the game renders sound for. If voices or some sounds are missing, try "Stereo" or "Headphones". Requires the Starlight engine.
settings-menu-audio-output-mode-auto = Automatic
settings-menu-audio-output-mode-stereo = Stereo
settings-menu-audio-output-mode-headphones = Headphones (HRTF)
settings-menu-audio-output-mode-quad = Quadraphonic
settings-menu-audio-output-mode-surround51 = 5.1 Surround
settings-menu-audio-output-mode-surround71 = 7.1 Surround
settings-menu-audio-output-mode-mono = Mono
settings-menu-audio-follow-device-option = Follow system output device
settings-menu-audio-follow-device-option-description = Switch the game audio along when the system default output changes, e.g. when headphones are plugged in. Requires the Starlight engine.
settings-menu-diagnostics-bundle-option = Collect diagnostics
settings-menu-diagnostics-bundle-option-description = Pack recent game and launcher logs, crash reports, system and GPU info into one zip for the developers. Logs contain your username and the servers you joined.
settings-menu-diagnostics-bundle-button = Collect
settings-menu-diagnostics-bundle-success = Diagnostics saved to { $path }
settings-menu-diagnostics-bundle-failed = Couldn't collect diagnostics, see the launcher log.
client-diagnostics-title = Running game
client-diagnostics-running = Client PID { $pid }. Live .NET runtime counters, like dotnet-counters.
client-diagnostics-not-running = The game isn't running.
client-diagnostics-dump-button = Dump now
client-diagnostics-dump-hint = A dump freezes the game for a few seconds while it's written. Use it for hangs and memory issues; crashes are caught by the crash dumps option.
client-diagnostics-dump-success = Dump saved to { $path }
client-diagnostics-dump-failed = Couldn't dump the game, see the launcher log.
client-diagnostics-cpu = CPU usage
client-diagnostics-working-set = Working set
client-diagnostics-gc-heap = GC heap size
client-diagnostics-alloc-rate = Allocation rate
client-diagnostics-time-in-gc = Time in GC
client-diagnostics-gen0 = Gen 0 collections
client-diagnostics-gen1 = Gen 1 collections
client-diagnostics-gen2 = Gen 2 collections
client-diagnostics-threadpool-queue = Thread pool queue
client-diagnostics-threadpool-threads = Thread pool threads
client-diagnostics-lock-contention = Lock contentions
client-diagnostics-exceptions = Exceptions

# Content

settings-menu-content-category = Content
settings-menu-downloads-category = Downloads
settings-menu-download-speed-limit-option = Download Speed Limit (KB/s)
settings-menu-download-speed-limit-option-description = Caps how fast server content, engines and modules are downloaded, so the launcher doesn't take your whole connection. 0 means no limit.
settings-menu-preload-favorites-option = Preload Favorite Servers
settings-menu-preload-favorites-option-description = While the launcher is idle, downloads updates of your favorite servers in the background, so connecting to them is instant. Only servers whose privacy policy you have accepted are preloaded.
settings-menu-storage-category = Storage
settings-storage-option = Disk Usage
settings-storage-option-description = How much space downloaded content takes for each server fork, and each engine version and module.
settings-storage-refresh = Refresh
settings-storage-content-section = Server content · { $size }
settings-storage-content-empty = No server content downloaded yet.
settings-storage-unknown-fork = Unknown fork
settings-storage-fork-versions = Versions: { $count }
settings-storage-shared-note = Files shared between forks are counted for each of them, so the sum can be larger than the database.
settings-storage-engines-section = Engines and modules · { $size }
settings-storage-engines-empty = No engines downloaded yet.
settings-storage-engine = Engine
settings-menu-verify-content-option = Verify and Repair Content
settings-menu-verify-content-option-description = Checks every downloaded file against its hash and removes broken ones. Server versions that used them will be downloaded again on the next join. Can take a few minutes.
settings-menu-verify-content-button = Verify
settings-menu-verify-content-ok = No problems found, { $count } files checked.
settings-menu-verify-content-repaired = Repaired: { $corrupt } broken files removed, { $versions } server versions will be downloaded again, { $orphans } leftover entries cleaned up.
settings-menu-verify-content-rebuilt = The content database was damaged and has been recreated. Server content will be downloaded again on the next join.
settings-menu-verify-content-failed = Couldn't verify the content. See the launcher logs for details.
settings-menu-verify-content-cancelled = Content check cancelled.
