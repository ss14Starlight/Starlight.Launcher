main-layout-development-tab = Development

settings-menu-developer-mode-option = Developer mode
settings-menu-developer-mode-option-description = Shows the Development page with launch options, profilers, crash dumps, .NET diagnostics tools and the game console. Its options only apply while developer mode is on.

development-page-disabled = Developer mode is off. Turn it on in Settings -> General to use this page.

development-tab-launch = Launch
development-tab-profiling = Profiling
development-tab-diagnostics = Diagnostics
development-tab-console = Console

development-log-level-option = Client log level
development-log-level-option-description = Minimum level the game logs at (--loglevel root=...). Verbose shows everything and is very noisy.
development-log-level-default = Engine default

development-renderdoc-option = RenderDoc
development-renderdoc-option-description = Load RenderDoc { $version } into the game so frames can be captured with F12 / Print Screen or the renderdoc_capture console command. Downloaded on first launch. Needs a client engine with display.renderdoc support.
development-renderdoc-open-option = RenderDoc UI
development-renderdoc-open-option-description = Open RenderDoc to look at captures. A running game shows up under File -> Attach to Running Instance; the renderdoc_ui console command opens it already connected.
development-renderdoc-open-button = Open
development-renderdoc-open-failed = Couldn't open RenderDoc, see the launcher log.
development-renderdoc-unsupported = RenderDoc has no build for this platform.

development-crash-dumps-category = Crash dumps
development-running-game-category = Running game
development-tools-category = .NET diagnostics tools

development-console-filter = Filter
development-console-errors-only = Errors only
development-console-copy = Copy shown lines
development-console-copied = Copied to clipboard.
development-console-copy-failed = Couldn't copy to clipboard.
development-console-open-logs = Open client logs folder
development-console-empty = Game output will appear here once the game is started.

development-tool-trace-description = Records a performance trace of the running game: CPU stacks, GC and JIT events. Speedscope output opens on speedscope.app, NetTrace in PerfView or Visual Studio.
development-tool-trace-profile = Profile
development-tool-trace-profile-cpu = CPU (sampled stacks)
development-tool-trace-profile-gc-verbose = GC + allocations
development-tool-trace-profile-gc-collect = GC only (low overhead)
development-tool-trace-profile-common = Runtime events
development-tool-trace-format = Extra format
development-tool-duration = Seconds
development-tool-record = Record
development-tool-counters-description = Records runtime counters (GC, allocations, thread pool, exceptions...) every second into a CSV file.
development-tool-gcdump-description = Snapshot of the managed heap: which types use the memory. Much smaller than a full dump, open it in Visual Studio or PerfView. The game pauses while the heap is walked.
development-tool-stack-description = Prints the managed stack of every thread of the game. Run it while the game hangs.
development-tool-monitor-description = Starts dotnet-monitor on localhost: a REST API for dumps, traces, logs and metrics of the game, also usable from scripts.
development-tool-monitor-open = Open API
development-tool-collect = Collect
development-tool-start = Start
development-tool-stop = Stop
development-tool-runs = Runs
development-tool-open-captures = Open captures folder
development-tool-no-runs = Nothing recorded yet. The first use of a tool downloads it, and .NET if needed.
development-tool-status-installing = Installing
development-tool-status-running = Running
development-tool-status-completed = Done
development-tool-status-failed = Failed
development-tool-status-stopped = Stopped
development-tool-unsupported = This tool has no build for this platform.
development-tool-already-running = dotnet-monitor is already running.
client-diagnostics-pid = PID { $pid }
