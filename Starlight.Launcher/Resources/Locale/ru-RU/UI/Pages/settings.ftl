# Categories

settings-menu-general-category = Основные
settings-menu-servers-category = Серверы
settings-menu-appearance-category = Внешний вид
settings-menu-server-list-category = Список серверов
settings-menu-tray-category = Трей
settings-menu-development-category = Разработка
settings-menu-discord-rpc-category = Rich Presence
settings-menu-ignored-category = Игнорируемые

# General

settings-menu-reset-option = Сбросить настройки
settings-menu-reset-confirm-title = Сброс настроек по умолчанию
settings-menu-reset-confirm-text = Вы уверены, что хотите сбросить настройки к значениям по умолчанию?
settings-menu-reset-confirm-yes = Да
settings-menu-reset-confirm-cancel = Отмена

settings-menu-language-option = Язык
settings-menu-language-option-description = Выберите язык для приложения.
settings-menu-language-option-system = Системный

settings-menu-multiple-clients-option = Запретить несколько клиентов
settings-menu-multiple-clients-option-description = Не даёт запустить несколько клиентов одновременно.

settings-menu-prereleases-option = Пре-релизные обновления
settings-menu-prereleases-option-description = Предлагать тестовые версии лаунчера. Они могут работать нестабильно.

settings-menu-data-folder-option = Папка данных
settings-menu-data-folder-option-description = Выберите папку, в которой будут храниться данные.
settings-menu-data-folder-no-access-title = Нет доступа к папке
settings-menu-data-folder-no-access-text = Лаунчер не может записывать в "{ $path }". Скорее всего, эта папка требует прав администратора, поэтому загрузка и обновление файлов игры не будут работать. Выберите другую папку.

settings-menu-logs-folder-option = Папка логов
settings-menu-logs-folder-option-description = Выберите папку, в которой будут храниться логи.

settings-menu-logs-split-mode-option = Разделение логов
settings-menu-logs-split-mode-option-description = Определяет, как логи разбиваются на отдельные файлы.

settings-menu-logs-combine-streams-option = Объединять потоки логов
settings-menu-logs-combine-streams-option-description = Записывает потоки stdout и stderr в один файл лога.

settings-menu-log-retain-amount-option = Количество хранимых логов
settings-menu-log-retain-amount-option-description = Определяет, сколько файлов логов хранить.

settings-menu-update-option = Проверить обновления
settings-menu-update-latest = У вас уже последняя версия.
settings-menu-update-found = Доступна версия { $latest }!
settings-menu-update-download = Скачать

# Servers

settings-menu-hub-list-option = Список хабов
settings-menu-hub-list-option-description = Настройте список хабов для списка серверов.
settings-menu-hub-list-option-add-tooltip = Добавить хаб
settings-menu-hub-list-option-empty-values = Нет настроенных хабов. Нажмите +, чтобы добавить.
settings-menu-hub-list-option-text-label = URL хаба

settings-menu-auth-list-option = Серверы авторизации
settings-menu-auth-list-option-description = Настройте список серверов авторизации и выберите текущий.
settings-menu-auth-list-option-add-tooltip = Добавить сервер авторизации
settings-menu-auth-list-option-empty-values = Нет настроенных серверов авторизации. Нажмите +, чтобы добавить.
settings-menu-auth-list-option-text-label = URL сервера авторизации

settings-menu-deauth-onchange-option = Выход при смене
settings-menu-deauth-onchange-option-description = Выходить из аккаунта после смены сервера авторизации?

