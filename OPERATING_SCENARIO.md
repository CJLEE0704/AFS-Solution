# Inline 운영 시나리오 (Loader 지속 공급)

라인 순서:

`Loader M/C → Cutting M/C → Marking M/C → Moving Robot → Bending M/C #1 → Bending M/C #2`

## 1) 기본 운전 시나리오 (Inline)

1. Loader가 배관 A를 Cutting으로 이송한다.
2. Cutting이 A를 가공하는 동안, Loader는 배관 B를 잡아 **사전 준비** 상태로 대기한다.
3. A가 Marking으로 넘어가면 Cutting에는 B가 즉시 투입된다.
4. 동일하게 공정이 겹쳐서 진행되어 각 공정에는 서로 다른 배관이 동시에 존재한다.
5. Moving Robot은 Bending #1/#2 중 **Ready 상태를 우선** 선택하여 이송한다.

## 2) Bending 2대 자동분배 시나리오

- Ready 장비가 1대면 해당 장비로 즉시 이송.
- Ready 장비가 2대면 더 빠르게 비는 장비를 우선 사용.
- 두 장비 모두 Busy면 Robot은 대기 후 재판단.

## 3) 병목/정체 시나리오

- 하류 공정(예: Bending)이 지연되면 상류 공정은 버퍼 한계 내에서만 선행 준비한다.
- 버퍼 한계 초과 시 Loader는 추가 투입 없이 대기한다.
- 하류 공정이 비면 즉시 재투입하여 라인 재가동률을 높인다.

## 4) 정지/재시작 시나리오

- STOP 시:
  - 공정 타이머 및 Loader 사전준비 타이머 중단
  - 사전 준비 버퍼 초기화
- START 시:
  - 첫 배관 투입과 동시에 Loader 사전준비 재개
  - 버퍼를 채우며 연속 공급 상태로 복귀

