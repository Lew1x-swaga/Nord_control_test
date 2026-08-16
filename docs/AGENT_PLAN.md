# AGENT_PLAN — этап 1 (для ИИ)

```yaml
plan_id: nord-stage-1
stage: 1
stage_2_forbidden: true
commit: only_if_user_asked
concurrent_implementers: 1
waves: 5
reviewer_per_wave: 1
final_reviewer: 1
os: windows-x64
dotnet: "8.0"
ui: wpf
lang_ui: ru
udp_port: 47820
tcp_port: 47821
test_udp_port: 47830
test_tcp_port: 47831
pin_min: 1000
pin_max: 9999
heartbeat_ms: 3000
stale_ms: 10000
reconnect_ms: 120000
udp_magic: "NORD1"
protocol_v: 1
```

## 0. Кто ты

- **Controller:** читаешь этот файл и `docs/subagents.md`. Диспатчишь **один** `.cursor/agents/nord-stage1-implementer.md` на волну. Не пишешь код сам, если идёт SDD.
- **Implementer:** субагент `nord-stage1-implementer`. Читаешь **только свою волну** + §1–3.
- **Reviewer:** субагент `nord-stage1-reviewer`. После всех волн — `nord-stage1-final-reviewer`.

Если роль не сказали — ты controller, пока человек не сказал «пиши код здесь».

## 1. READ (строго этот порядок)

1. Этот файл (§0–3 и своя волна)
2. `docs/subagents.md`
3. `docs/requirements.md` — только таблица «Этап 1»
4. `docs/protocol.md` — если волна 1–3 или 5
5. `docs/ui.md` — если волна 4
6. `docs/invariants.md` — если спор

Не читать весь чат с человеком как спецификацию. Спека = файлы в `docs/`.

## 2. DO / DON'T

| DO | DON'T |
|---|---|
| LAN TCP/UDP, PIN, heartbeat, WPF список+плашка | DXGI, JPEG, process list, launch, blocklist |
| `lastRecv`; 10с stale; 120с Ended | `Environment.Exit` из сокета |
| Probe 255.255.255.255 **и** 127.0.0.1 | ping 8.8.8.8, HTTP наружу |
| PIN в Join, не в UDP | автозапуск, служба, AppLocker |
| `dotnet test` зелёный в конце волны | коммит без просьбы человека |
| UI на русском из `ui.md` | маскировать процесс |

`DONE_WHEN` волны ложен → волна не закончена. Не начинай следующую.

## 3. Волны (делай сверху вниз)

| Wave | Tasks | DONE_WHEN (exit 0) |
|---|---|---|
| 1 | 1, 2, 3 | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~FrameCodecTests|FullyQualifiedName~UdpPacketsTests|FullyQualifiedName~StudentSessionTests"` |
| 2 | 4 | файл `src/NordControl.Core/ClassHub.cs` есть; `dotnet build src/NordControl.Core` |
| 3 | 5 | файл `src/NordControl.Core/ClassClient.cs` есть; `dotnet build src/NordControl.Core` |
| 4 | 6, 7 | `dotnet build NordControl.sln` |
| 5 | 8, 9 | `dotnet test tests/NordControl.Tests` |

Wave 2 и 3 без полного loopback — это нормально: `DONE_WHEN` = build. Поведение Join проверяет волна 5.

## 4. Карта файлов (создать, пути не менять)

```text
NordControl.sln
src/NordControl.Protocol/NordControl.Protocol.csproj
src/NordControl.Protocol/ProtocolConstants.cs
src/NordControl.Protocol/FrameCodec.cs
src/NordControl.Protocol/UdpPackets.cs
src/NordControl.Protocol/WireMessage.cs
src/NordControl.Core/NordControl.Core.csproj
src/NordControl.Core/IClock.cs
src/NordControl.Core/SystemClock.cs
src/NordControl.Core/StudentSession.cs
src/NordControl.Core/ClassHub.cs
src/NordControl.Core/ClassClient.cs
src/NordControl.Teacher/NordControl.Teacher.csproj
src/NordControl.Teacher/App.xaml
src/NordControl.Teacher/App.xaml.cs
src/NordControl.Teacher/MainWindow.xaml
src/NordControl.Teacher/MainWindow.xaml.cs
src/NordControl.Student/NordControl.Student.csproj
src/NordControl.Student/App.xaml
src/NordControl.Student/App.xaml.cs
src/NordControl.Student/MainWindow.xaml
src/NordControl.Student/MainWindow.xaml.cs
tests/NordControl.Tests/NordControl.Tests.csproj
tests/NordControl.Tests/FrameCodecTests.cs
tests/NordControl.Tests/UdpPacketsTests.cs
tests/NordControl.Tests/StudentSessionTests.cs
tests/NordControl.Tests/LoopbackJoinTests.cs
```