settings-cdns-option = CDN
settings-cdns-option-description = Настройте список CDN-серверов, с которых загружается движок клиента.
settings-cdns-option-add = Добавить CDN
settings-cdns-option-empty = Нет настроенных CDN. Нажмите +, чтобы добавить.
settings-cdns-option-name-label = Название CDN
settings-cdns-option-important-tooltip = Важный
settings-cdns-option-enabled-tooltip = Включён
settings-cdns-option-key-label = Публичный ключ CDN
settings-cdns-option-mirrors-label = Зеркала CDN
settings-cdns-option-add-mirror = Добавить зеркало CDN
settings-cdns-option-important-warning-title = Изменение важного CDN
settings-cdns-option-important-warning-body =
    Вы собираетесь изменить CDN-сервер, отмеченный как важный.
    Это сломает часть функций приложения.
    Убедитесь, что вы понимаете, что делаете.
settings-cdns-option-important-warning-hint =
    Это сломает авторизацию через Discord.
    Действуйте осторожно.
settings-cdns-option-important-warning-cancel = Отмена
settings-cdns-option-important-warning-confirm = Подтвердить
settings-cdns-option-key-unlock-tooltip = Разблокировать редактирование публичных ключей
settings-cdns-option-key-lock-tooltip = Заблокировать редактирование публичных ключей
settings-cdns-option-key-unlock-warning-title = Редактировать публичные ключи CDN?
settings-cdns-option-key-unlock-warning = Публичные ключи определяют, каким сборкам движка доверяет лаунчер. Добавляйте только ключи, полученные напрямую от владельца CDN: неверный ключ сломает загрузку движка, а ключ от того, кому вы не доверяете, позволит ему запускать свой код на вашем компьютере. Смена ключа у CDN Starlight сломает вход через Discord и Steam.

# Appearance

settings-menu-theme-option = Тема
settings-menu-theme-option-description = Выберите тему для приложения.

settings-menu-navigation-bar-option = Положение навигации
settings-menu-navigation-bar-option-description = Выберите положение навигационной панели в приложении.

settings-menu-animated-background-option = Анимированный фон
settings-menu-animated-background-option-description = Движущиеся звёзды на фоне и перелёты камеры между страницами. Выключение снижает нагрузку на процессор, пока лаунчер открыт.

settings-menu-bottom-search-option = Строка поиска снизу
settings-menu-bottom-search-option-description = Если включено, строка с поиском будет находиться в самом низу панели инструментов (когда панель с тегами и панель с поиском вместе).

settings-menu-bottom-search-bar-option = Нижняя строка поиска
settings-menu-bottom-search-bar-option-description = Если включено, строка с поиском будет находиться внизу приложения.

settings-menu-bottom-tags-bar-option = Нижняя панель фильтров
settings-menu-bottom-tags-bar-option-description = Если включено, панель с фильтрами будет располагаться внизу списка серверов.

settings-menu-tags-bar-open-option = Развернутая панель фильтров
settings-menu-tags-bar-open-option-description = Если включено, то панель фильтров будет всегда открыта по умолчанию при открытии списка серверов. (Но вы все еще сможете ее свернуть!!!)

# Tray

settings-menu-tray-collapse-start-option = Свернуть при запуске
settings-menu-tray-collapse-start-option-description = Запускать приложение свернутым в трей.

settings-menu-tray-collapse-run-option = Свернуть при игре
settings-menu-tray-collapse-run-option-description = Сворачивать приложение в трей при запуске игры.

settings-menu-tray-uncollapse-end-option = Развернуть в конце
settings-menu-tray-uncollapse-end-option-description = Разворачивать приложение из трея при окончании игры.

settings-menu-tray-collapse-close-option = Свернуть при закрытии
settings-menu-tray-collapse-close-option-description = Сворачивать приложение в трей при попытке закрыть его.

settings-menu-tray-collapse-minimize-option = Свернуть при минимизировании
settings-menu-tray-collapse-minimize-option-description = Сворачивать приложение в трей при попытке его минимизировать.

# Discord RPC

settings-menu-discord-presence-option = Discord Presence
settings-menu-discord-presence-option-description = Показать статус в вашем Discord.

