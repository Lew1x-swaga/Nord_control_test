# Протокол

`v` всегда в JSON. Несовместимость — новый `v`, отказ `join_reject` с `reason=version`, процессы живы.

## Кадр TCP

Big-endian:

```text
uint32 payloadLength     // длина (type + payload), не включая эти 4 байта
uint8  messageType       // 1 = UTF-8 JSON, 2 = JPEG (этап 2)
bytes  payload
```

Максимум кадра: 4 MiB. Больше — закрыть сокет, не падать всему процессу, ученик идёт в Reconnecting.

Тип 1: JSON-объект, поле `"type"` в snake_case, `"v": 1`.

Тип 2 (этап 2): `uint32 width`, `uint32 height`, `uint64 ts_ms`, далее JPEG. Не JSON и не base64.

## Константы

| Имя | Значение |
|---|---|
| UDP порт | 47820 |
| TCP порт | 47821 |
| ProtocolVersion | 1 |
| AgentVersion | 0.1.0 |
| HeartbeatInterval | 3000 мс |
| StaleStream | 10000 мс |
| ReconnectWindow | 120000 мс |
| UDP magic | `NORD1` |
| PIN | целое 1000–9999, на проводе строка из 4 цифр |

## UDP (ASCII, одна строка, `\n` не обязателен)

Probe от ученика:

```text
NORD1|probe|v=1
```

Announce от учителя (PIN нет):

```text
NORD1|announce|v=1|name=Класс|ip=192.168.1.5|tcp=47821
```

`name` без `|`. Если класс не найден за ~2 с — повтор probe, плюс опционально ручной IP.

## JSON этапа 1 (обязательны сейчас)

`join_class` ученик → учитель:

```json
{"v":1,"type":"join_class","pin":"4821","display_name":"ПК-ученик","hostname":"DESKTOP-ABC","agent_version":"0.1.0","session_token":null}
```

Повторный Join: тот же объект, `session_token` заполнен.

`join_ok` учитель → ученик:

```json
{"v":1,"type":"join_ok","student_id":"uuid","session_token":"uuid","heartbeat_interval_ms":3000,"reconnect_window_ms":120000}
```

`join_reject`:

```json
{"v":1,"type":"join_reject","reason":"bad_pin","message":"Неверный PIN"}
```

`reason`: `bad_pin` | `version` | `class_closed`.

`heartbeat` (кто чаще шлёт — оба, раз в 3 с; приём любого кадра обновляет `lastRecv`):

```json
{"v":1,"type":"heartbeat","seq":1}
```

`heartbeat_ack` не обязателен, если обе стороны шлют `heartbeat`. Оставить разбор `heartbeat` достаточным. Не слать ack-шторм.

`session_end` учитель → ученик (всем клиентам при завершении класса):

```json
{"v":1,"type":"session_end","reason":"class_ended"}
```

`reason`: `teacher_disconnect` | `class_ended`.

Неизвестный `type` при `v=1`: игнорировать кадр, не рвать сокет (задел на этап 2).

## JSON / бинарь этапов 2–3 (спецификация есть, код — не в первой сессии)

`stream_start` / `stream_stop`: `{"v":1,"type":"stream_start"}` (тело пустое кроме v/type). Только выбранному.

`process_list` ученик → учитель:

```json
{"v":1,"type":"process_list","active_exe":"chrome.exe","items":[{"exe":"chrome.exe","pid":1234,"title":"Документ"}]}
```

Не более ~40 items. Без служебных вроде сплошного `svchost`.

`installed_hints`:

```json
{"v":1,"type":"installed_hints","apps":[{"name":"Google Chrome","exe":"chrome.exe","launch_target":"C:\\...\\chrome.exe"}]}
```

`launch_app`:

```json
{"v":1,"type":"launch_app","exe":"chrome.exe","launch_target":"C:\\...\\chrome.exe"}
```

`set_block_list`:

```json
{"v":1,"type":"set_block_list","exe_names":["discord.exe","steam.exe"]}
```

Пустой массив — запретов нет.

## Запрещено в протоколе v1

События мыши/клавиатуры, аудио, файлы, чат, лицензия, URL облака.