Логи: `%LocalAppData%\NordControl\logs\`. PIN не логировать (`pin_ok` / `pin_bad`).

---

## Wave 1 / Task 1: Solution и константы

- **DEPENDS_ON:** none
- **WAVE:** 1
- **SKILL:** test-driven-development не обязателен (scaffold). Дальше Task 2 — обязателен.
- **DONE_WHEN:** `dotnet build` после констант

**Produces:** `ProtocolConstants` как ниже.

- [ ] **Step 1: проекты**

```bash
dotnet new sln -n NordControl
dotnet new classlib -n NordControl.Protocol -o src/NordControl.Protocol -f net8.0
dotnet new classlib -n NordControl.Core -o src/NordControl.Core -f net8.0
dotnet new wpf -n NordControl.Teacher -o src/NordControl.Teacher -f net8.0-windows
dotnet new wpf -n NordControl.Student -o src/NordControl.Student -f net8.0-windows
dotnet new xunit -n NordControl.Tests -o tests/NordControl.Tests -f net8.0
dotnet sln add src/NordControl.Protocol src/NordControl.Core src/NordControl.Teacher src/NordControl.Student tests/NordControl.Tests
dotnet add src/NordControl.Core reference src/NordControl.Protocol
dotnet add src/NordControl.Teacher reference src/NordControl.Protocol src/NordControl.Core
dotnet add src/NordControl.Student reference src/NordControl.Protocol src/NordControl.Core
dotnet add tests/NordControl.Tests reference src/NordControl.Protocol src/NordControl.Core
```

Teacher/Student: `UseWPF=true`, `net8.0-windows`. Student: ещё `UseWindowsForms=true`.

- [ ] **Step 2: константы** — файл `src/NordControl.Protocol/ProtocolConstants.cs`

```csharp
namespace NordControl.Protocol;

public static class ProtocolConstants
{
    public const int Version = 1;
    public const string AgentVersion = "0.1.0";
    public const int UdpPort = 47820;
    public const int TcpPort = 47821;
    public const int HeartbeatIntervalMs = 3000;
    public const int StaleStreamMs = 10_000;
    public const int ReconnectWindowMs = 120_000;
    public const int MaxFramePayload = 4 * 1024 * 1024;
    public const byte JsonMessageType = 1;
    public const byte JpegMessageType = 2;
    public const string UdpMagic = "NORD1";
    public const int PinMin = 1000;
    public const int PinMax = 9999;
}
```

`JpegMessageType` зарезервирован. **Не реализовывать JPEG.**

- [ ] **Step 3:** `dotnet build` exit 0

---

## Wave 1 / Task 2: TCP-кадры и UDP-строки

- **DEPENDS_ON:** Task 1
- **WAVE:** 1
- **SKILL:** test-driven-development
- **DONE_WHEN:** filter FrameCodecTests|UdpPacketsTests PASS
- **Files:** `FrameCodec.cs`, `UdpPackets.cs`, `WireMessage.cs`, тесты

**Produces:** `FrameCodec.WriteAsync` / `ReadAsync`, `UdpPackets.Probe` / `Announce` / `TryParseAnnounce`, `WireMessage` + JSON

- [ ] **Step 1: RED** — `tests/NordControl.Tests/FrameCodecTests.cs`

```csharp
using NordControl.Protocol;
using Xunit;

public class FrameCodecTests
{
    [Fact]
    public async Task Roundtrip_json_payload()
    {
        using var ms = new MemoryStream();
        var payload = "{\"v\":1,\"type\":\"heartbeat\"}"u8.ToArray();
        await FrameCodec.WriteAsync(ms, ProtocolConstants.JsonMessageType, payload, CancellationToken.None);
        ms.Position = 0;
        var frame = await FrameCodec.ReadAsync(ms, CancellationToken.None);
        Assert.NotNull(frame);
        Assert.Equal(ProtocolConstants.JsonMessageType, frame!.Value.Type);
        Assert.Equal(payload, frame.Value.Payload);
    }

    [Fact]
    public async Task Too_large_payload_throws()
    {
        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await FrameCodec.WriteAsync(ms, 1, new byte[ProtocolConstants.MaxFramePayload + 1], CancellationToken.None);
        });
    }
}
```

- [ ] **Step 2: GREEN** — длина big-endian uint32 = `1 + payload.Length`, затем type, затем payload.

```csharp
public readonly record struct TcpFrame(byte Type, byte[] Payload);

