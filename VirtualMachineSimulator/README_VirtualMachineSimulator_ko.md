# AFP 가상 머신 테스트 프로그램

이 프로젝트는 AFP 프로그램의 `Communication` 화면과 직접 붙여서 테스트하는 **TCP/IP 장비 시뮬레이터**다.

핵심 목적:
- 실장비 대신 TCP 서버로 응답
- `READY / STATUS / START / STOP / RESET` 테스트
- 내부 구조화 명령(`LOADER_JOB`, `CUTTING_JOB`, `MARKING_JOB`, `ROBOT_TRANSFER`, `BENDING_JOB`) 테스트
- `ACK` 와 `STATUS:WORKING / STATUS:FINISH / ALARM` 전이를 확인

---

## 1. 프로젝트 구성

- `VirtualMachineSimulator.csproj`
- `appsettings.json` : 바인드 주소, 포트, 머신 목록
- `Program.cs` : 실행 진입점 + 콘솔 제어
- `Protocol/ProtocolParsing.cs` : AFP 명령 파싱
- `Simulator/MachineSimulator.cs` : 머신별 상태 전이
- `Simulator/TcpMachineServer.cs` : 머신별 TCP 서버

---

## 2. 기본 포트

기본 포트는 AFP 화면의 머신 포트와 맞추기 쉽게 아래처럼 넣어두었다.

- LOADER   : 5000
- CUTTING  : 5001
- LASER    : 5002
- BENDING  : 5003
- ROBOT    : 5004
- BENDING2 : 5005

> 현재 AFP 화면에 설정된 포트가 다르면 `appsettings.json` 또는 AFP 설정값을 맞춰야 한다.

---

## 3. 실행 방법

로컬 PC에 .NET SDK 8.0 이상 설치 후:

```bash
cd VirtualMachineSimulator

dotnet run
```

실행되면 각 포트에 TCP 서버가 뜬다.

---

## 4. AFP 쪽 설정 방법

### 방법 A. 같은 PC에서 테스트
AFP 설정 화면에서 각 장비 IP를 `127.0.0.1` 로 바꾸고 포트를 아래처럼 맞춘다.

- Loader   -> 127.0.0.1:5000
- Cutting  -> 127.0.0.1:5001
- Laser    -> 127.0.0.1:5002
- Bending  -> 127.0.0.1:5003
- Robot    -> 127.0.0.1:5004
- Bending2 -> 127.0.0.1:5005

### 방법 B. 다른 PC에서 테스트
시뮬레이터 PC의 IP를 넣는다. 예:

- 192.168.0.50:5000 ~ 5005

이 경우 방화벽에서 해당 포트를 열어야 한다.

---

## 5. 지금 지원하는 AFP 명령

### Legacy 명령
- `LOADER:READY?`
- `LOADER:STATUS?`
- `LOADER:START`
- `LOADER:STOP`
- `LOADER:RESET`
- 나머지 CUT/LASER/BEND/ROBOT/BEND2 도 같은 패턴

### Structured frame
예시:

```text
CUT:CMD=CUTTING_JOB;CID=abc123;TS=2026-04-10T12:00:00Z;PAYLOAD=...
```

지원 명령 코드:
- `LOADER_JOB`
- `LOAD_REQUEST`
- `PREFETCH_LOAD`
- `BUFFER_PREPARE`
- `JOB_LOAD`
- `JOB_EXEC`
- `CUTTING_JOB`
- `MARKING_JOB`
- `BENDING_JOB`
- `ROBOT_TRANSFER`
- `ABORT`
- `CUSTOM`

---

## 6. 응답 규칙

### READY?
- 준비 가능: `OK`
- 준비 불가: `NOT_READY`

### STATUS?
상태에 따라 아래 중 하나를 반환한다.

- `STATUS:READY;READY=1`
- `STATUS:WORKING;READY=0;CID=...;CMD=...`
- `STATUS:FINISH;READY=1;UNLOAD_COMPLETE;CID=...;CMD=...`
- `STATUS:ALARM;READY=0;ERROR_CODE=...`

### 명령 수락
- `OK`
- `OK;CID=<id>`

### 명령 거부
- `ERROR;CODE=...`
- `ERROR;CID=<id>;CODE=...`

---

## 7. 콘솔 명령

시뮬레이터 실행 후 콘솔에서 아래를 입력할 수 있다.

### 현재 상태 확인
```text
status
```

### 알람 발생
```text
alarm CUTTING E201
```

### 알람 해제
```text
reset CUTTING
```

### Ready 강제 on/off
```text
ready ROBOT off
ready ROBOT on
```

### 강제 완료
```text
complete BENDING
```

### 종료
```text
quit
```

---

## 8. 테스트 방법

### 1단계. 시뮬레이터 실행
```bash
dotnet run
```

### 2단계. AFP에서 Communication 화면 진입
- 각 장비 IP/Port를 시뮬레이터 값으로 설정
- 연결 버튼 실행

### 3단계. 개별 명령 테스트
각 머신 카드에서 아래를 눌러본다.

- `STATUS`
- `START`
- `STOP`
- `RESET`

확인 포인트:
- 연결 성공 여부
- `STATUS:READY` 가 보이는지
- `START` 후 `OK` 가 오는지
- 이후 `STATUS` 때 `WORKING -> FINISH -> READY` 흐름이 보이는지

### 4단계. 구조화 명령 테스트
AFP 쪽에서 `LOADER_JOB`, `CUTTING_JOB`, `MARKING_JOB`, `ROBOT_TRANSFER`, `BENDING_JOB` 을 보내고 시뮬레이터 콘솔에서 RX/TX 로그를 확인한다.

### 5단계. 예외 테스트
콘솔에서 강제로 상태를 바꿔본다.

#### Ready 차단
```text
ready CUTTING off
```
그 후 AFP에서 `START` 또는 job 명령 전송

#### 알람 테스트
```text
alarm ROBOT E901
```
그 후 AFP에서 `STATUS` / `RESET` 동작 확인

#### 강제 완료
```text
complete BENDING
```
그 후 AFP에서 `STATUS` 조회 시 `FINISH` 확인

---

## 9. 현재 한계

이 버전은 **1차 통신 검증용**이다.

아직 아래는 없다.
- 비동기 푸시 이벤트
- 실제 장비 위치값 스트리밍
- worker/operation 화면의 완전한 이벤트 기반 배관 이동
- 복잡한 인터락 엔진
- BENDING/BENDING2 자동 부하분산

즉, 이 버전은 **Communication 테스트용 1차 시뮬레이터**로 보는 것이 맞다.

---

## 10. 다음 단계 권장

1. AFP Communication 화면과 연결 검증
2. Structured payload 수신 검증
3. 에러/알람/Ready 차단 테스트
4. 그 다음에 worker/operation 화면을 **장비 이벤트 기반**으로 연결