settings-menu-discord-presence-buttons-option = Кнопки в статусе
settings-menu-discord-presence-buttons-option-description = Показывать кнопку «Скачать» и другие «рекламные» кнопки в Discord Rich Presence?

settings-menu-discord-presence-states-option = Состояния статуса
settings-menu-discord-presence-states-option-description = Настройте состояния и их приоритет для вашего статуса в Discord.

# Development(Advanced)

settings-menu-development-gles-option = Режим Совместимости (GLES2)
settings-menu-development-gles-option-description = Включить режим совместимости для устройств с устаревшим графическим оборудованием. Это может привести к снижению производительности на современных системах.

settings-menu-development-signing-option = Выключить верификацию сигнатур игрового движка
settings-menu-development-signing-option-description = Отключить проверку подписи файлов движка. Эта функция предназначена для разработки и может представлять угрозу безопасности, если будет включена в релизной сборке. (Работает только с конфигурацией "Debug")

settings-menu-development-engine-override-option = Переопределение движка
settings-menu-development-engine-override-option-description = Переопределить путь к исполняемому файлу движка по умолчанию. Эта настройка предназначена для целей разработки и может вызвать проблемы в случае неправильной настройки.

settings-menu-development-engine-override-path-option = Путь Переопределения Движка
settings-menu-development-engine-override-path-option-description = Путь к исполняемому файлу движка, который будет использоваться при включенной функции "Переопределение движка". Этот путь должен указывать на действительный исполняемый файл движка.
settings-menu-development-engine-override-path-option-helper = Укажите путь к исполняемому файлу движка
settings-menu-development-engine-override-path-option-label = Путь к движку

settings-menu-development-robust-manifest-cache-time-option = Время хранения кэша манифеста
settings-menu-development-robust-manifest-cache-time-option-description = Укажите время, на которое следует кэшировать данные манифеста.

settings-menu-development-max-versions-option = Максимальное количество версий
settings-menu-development-max-versions-option-description = Укажите максимальное количество версий, которые необходимо сохранить в истории версий. При превышении этого лимита более старые версии будут удалены.

settings-menu-development-max-fork-versions-option = Максимальное количество версий форков
settings-menu-development-max-fork-versions-option-description = Укажите максимальное количество версий форка, которые необходимо сохранить в истории версий. Старые версии форка будут удаляться при превышении этого лимита.

# Development tab alert

settings-development-tab-alert-title = Предупреждение о вкладке разработки
settings-development-tab-alert-description =
    Вы пытаетесь открыть вкладку, предназначенную только для опытных пользователей и разработчиков.
    Чтобы продолжить, вы должны согласиться с тем, что после изменения любых настроек
    STARLIGHT не гарантирует корректную работу программы, а также не гарантирует, что она не повредит ваше оборудование.

# Ignored

settings-menu-ignored-servers-option = Игнорируемые серверы
settings-menu-ignored-servers-option-description = Позволяет убрать серверы из списка игнорируемых.

settings-ignored-servers-option-remove = Убрать
# Data

settings-menu-data-category = Данные
settings-menu-clear-button = Очистить
settings-menu-clear-confirm-yes = Удалить
settings-menu-clear-engines-option = Загруженные движки
settings-menu-clear-engines-option-description = Удаляет все загруженные версии движка и модули. Они будут скачаны заново при следующем подключении к серверу.
settings-menu-clear-engines-confirm-title = Удалить загруженные движки?
settings-menu-clear-engines-confirm-text = Все загруженные версии движка и модули будут удалены. Подключение к серверу займёт больше времени, пока движок скачивается заново. Это действие нельзя отменить.
settings-menu-clear-engines-success = Загруженные движки удалены.
settings-menu-clear-content-option = Игровой контент
settings-menu-clear-content-option-description = Удаляет весь загруженный контент серверов. Он будет скачан заново при следующем подключении к серверу.
settings-menu-clear-content-confirm-title = Удалить игровой контент?
settings-menu-clear-content-confirm-text = Весь загруженный контент всех серверов будет удалён. Подключение к серверу займёт больше времени, пока контент скачивается заново. Это действие нельзя отменить.
settings-menu-clear-content-success = Игровой контент удалён.
settings-menu-clear-busy = Дождитесь окончания текущей загрузки и попробуйте снова.
settings-menu-clear-client-running = Закройте запущенную игру и попробуйте снова.
settings-menu-clear-failed = Не удалось удалить данные. Подробности в логах лаунчера.

