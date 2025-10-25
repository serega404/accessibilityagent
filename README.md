# AccessibilityAgent

Инструмент командной строки для проверки доступности хоста и сетевых сервисов. Поддерживает ICMP ping, DNS‑резолвинг, TCP/UDP‑порты, HTTP‑запросы и составные сценарии из нескольких проверок. Умеет печатать человекочитаемый вывод и структурированный JSON.

- Целевая платформа: .NET 9 (консольное приложение)
- Сборка: self-contained, single-file, с поддержкой AOT/trim (см. [Dockerfile](src/Dockerfile)).
- Вывод: текст или JSON (флагами), каждый чек — независимый результат с полем success
- 38 автотестов (xUnit).

## Возможности

- Ping: ICMP‑эхо до узла с измерением RTT
- DNS: разрешение имени хоста в IP‑адреса с тайм‑аутом
- TCP: попытка установления соединения на указанный порт
- UDP: отправка датаграммы и, при необходимости, ожидание ответа
- HTTP: произвольный HTTP‑запрос (метод, заголовки, тело)
- Composite: комбинированный режим, позволяющий собрать набор проверок и выполнить их параллельно с общей сводкой

## Быстрый старт

Минимальные требования: установлен SDK .NET 9.

```bash
# Запуск без сборки (из исходников)
cd AccessibilityAgent
DOTNET_CLI_UI_LANGUAGE=en dotnet run -- ping example.com --timeout 3000

# Локальная сборка Debug и запуск бинарника
dotnet build AccessibilityAgent/AccessibilityAgent.csproj -c Debug
./AccessibilityAgent/bin/Debug/net9.0/AccessibilityAgent ping example.com --timeout 3000
```

Примечание: для стабильности примеров выше принудительно включён английский вывод `DOTNET_CLI_UI_LANGUAGE=en` только при запуске через `dotnet run` — сам инструмент печатает свои сообщения без локализации.

## Установка и сборка

### Сборка Release (self-contained, single-file)

```bash
# Linux x64 (glibc)
dotnet publish AccessibilityAgent/AccessibilityAgent.csproj \
  -c Release -r linux-x64 -o out --self-contained true /p:PublishSingleFile=true

# macOS (x64/arm64)
dotnet publish AccessibilityAgent/AccessibilityAgent.csproj \
  -c Release -r osx-arm64 -o out --self-contained true /p:PublishSingleFile=true

# Windows (x64)
dotnet publish AccessibilityAgent/AccessibilityAgent.csproj \
  -c Release -r win-x64 -o out --self-contained true /p:PublishSingleFile=true
```

Собранный исполняемый файл будет в каталоге `out/AccessibilityAgent` (или `AccessibilityAgent.exe` на Windows).

### AOT для минимального размера/старта (через Docker)

В репозитории есть `Dockerfile`, который собирает AOT‑бинарь под `linux-musl-x64` (Alpine). Это даёт быстрый старт и малую зависимость от среды.

```bash
# Сборка образа
docker build -t accessibilityagent:start -f Dockerfile .

# Запуск проверки (пример TCP)
docker run --rm accessibilityagent:start tcp google.com --port 443 --timeout 3000
```

ICMP (ping) внутри контейнера может требовать дополнительные привилегии:

```bash
# Для использования ICMP echo в контейнере
# (минимально — NET_RAW; при необходимости добавьте NET_ADMIN)
docker run --rm --cap-add=NET_RAW --cap-add=NET_ADMIN accessibilityagent:start ping 8.8.8.8 --timeout 2000
```

Альтернатива для нативного бинаря в хост‑системе Linux:

```bash
# Разрешить ICMP без sudo для конкретного файла
sudo setcap cap_net_raw+ep /path/to/AccessibilityAgent
```

## Использование CLI

Общий шаблон:

```text
accessibilityagent <command> [options]
```

Поддерживаемые команды:

- ping   HOST [--timeout MS] [--format json]
- dns    HOST [--timeout MS] [--format json]
- tcp    HOST PORT [--timeout MS] [--format json]
- udp    HOST PORT [--payload TEXT] [--expect-response] [--timeout MS] [--format json]
- http   URL [--method VERB] [--timeout MS] [--header key=value] [--body TEXT] [--format json]
- check  HOST [protocol-specific options] [--format json]
- agent  [--server URL] [--token VALUE] [--name NAME] [...options]

Глобальная справка:

```bash
accessibilityagent help
accessibilityagent <command> --help
```

Общие флаги формата вывода (для любой команды):

- `--format json` или `--json` — JSON‑вывод (по умолчанию — текст)

### Команда: ping

```bash
accessibilityagent ping HOST [--timeout MS] [--format json]
```

- Проверяет доступность по ICMP. Возвращает RTT, адрес и статус ICMP при успехе.
- Тайм‑аут по умолчанию: 4000 мс.

### Команда: dns

```bash
accessibilityagent dns HOST [--timeout MS] [--format json]
```

- Разрешает доменное имя. Возвращает список IP‑адресов.
- Тайм‑аут по умолчанию: 4000 мс.

### Команда: tcp

```bash
accessibilityagent tcp HOST PORT [--timeout MS] [--format json]
```

- Пытается установить TCP‑соединение с указанным портом.
- Тайм‑аут по умолчанию: 3000 мс.

Эквиваленты опций порта: `--port`, `-p`. Порт должен быть в диапазоне 1–65535.

### Команда: udp

```bash
accessibilityagent udp HOST PORT [--payload TEXT] [--expect-response] [--timeout MS] [--format json]
```

- Отправляет UDP‑датаграмму; если задан `--expect-response`, ждёт ответ до тайм‑аута.
- Тайм‑аут по умолчанию: 2000 мс.

Особенности: UDP ненадёжен — отсутствие ответа не всегда означает недоступность сервиса.

### Команда: http

```bash
accessibilityagent http URL \
  [--method VERB] [--timeout MS] \
  [--header key=value]... [--body TEXT] [--content-type MIME] \
  [--format json]
```

- Выполняет HTTP‑запрос, по умолчанию `GET`.
- Тайм‑аут по умолчанию: 5000 мс.
- Заголовки задаются как `key=value` (или `key:value`).
- Если указано `--body`, будет отправлено тело с `Content-Type` (по умолчанию `text/plain`).

Поддерживаемые алиасы: `--url`/`-u`, `--method`/`-m`, `--header`/`-H`, `--body`/`--data`.

### Команда: check (составная)

Позволяет собрать набор проверок и выполнить их параллельно. Удобно для быстрой диагностики.

```bash
accessibilityagent check <host> [options]

# Общие и протокольные опции
--skip-ping                  # Не выполнять ping
--skip-dns                   # Не выполнять DNS
--tcp-port <value>           # Добавить TCP‑порт (повторяемая опция)
--udp-port <value>           # Добавить UDP‑порт (повторяемая опция)
--udp-payload <text>         # Тело UDP‑датаграммы
--udp-expect-response        # Ждать ответ по UDP
--http                       # Проверить http://HOST/ (или https при --https)
--https                      # Использовать HTTPS при конструировании URL
--http-url <value>           # Явный HTTP‑URL (повторяемая опция)
--http-port <value>          # Сконструировать URL из host и порта (повторяемая)
--http-path <value>          # Путь для сконструированных URL (по умолчанию '/')

# Таймауты
--timeout <ms>               # Общий тайм‑аут по умолчанию для всех проверок
--ping-timeout <ms>
--dns-timeout <ms>
--tcp-timeout <ms>
--udp-timeout <ms>
--http-timeout <ms>

# HTTP‑параметры
--http-method <verb>         # По умолчанию GET
--http-header key=value      # Доп. заголовки (повторяемо)
--http-body <text>           # Тело запроса
--http-content-type <mime>   # Тип содержимого тела

# Формат вывода
--format json                # JSON‑массив результатов
```

Логика конструирования HTTP‑URL в check‑режиме:

