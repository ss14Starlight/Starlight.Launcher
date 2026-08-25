# Page

local-server-page-warning-banner = Local servers run unprotected, unverified code from whatever source you configure below. You are solely responsible for what you download and run - Starlight is not liable for any damage it may cause.

# One-time policy gate

local-server-policy-alert-title = Local Server warning
local-server-policy-alert-description =
    The Local Server page downloads and runs a server build from a manifest URL you provide, with no protection whatsoever.
    By continuing, you agree that you are solely responsible for any build you choose to run, and that STARLIGHT does not guarantee
    the software will function properly nor that it will not cause damage to your equipment.

# Sources

local-server-sources-title = Sources
local-server-sources-option-title = Manifest sources
local-server-sources-option-description = Manifest URLs to fetch local server builds from.
local-server-sources-empty = No sources configured. Click + to add one.
local-server-sources-add-tooltip = Add source
local-server-sources-name-label = Name
local-server-sources-url-label = Manifest URL

local-server-source-warning-title = New source warning
local-server-source-warning-body = You're about to add a new source of unsandboxed, unverified server builds. Only add sources you trust.
local-server-source-warning-hint = Adding a new local server source.
local-server-source-warning-cancel = Cancel
local-server-source-warning-confirm = I understand, add it

# Launch

local-server-launch-title = Launch
local-server-source-select-label = Source
local-server-refresh-button = Refresh
local-server-latest-build-info = Latest build { $hash } ({ $time }) - { $size } for your platform.
local-server-unsupported-platform = No server build available for your platform in this manifest.
local-server-start-button = Start
local-server-stop-button = Stop
local-server-connect-button = Connect
local-server-connecting-title = Connecting
local-server-open-folder-button = Open Folder

local-server-clear-description = Remove every downloaded and extracted local server build from disk.
local-server-clear-button = Clear installed servers
local-server-clear-confirm-title = Clear installed servers
local-server-clear-confirm-text = This stops the running local server (if any) and deletes every downloaded build from disk. You'll need to download them again to run them.
local-server-clear-confirm-yes = Clear
local-server-clear-confirm-cancel = Cancel
local-server-clear-done = Installed local servers cleared.

local-server-status-idle = Idle
local-server-status-fetching = Fetching manifest...
local-server-status-downloading = Downloading... { $percent }
local-server-status-extracting = Extracting...
local-server-status-starting = Starting...
local-server-status-running = Running
local-server-status-stopping = Stopping...
local-server-status-stopped = Stopped
local-server-status-error = Error

# Server configuration

local-server-config-title = Server Configuration
local-server-config-no-source = Select a source above to configure its server_config.toml.
local-server-config-basic-title = Basic options
local-server-config-custom-title = Custom options
local-server-config-custom-empty = No custom CVars added.
local-server-config-add-tooltip = Add CVar
local-server-config-group-placeholder = Group
local-server-config-name-placeholder = Name
local-server-config-value-placeholder = Value
local-server-config-type-string = String
local-server-config-type-int = Int
local-server-config-type-float = Float
local-server-config-type-bool = Bool
local-server-config-save-button = Save configuration
local-server-config-saved = Server configuration saved.
local-server-config-hint = Changes are written into server_config.toml the next time this server starts.

# Console

local-server-console-title = Console
local-server-console-empty = No output yet.
local-server-console-clear-tooltip = Clear console
local-server-console-autoscroll-tooltip = Toggle auto-scroll
local-server-console-input-placeholder = Type a server command...
local-server-console-send-failed = Could not send command - the server isn't running.