settings-menu-development-client-category = Игровой клиент
settings-menu-development-tracy-option = Профайлер Tracy
settings-menu-development-tracy-option-description = Запускать игру с включённым профайлером Tracy (prof.tracy.enabled). Нужен профайлер версии 0.13.1, другие версии не подключатся. Немного снижает производительность.
settings-menu-development-tracy-localhost-option = Tracy: только localhost
settings-menu-development-tracy-localhost-option-description = Принимать подключения Tracy только с этого компьютера. Если выключено, Tracy слушает на всех сетевых интерфейсах.
settings-menu-development-tracy-launch-option = Tracy: открывать профайлер автоматически
settings-menu-development-tracy-launch-option-description = Скачать Tracy 0.13.1 при первом запуске и открыть его уже подключённым к игре. Только Windows, на Linux профайлер нужно собрать самостоятельно.
settings-menu-development-crash-dumps-option = Дампы при краше
settings-menu-development-crash-dumps-option-description = Записывать дамп памяти, когда игра падает, включая нативные краши (access violation / SIGSEGV). Открывается через dotnet-dump analyze, WinDbg или Visual Studio. Дамп может занимать больше гигабайта. На Linux и macOS рядом пишется читаемый .crashreport.json со стеками всех потоков.
settings-menu-development-crash-dumps-retain-option = Сколько дампов хранить
settings-menu-development-crash-dumps-retain-option-description = Старые дампы удаляются перед каждым запуском.
settings-menu-development-crash-dumps-folder-option = Папка с дампами
settings-menu-development-crash-dumps-folder-option-description = Путь к дампу краша также пишется в лог клиента.
settings-menu-development-crash-dumps-folder-button = Открыть
settings-menu-development-cvars-option = Переопределение CVar клиента
settings-menu-development-cvars-option-description = Дополнительные cvar, передаваемые игровому клиенту при запуске. Cvar, которые сервер реплицирует клиентам, всё равно будут перезаписаны сервером.
settings-menu-development-cvars-option-helper = По одной паре key=value на строку или через ";", строки с # игнорируются

# Audio