- Если указаны `--http-url`, используются ровно эти URL (каждый — отдельный чек).
- Если задан флаг `--http` без портов, конструируется `http(s)://HOST/PATH`.
- Если указаны `--http-port`, для каждого порта строится `http(s)://HOST:PORT/PATH`.
- Флаг `--https` переключает схему на `https`.

Все включённые проверки выполняются параллельно. Код выхода равен 0 только если все результаты `success=true`. Если ни одна проверка не выбрана (например, `--skip-ping --skip-dns` без других опций), будет выведен пустой массив `[]`, а код выхода — 0.

## Примеры

### Текстовый вывод

```bash
accessibilityagent tcp example.com --port 443
OK   tcp:example.com:443: TCP handshake successful (85.8 ms)
```

### JSON‑вывод

```bash
accessibilityagent check example.com --tcp-port 80 --http --format json
[
  {
    "check": "tcp:example.com:80",
    "success": true,
    "message": "TCP handshake successful",
    "durationMs": 42.1,
    "data": {
      "remoteEndPoint": "93.184.216.34:80"
    }
  },
  {
    "check": "http:http://example.com/",
    "success": true,
    "message": "HTTP 200 OK",
    "durationMs": 73.5,
    "data": {
      "statusCode": "200",
      "reasonPhrase": "OK",
      "headers": "server: ECS (dcb/7F5E)"  
    }
  }
]
```

Примечание: список полей в `data` зависит от типа проверки и ситуации (успех/ошибка/тайм‑аут). Числовые значения сериализуются как строки в `data` для предсказуемости.

## Схема JSON‑вывода

Инструмент всегда печатает массив JSON. Каждый элемент массива — объект результата отдельной проверки:

- `check` (string) — идентификатор проверки, например `ping:example.com`, `tcp:host:443`, `http:https://host/`.
- `success` (boolean) — итог проверки.
- `message` (string) — краткое человекочитаемое описание.
- `durationMs` (number, optional) — длительность выполнения (миллисекунды, округляется до 0.001).
- `data` (object<string,string>, optional) — дополнительные метаданные. Значения всегда строки для предсказуемости парсинга.

## Коды возврата

- 0 — успех (для составной проверки — когда все чекапы успешны; также когда ничего не выбрано — `[]`)
- 1 — ошибка валидации входных параметров или хотя бы один чек завершился неуспехом
- 2 — непредвиденная ошибка выполнения (исключение вне ожидаемых сценариев)

## Нюансы платформы и окружения

- ICMP (ping) на Linux без привилегий может завершаться сообщением вида "operation not permitted". Это ожидаемо. Для пинга без `sudo` в бинарник можно добавить capability `cap_net_raw` (см. выше) или запускать контейнер с `--cap-add=NET_RAW`.
- Межсетевые экраны и NAT могут влиять на UDP: отсутствие ответа не равно недоступности сервиса.
- Резолвинг DNS и HTTP‑запросы зависят от настроек прокси/сети окружения. Тайм‑ауты можно увеличивать опциями.

## Режим агента (agent)

Агент — это длительно живущий процесс, который подключается к координатору по Socket.IO и принимает задания на выполнение проверок удалённо. Подходит для распределённого мониторинга из разных сетевых зон.

- Транспорт: Socket.IO (WebSocket, путь `/socket.io`, совместимость с Engine.IO v3)
- Аутентификация: токен в query/auth (`token`) и имя агента (`agent`)
- Поддерживаемые типы заданий: `ping`, `dns`, `tcp`, `udp`, `http`, `check` (составная)
- Одновременное выполнение: агент обрабатывает только одно задание за раз; внутри составного задания проверки выполняются параллельно

### Как запустить

```bash
# Минимальный запуск (переменные окружения)
export AA_SERVER_URL="http://localhost:8080"    # URL координатора Socket.IO
export AA_AGENT_TOKEN="devtoken"                # общий токен доступа
accessibilityagent agent                         # имя возьмётся из имени хоста

# Явно через флаги с метаданными
accessibilityagent agent \
  --server http://localhost:8080 \
  --token devtoken \
  --name agent-1 \
  --heartbeat 30000 \
  --metadata env=prod --metadata region=ru-msk-1
```