public static class FrameCodec
{
    public static async Task WriteAsync(Stream stream, byte type, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length > ProtocolConstants.MaxFramePayload)
            throw new InvalidDataException("payload too large");
        var len = 1 + payload.Length;
        var header = new byte[5];
        header[0] = (byte)(len >> 24);
        header[1] = (byte)(len >> 16);
        header[2] = (byte)(len >> 8);
        header[3] = (byte)len;
        header[4] = type;
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<TcpFrame?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[5];
        if (!await ReadExactAsync(stream, header, ct)) return null;
        var len = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        if (len < 1 || len > ProtocolConstants.MaxFramePayload + 1)
            throw new InvalidDataException("bad length");
        var type = header[4];
        var payloadLen = len - 1;
        var payload = new byte[payloadLen];
        if (payloadLen > 0 && !await ReadExactAsync(stream, payload, ct)) return null;
        return new TcpFrame(type, payload);
    }

    static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var off = 0;
        while (off < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(off), ct);
            if (n == 0) return false;
            off += n;
        }
        return true;
    }
}
```

- [ ] **Step 3: UDP + JSON**

- Probe = `NORD1|probe|v=1`
- Announce = `NORD1|announce|v=1|name={name}|ip={ip}|tcp={port}`
- `TryParseAnnounce` false если не magic или не announce
- Тест: `Assert.DoesNotContain("pin=", UdpPackets.Announce("Класс", "192.168.0.5", 47821));`
- `WireMessage`: `V, Type, Pin, DisplayName, Hostname, AgentVersion, SessionToken, StudentId, HeartbeatIntervalMs, ReconnectWindowMs, Reason, Message, Seq`
- JSON:

```csharp
public static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
};
```

- [ ] **Step 4:** `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~FrameCodecTests|FullyQualifiedName~UdpPacketsTests"` PASS

---

## Wave 1 / Task 3: StudentSession

- **DEPENDS_ON:** Task 1
- **WAVE:** 1
- **SKILL:** test-driven-development
- **DONE_WHEN:** `dotnet test --filter FullyQualifiedName~StudentSessionTests` PASS
- **Files:** `IClock.cs`, `SystemClock.cs`, `StudentSession.cs`, `StudentSessionTests.cs`

**Produces:**

```text
enum SessionStatus { Idle, Online, Reconnecting, Ended }
OnJoinOk(studentId, token, now)
OnMessageReceived(now)
OnTcpDropped()
OnSessionEnd()
Tick(now)
ResetToIdle()
ShouldHoldPolicies = Online || Reconnecting
StreamPaused = silent >= 10s
ShouldCapture = Online && StreamEnabled && !StreamPaused
StreamEnabled default false
```

- [ ] **Step 1: RED** — `FakeClock` с `UtcNow` и `Advance(TimeSpan)`

1. JoinOk → Online, ShouldHoldPolicies true
2. +10s без сообщений + Tick → StreamPaused true, статус Online
3. суммарно 120s без сообщений + Tick → Ended, ShouldHoldPolicies false
4. JoinOk + OnSessionEnd → Ended сразу
5. JoinOk + OnTcpDropped (свежий lastRecv) → Reconnecting, policies true; OnMessageReceived → Online
6. OnSessionEnd из Reconnecting → Ended
7. Ended + ResetToIdle → Idle, policies false

- [ ] **Step 2: GREEN** — якорь `lastRecv` как в `docs/architecture.md`. `OnTcpDropped` из Ended = nop. Нет `Environment.Exit`.

- [ ] **Step 3:** StudentSessionTests PASS

---

## Wave 2 / Task 4: ClassHub

- **DEPENDS_ON:** Task 2, Task 3
- **WAVE:** 2
- **SKILL:** test-driven-development (минимум: компиляция; Join проверяет волна 5)
- **DONE_WHEN:** `dotnet build src/NordControl.Core` PASS
- **Files:** `src/NordControl.Core/ClassHub.cs` — **без WPF**

**Produces:**

```csharp
ClassHub(int udpPort = ProtocolConstants.UdpPort, int tcpPort = ProtocolConstants.TcpPort)
Task StartClass(string className, string pin, CancellationToken ct)
Task StopClassAsync()
events: StudentJoined, StudentStatusChanged, StudentLeft
```

Поведение (обязательно):

- UDP `0.0.0.0:udpPort`, ответ на probe без PIN. IP в announce = IPv4 локального конца ответа; loopback probe → `127.0.0.1` ок.
- TCP `0.0.0.0:tcpPort`. `join_class`: PIN + `v==1`. Ок → новые Guid + `join_ok`. Плохой PIN → `join_reject` `bad_pin`, закрыть сокет.
- Повторный Join с известным `session_token` → тот же `student_id`.
- Heartbeat каждые 3 с каждому клиенту. Входящий кадр = lastRecv.
- `StopClassAsync` → всем `session_end` `class_ended`, закрыть сокеты.
- Порт занят → исключение с текстом `порт занят — закройте второй Teacher`.
- Accept/read не на UI-потоке.

