# Page

local-server-page-warning-banner = Локальные серверы запускаются без защиты и без проверки кода из источника, который вы указали ниже. Вы несёте полную ответственность за то, что скачиваете и запускаете - Starlight не несёт ответственности за возможный ущерб.

# One-time policy gate

local-server-policy-alert-title = Предупреждение о локальном сервере
local-server-policy-alert-description =
    Страница "Локальный сервер" скачивает и запускает серверную сборку по указанному вами URL манифеста без какой-либо защиты.
    Продолжая, вы соглашаетесь с тем, что несёте полную ответственность за любую сборку, которую решите запустить, и что STARLIGHT
    не гарантирует корректную работу ПО и не гарантирует отсутствие ущерба вашему оборудованию.

# Sources

local-server-sources-title = Источники
local-server-sources-option-title = Источники манифестов
local-server-sources-option-description = URL манифестов, из которых будут загружаться сборки локального сервера.
local-server-sources-empty = Источники не настроены. Нажмите +, чтобы добавить.
local-server-sources-add-tooltip = Добавить источник
local-server-sources-name-label = Название
local-server-sources-url-label = URL манифеста

local-server-source-warning-title = Предупреждение о новом источнике
local-server-source-warning-body = Вы собираетесь добавить новый источник неподписанных серверных сборок без песочницы. Добавляйте только те источники, которым доверяете.
local-server-source-warning-hint = Добавление нового источника локального сервера.
local-server-source-warning-cancel = Отмена
local-server-source-warning-confirm = Я понимаю, добавить

# Launch

local-server-launch-title = Запуск
local-server-source-select-label = Источник
local-server-refresh-button = Обновить
local-server-latest-build-info = Последняя сборка { $hash } ({ $time }) - { $size } для вашей платформы.
local-server-unsupported-platform = В этом манифесте нет серверной сборки для вашей платформы.
local-server-start-button = Запустить
local-server-stop-button = Остановить
local-server-connect-button = Подключиться
local-server-connecting-title = Подключение
local-server-open-folder-button = Открыть папку

local-server-clear-description = Удалить с диска все скачанные и распакованные сборки локального сервера.
local-server-clear-button = Очистить установленные серверы
local-server-clear-confirm-title = Очистить установленные серверы
local-server-clear-confirm-text = Это остановит запущенный локальный сервер (если он запущен) и удалит с диска все скачанные сборки. Чтобы запустить их снова, потребуется скачать заново.
local-server-clear-confirm-yes = Очистить
local-server-clear-confirm-cancel = Отмена
local-server-clear-done = Установленные локальные серверы очищены.

local-server-status-idle = Ожидание
local-server-status-fetching = Получение манифеста...
local-server-status-downloading = Загрузка... { $percent }
local-server-status-extracting = Распаковка...
local-server-status-starting = Запуск…
local-server-status-running = Работает
local-server-status-stopping = Остановка...
local-server-status-stopped = Остановлен
local-server-status-error = Ошибка

# Server configuration

local-server-config-title = Конфигурация сервера
local-server-config-no-source = Выберите источник выше, чтобы настроить его server_config.toml.
local-server-config-basic-title = Базовые опции
local-server-config-custom-title = Пользовательские опции
local-server-config-custom-empty = Пользовательские CVar-ы не добавлены.
local-server-config-add-tooltip = Добавить CVar
local-server-config-group-placeholder = Группа
local-server-config-name-placeholder = Имя
local-server-config-value-placeholder = Значение
local-server-config-type-string = Строка
local-server-config-type-int = Целое
local-server-config-type-float = Дробное
local-server-config-type-bool = Булево
local-server-config-save-button = Сохранить конфигурацию
local-server-config-saved = Конфигурация сервера сохранена.
local-server-config-hint = Изменения записываются в server_config.toml при следующем запуске сервера.

# Console

local-server-console-title = Консоль
local-server-console-empty = Пока нет вывода.
local-server-console-clear-tooltip = Очистить консоль
local-server-console-autoscroll-tooltip = Вкл/выкл авто-прокрутку
local-server-console-input-placeholder = Введите команду сервера...
local-server-console-send-failed = Не удалось отправить команду - сервер не запущен.