Остановка — Ctrl+C. Коды возврата: 0 при штатном завершении/отмене, 1 при ошибке запуска, 2 — для непредвиденных исключений.

### Опции и переменные окружения

- `--server <url>` или `AA_SERVER_URL` — адрес координатора Socket.IO (обязательно)
- `--token <value>` или `AA_AGENT_TOKEN` — токен доступа (обязательно)
- `--name <value>` или `AA_AGENT_NAME` — имя агента (по умолчанию имя машины)
- `--reconnect-delay <ms>` — стартовая задержка реконнекта, по умолчанию 2000
- `--reconnect-delay-max <ms>` — максимальная задержка реконнекта, по умолчанию 30000
- `--reconnect-attempts <n>` — ограничение числа попыток (по умолчанию без лимита)
- `--heartbeat <ms>` — интервал heartbeat (0 — выключить), по умолчанию 30000
- `--metadata key=value` — дополнительные метаданные (можно повторять)

### Запуск агента через Docker

Ниже приведены готовые команды для запуска агента в контейнере из образа:

Образ: `git.zetcraft.ru/whales/accessibilityagent/accessibilitychecker:main`

Базовый запуск (переменные окружения):

```bash
docker run -d --name accessibilityagent \
  -e AA_SERVER_URL="http://coordinator.example.com:8080" \
  -e AA_AGENT_TOKEN="<секретный_токен>" \
  -e AA_AGENT_NAME="agent-1" \
  --restart unless-stopped \
  git.zetcraft.ru/whales/accessibilityagent/accessibilitychecker:main \
  agent
```

Запуск с флагами CLI (без env):

```bash
docker run -d --name accessibilityagent \
  --restart unless-stopped \
  git.zetcraft.ru/whales/accessibilityagent/accessibilitychecker:main \
  agent \
  --server http://coordinator.example.com:8080 \
  --token <секретный_токен> \
  --name agent-1 \
  --heartbeat 30000 \
  --metadata env=prod --metadata region=ru-msk-1
```

ICMP (ping) в контейнере требует capability `NET_RAW` (и иногда `NET_ADMIN`). Если пинг не критичен, можно обойтись без дополнительных прав — просто не используйте ping‑проверки. Если пинг нужен:

```bash
docker run -d --name accessibilityagent \
  --cap-add=NET_RAW --cap-add=NET_ADMIN \
  -e AA_SERVER_URL="http://coordinator.example.com:8080" \
  -e AA_AGENT_TOKEN="<секретный_токен>" \
  --restart unless-stopped \
  git.zetcraft.ru/whales/accessibilityagent/accessibilitychecker:main \
  agent
```

Полезные приёмы:

- Просмотр логов: `docker logs -f accessibilityagent`
- Обновление до свежего образа: `docker pull git.zetcraft.ru/whales/accessibilityagent/accessibilitychecker:main && docker restart accessibilityagent`

### Протокол взаимодействия с координатором

События Socket.IO, которыми обменивается агент:

- `agent:register` (исходящее от агента) — регистрация
  - payload: `{ agent: string, version?: string, capabilities: string[], metadata?: object|null, runtime?: object }`
  - capabilities у .NET‑агента: `["ping","dns","tcp","udp","http","check"]`

- `agent:heartbeat` (исходящее) — пульс каждые `--heartbeat` мс
  - payload: `{ agent: string, timestamp: ISO, uptimeSeconds: number }`

- `job:request` (входящее) — координатор присылает задание
  - payload: `{ id: string, type: string, payload?: object|null, metadata?: object|null }`

- `job:accepted` (исходящее) — агент принял задание
  - payload: `{ jobId: string, agent: string, type: string, receivedAt: ISO, metadata?: object|null }`

- `job:result` (исходящее) — результат выполнения
  - payload: `{ jobId: string, agent: string, success: boolean, error?: string, errorDetails?: string, completedAt: ISO, checks: CheckResult[], metadata?: object|null }`
  - `CheckResult` соответствует схеме JSON‑вывода утилиты: `{ check, success, message, durationMs?, data? }`