---

## Wave 3 / Task 5: ClassClient

- **DEPENDS_ON:** Task 4 (контракт Join)
- **WAVE:** 3
- **SKILL:** test-driven-development
- **DONE_WHEN:** `dotnet build src/NordControl.Core` PASS
- **Files:** `src/NordControl.Core/ClassClient.cs` — **без WPF**

**Produces:**

```csharp
ClassClient(string pin, int udpPort = ProtocolConstants.UdpPort, int tcpPort = ProtocolConstants.TcpPort, string? manualTeacherIp = null)
Task RunAsync(CancellationToken ct)
void RequestStop()
events: StatusChanged(StudentSession), Error(string)
```

Поведение:

- `EnableBroadcast = true`. Probe на `255.255.255.255:udpPort` и `127.0.0.1:udpPort`.
- TCP на `ip:tcp` из announce. Join с pin из конструктора.
- Сохранить `session_token`. Кадр JSON → `OnMessageReceived`. `session_end` → `OnSessionEnd`, потом `ResetToIdle`, снова discovery. Не Exit.
- TCP drop → `OnTcpDropped`, reconnect с token пока окно 120 с. IP мёртв → снова probe.
- `Tick` раз в 1 с. Нет ping интернета.
- Если 2 с нет announce и задан `manualTeacherIp` — TCP туда.

---

## Wave 4 / Task 6: UI учителя

- **DEPENDS_ON:** Task 4
- **WAVE:** 4
- **SKILL:** none (XAML). Не ломать Core tests.
- **DONE_WHEN:** вместе с Task 7 — `dotnet build NordControl.sln`
- **Files:** Teacher `MainWindow.xaml`, `MainWindow.xaml.cs`
- **Spec:** `docs/ui.md`

Обязательно: имя класса по умолчанию `Класс`; крупный PIN; «Новый PIN» только до старта (`Random.Shared.Next(1000, 10000)`); «Начать класс» / «Завершить класс»; список имя+hostname+`онлайн|переподключение|нет`; справа `Экран — этап 2`; Closing → `StopClassAsync`. Нет проверки интернета.

---

## Wave 4 / Task 7: UI ученика

- **DEPENDS_ON:** Task 5
- **WAVE:** 4
- **DONE_WHEN:** `dotnet build NordControl.sln`
- **Files:** Student MainWindow + NotifyIcon
- **Spec:** `docs/ui.md`

Обязательно: поле PIN, опционально IP, «Подключиться». Тексты плашки из ui.md. Высота окна ~80–120, `Topmost` если не Idle. `NotifyIcon`, «Выйти…». После Join Closing cancel + PIN. До Join закрывать свободно. Неверный PIN — текст, процесс жив.

---

## Wave 5 / Task 8: LoopbackJoinTests

- **DEPENDS_ON:** Task 4, Task 5
- **WAVE:** 5
- **SKILL:** test-driven-development
- **DONE_WHEN:** входит в полный `dotnet test`
- **Files:** `tests/NordControl.Tests/LoopbackJoinTests.cs`

Порты **47830 / 47831**. PIN `1234` → Online. PIN `9999` → reject, не в списке. `StopClassAsync` → клиент Ended.

- [ ] RED затем GREEN. `dotnet test` PASS

---

## Wave 5 / Task 9: Приёмка человеком

- **DEPENDS_ON:** Task 6, 7, 8
- **WAVE:** 5
- **SKILL:** verification-before-completion
- **DONE_WHEN:** `dotnet test tests/NordControl.Tests` PASS + controller сообщает человеку результаты сценариев

- [ ] `dotnet test` все зелёные
- [ ] `dotnet run --project src/NordControl.Teacher` и Student на одной машине: requirements сценарии 1, 2, 3, 4, 6
- [ ] Если есть вторая машина:

```bash
dotnet publish src/NordControl.Teacher -c Release -r win-x64 --self-contained true -o dist/teacher
dotnet publish src/NordControl.Student -c Release -r win-x64 --self-contained true -o dist/student
```

Брандмауэр учителя (частные сети). Если надо:

```powershell
netsh advfirewall firewall add rule name="NordControl Teacher TCP" dir=in action=allow protocol=TCP localport=47821 profile=private
netsh advfirewall firewall add rule name="NordControl Teacher UDP" dir=in action=allow protocol=UDP localport=47820 profile=private
```

Сценарии 5 и 7.

- [ ] В чат человеку: что PASS/FAIL. **Не начинать этап 2.**

Heartbeat без второго ноута: airplane на секунды → оба exe живы; выключить только интернет → класс жив; kill Teacher → Student.exe сам не закрывается.

## Конец плана

Этап 2 (DXGI/JPEG) — **новый** план после приёмки человеком. В этой сессии не писать.
