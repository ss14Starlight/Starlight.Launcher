main-layout-development-tab = Разработка

settings-menu-developer-mode-option = Режим разработчика
settings-menu-developer-mode-option-description = Показывает страницу «Разработка» с параметрами запуска, профайлерами, дампами, диагностическими инструментами .NET и консолью игры. Её настройки действуют только пока режим разработчика включён.

development-page-disabled = Режим разработчика выключен. Включите его в Настройки -> Общие, чтобы пользоваться этой страницей.

development-tab-launch = Запуск
development-tab-profiling = Профилирование
development-tab-diagnostics = Диагностика
development-tab-console = Консоль

development-log-level-option = Уровень логов клиента
development-log-level-option-description = Минимальный уровень логов игры (--loglevel root=...). Verbose показывает всё и очень шумный.
development-log-level-default = По умолчанию движка

development-renderdoc-option = RenderDoc
development-renderdoc-option-description = Загружать RenderDoc { $version } в игру, чтобы снимать кадры по F12 / Print Screen или командой консоли renderdoc_capture. Скачивается при первом запуске. Нужен клиентский движок с поддержкой display.renderdoc.
development-renderdoc-open-option = Интерфейс RenderDoc
development-renderdoc-open-option-description = Открыть RenderDoc для просмотра снимков. Запущенная игра видна в File -> Attach to Running Instance; команда консоли renderdoc_ui открывает его уже подключённым.
development-renderdoc-open-button = Открыть
development-renderdoc-open-failed = Не удалось открыть RenderDoc, подробности в логе лаунчера.
development-renderdoc-unsupported = Для этой платформы нет сборки RenderDoc.

development-crash-dumps-category = Дампы при краше
development-running-game-category = Запущенная игра
development-tools-category = Диагностические инструменты .NET

development-console-filter = Фильтр
development-console-errors-only = Только ошибки
development-console-copy = Скопировать показанные строки
development-console-copied = Скопировано в буфер обмена.
development-console-copy-failed = Не удалось скопировать в буфер обмена.
development-console-open-logs = Открыть папку логов клиента
development-console-empty = Здесь появится вывод игры, когда она будет запущена.

development-tool-trace-description = Записывает трейс производительности запущенной игры: стеки CPU, события GC и JIT. Формат Speedscope открывается на speedscope.app, NetTrace - в PerfView или Visual Studio.
development-tool-trace-profile = Профиль
development-tool-trace-profile-cpu = CPU (сэмплы стеков)
development-tool-trace-profile-gc-verbose = GC + аллокации
development-tool-trace-profile-gc-collect = Только GC (минимальная нагрузка)
development-tool-trace-profile-common = События runtime
development-tool-trace-format = Доп. формат
development-tool-duration = Секунды
development-tool-record = Записать
development-tool-counters-description = Записывает счётчики runtime (GC, аллокации, пул потоков, исключения...) раз в секунду в CSV-файл.
development-tool-gcdump-description = Снимок управляемой кучи: какие типы занимают память. Намного меньше полного дампа, открывается в Visual Studio или PerfView. Пока куча обходится, игра стоит.
development-tool-stack-description = Выводит управляемые стеки всех потоков игры. Запускайте, когда игра зависла.
development-tool-monitor-description = Запускает dotnet-monitor на localhost: REST API для дампов, трейсов, логов и метрик игры, пригодный и для скриптов.
development-tool-monitor-open = Открыть API
development-tool-collect = Снять
development-tool-start = Запустить
development-tool-stop = Остановить
development-tool-runs = Запуски
development-tool-open-captures = Открыть папку записей
development-tool-no-runs = Пока ничего не записано. При первом использовании инструмент скачивается, а при необходимости и .NET.
development-tool-status-installing = Установка
development-tool-status-running = Работает
development-tool-status-completed = Готово
development-tool-status-failed = Ошибка
development-tool-status-stopped = Остановлен
development-tool-unsupported = Для этой платформы нет сборки инструмента.
development-tool-already-running = dotnet-monitor уже запущен.
client-diagnostics-pid = PID { $pid }