settings-menu-audio-category = Звук
settings-menu-audio-output-mode-option = Конфигурация динамиков
settings-menu-audio-output-mode-option-description = Под какую акустику игра сводит звук. Если пропадают голоса или часть звуков, попробуйте «Стерео» или «Наушники». Работает с движком Starlight.
settings-menu-audio-output-mode-auto = Автоматически
settings-menu-audio-output-mode-stereo = Стерео
settings-menu-audio-output-mode-headphones = Наушники (HRTF)
settings-menu-audio-output-mode-quad = Квадро
settings-menu-audio-output-mode-surround51 = Объёмный 5.1
settings-menu-audio-output-mode-surround71 = Объёмный 7.1
settings-menu-audio-output-mode-mono = Моно
settings-menu-audio-follow-device-option = Следовать системному устройству вывода
settings-menu-audio-follow-device-option-description = Переключать звук игры вслед за системным устройством по умолчанию, например при подключении наушников. Работает с движком Starlight.
settings-menu-diagnostics-bundle-option = Собрать диагностику
settings-menu-diagnostics-bundle-option-description = Упаковать последние логи игры и лаунчера, отчёты о крашах, данные о системе и видеокарте в один zip для разработчиков. В логах есть ваш никнейм и серверы, на которые вы заходили.
settings-menu-diagnostics-bundle-button = Собрать
settings-menu-diagnostics-bundle-success = Диагностика сохранена в { $path }
settings-menu-diagnostics-bundle-failed = Не удалось собрать диагностику, подробности в логе лаунчера.
client-diagnostics-title = Запущенная игра
client-diagnostics-running = PID клиента { $pid }. Счётчики .NET runtime в реальном времени, как в dotnet-counters.
client-diagnostics-not-running = Игра не запущена.
client-diagnostics-dump-button = Снять дамп
client-diagnostics-dump-hint = Пока пишется дамп, игра замирает на несколько секунд. Нужен для зависаний и проблем с памятью; краши ловит опция дампов при краше.
client-diagnostics-dump-success = Дамп сохранён в { $path }
client-diagnostics-dump-failed = Не удалось снять дамп, подробности в логе лаунчера.
client-diagnostics-cpu = Загрузка CPU
client-diagnostics-working-set = Рабочий набор
client-diagnostics-gc-heap = Размер кучи GC
client-diagnostics-alloc-rate = Скорость аллокаций
client-diagnostics-time-in-gc = Время в GC
client-diagnostics-gen0 = Сборки Gen 0
client-diagnostics-gen1 = Сборки Gen 1
client-diagnostics-gen2 = Сборки Gen 2
client-diagnostics-threadpool-queue = Очередь пула потоков
client-diagnostics-threadpool-threads = Потоки пула
client-diagnostics-lock-contention = Конкуренция за локи
client-diagnostics-exceptions = Исключения

# Content

settings-menu-content-category = Контент
settings-menu-downloads-category = Загрузки
settings-menu-download-speed-limit-option = Ограничение скорости загрузки (КБ/с)
settings-menu-download-speed-limit-option-description = Ограничивает скорость скачивания контента серверов, движков и модулей, чтобы лаунчер не занимал весь канал. 0 — без ограничения.
settings-menu-preload-favorites-option = Предзагрузка избранных серверов
settings-menu-preload-favorites-option-description = Пока лаунчер простаивает, в фоне скачивает обновления избранных серверов, чтобы подключение к ним было мгновенным. Загружаются только серверы, чью политику конфиденциальности вы уже приняли.
settings-menu-storage-category = Хранилище
settings-storage-option = Занятое место
settings-storage-option-description = Сколько места занимает загруженный контент каждого форка, каждая версия движка и модуль.
settings-storage-refresh = Обновить
settings-storage-content-section = Контент серверов · { $size }
settings-storage-content-empty = Контент серверов ещё не загружен.
settings-storage-unknown-fork = Неизвестный форк
settings-storage-fork-versions = Версий: { $count }
settings-storage-shared-note = Общие для нескольких форков файлы учитываются у каждого из них, поэтому сумма может быть больше размера базы.
settings-storage-engines-section = Движки и модули · { $size }
settings-storage-engines-empty = Движки ещё не загружены.
settings-storage-engine = Движок
settings-menu-verify-content-option = Проверить и починить контент
settings-menu-verify-content-option-description = Сверяет каждый загруженный файл с его хешем и удаляет повреждённые. Версии серверов, которые их использовали, будут скачаны заново при следующем подключении. Может занять несколько минут.
settings-menu-verify-content-button = Проверить
settings-menu-verify-content-ok = Проблем не найдено, проверено файлов: { $count }.
settings-menu-verify-content-repaired = Исправлено: удалено повреждённых файлов: { $corrupt }, версий серверов к повторной загрузке: { $versions }, удалено лишних записей: { $orphans }.
settings-menu-verify-content-rebuilt = База контента была повреждена и создана заново. Контент серверов будет скачан заново при следующем подключении.
settings-menu-verify-content-failed = Не удалось проверить контент. Подробности в логах лаунчера.
settings-menu-verify-content-cancelled = Проверка контента отменена.