Примечания реализации:

- Агент использует query‑параметры `token` и `agent` при подключении. Также совместим с `handshake.auth`.
- При потере связи включается авто‑реконнект с нарастающей задержкой в пределах `--reconnect-delay --reconnect-delay-max`.
- Если `--heartbeat 0`, heartbeat не отправляется.
- Агент обрабатывает задания последовательно (очередь по одному), но внутри `check` все выбранные подпроверки запускаются параллельно.

### Типы заданий и формат payload

Во всех случаях корень запроса выглядит так:

```json
{
  "id": "job-123",
  "type": "<ping|dns|tcp|udp|http|check>",
  "payload": { /* параметры задания, см. ниже */ },
  "metadata": { "any": "data" }
}
```

- ping
  - payload: `{ "host": string, "timeoutMs"?: number }`

- dns
  - payload: `{ "host": string, "timeoutMs"?: number }`

- tcp
  - payload: `{ "host": string, "port": number (1..65535), "timeoutMs"?: number }`

- udp
  - payload: `{ "host": string, "port": number, "payload"?: string, "expectResponse"?: boolean, "timeoutMs"?: number }`

- http
  - payload: `{ "url": string, "method"?: string (по умолчанию GET), "timeoutMs"?: number, "body"?: string, "contentType"?: string, "headers"?: string[] | Record<string,string> }`
  - Заголовки допускаются в формах `key=value`, `key:value`, а также объектом `{key: value}`.

- check (составная)
  - обязательные: `host: string`
  - общие: `timeoutMs?`
  - опции:
    - `skipPing?`, `skipDns?`
    - `tcpPorts?: number|number[]`, `tcpTimeoutMs?`
    - `udpPorts?: number|number[]`, `udpPayload?: string`, `udpExpectResponse?: boolean`, `udpTimeoutMs?`
    - HTTP: либо `httpUrls?: string[]`, либо флажки `http?: boolean`, `https?: boolean` с конструкцией URL из `host`
      - дополнительные: `httpPorts?: number|number[]`, `httpPath?: string` (по умолчанию `/`), `httpMethod?`, `httpTimeoutMs?`, `httpBody?`, `httpContentType?`, `httpHeaders?: string[] | Record<string,string>`

Успех всего задания определяется по конъюнкции результатов подпроверок (`success=true` только если все подпроверки успешны). Если в `check` не выбраны никакие подпроверки, возвращается пустой массив `checks: []` и `success=true`.

Ошибки валидации входных параметров (например, порт вне диапазона) приводят к `success=false` и заполненным полям `error`, `errorDetails` в `job:result`.

### Безопасность и права

- Настройте секретный токен координатора и передавайте его агенту (переменная `AA_AGENT_TOKEN` / флаг `--token`).
- Для ICMP‑пингов без root на Linux добавьте capability `cap_net_raw` бинарю или запускайте контейнер с `--cap-add=NET_RAW`.
- Агент не исполняет произвольные команды — только предопределённые сетевые проверки выше.

### Диагностика

- Агент пишет логи в stdout/stderr с временными метками.
- При обрывах соединения предпринимаются автоматические попытки переподключения; интервалы регулируются флагами реконнекта.

## Разработка

### Структура проекта

- `AccessibilityAgent/` — исходный код приложения
  - `Cli/` — парсинг аргументов (`ParsedArguments`) и диспетчер команд (`CommandDispatcher`)
  - `Checks/` — реализации проверок и вспомогательные типы (`NetworkChecks`, `CheckResult`, `MetadataBuilder`)
  - `Output/` — форматирование результатов (`ResultWriter`, режимы Text/JSON)
- `AccessibilityAgent.Tests/` — модульные и интеграционные тесты (xUnit)
- `Dockerfile` — сборка AOT под Alpine (musl)

### Запуск тестов

```bash
dotnet test AccessibilityAgent.sln --nologo --verbosity minimal
```
