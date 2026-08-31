# FBXtoRVT 애드인 — 전체 공통 규칙

이 문서는 **모든 기능에 공통으로 적용되는 규칙**을 모아둔 곳입니다.
새 기능을 만들거나 기존 기능을 고칠 때, 먼저 이 문서를 확인하세요.

대상: Revit 2025 Add-in (`FBXtoRVT`)

---

## 규칙 1. 복합 패밀리의 Sub-Component 는 기능 대상에서 제외

### 내용

객체를 조회하거나 선택하는 모든 기능에서, **복합 패밀리(Nested Family) 안에 들어있는
Sub-Component 는 대상으로 삼지 않습니다.**

즉, 아래와 같은 일이 일어나면 안 됩니다.

- 패밀리 내부의 패밀리가 검색 결과에 잡히는 것
- 패밀리 내부의 패밀리가 선택되는 것
- 패밀리 내부의 패밀리가 이동·회전되는 것
- 패밀리 내부의 패밀리의 커넥터가 연결 대상이 되는 것

기능이 다루는 단위는 **항상 최상위 패밀리 인스턴스 하나**입니다.

### 이유

`FilteredElementCollector` 로 `FamilyInstance` 를 모으면, 복합 패밀리 안에 들어있는
Sub-Component 도 **별개의 FamilyInstance 로 같이 수집됩니다.**

이 상태로 그냥 처리하면 다음 문제가 생깁니다.

- 부모 패밀리와 Sub-Component 가 **중복으로 카운트**됩니다.
- Sub-Component 만 따로 이동·회전되어 **부모 패밀리와 형상이 어긋납니다.**
  (Sub-Component 는 부모의 좌표계에 묶여 있으므로, 단독으로 옮기면 모델이 깨집니다.)
- 사용자가 화면에서 클릭하는 단위(= 최상위 패밀리)와 코드가 다루는 단위가 달라져서,
  결과 요약의 개수와 실제로 보이는 객체 수가 맞지 않습니다.

### 적용 방법

**새 기능은 `Core/ElementUtils.cs` 의 `CollectFamilyInstances` 를 쓰면 됩니다.**
이 함수가 Sub-Component 를 이미 걸러줍니다.

```csharp
foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, "타공 SLEEVE"))
{
    // Sub-Component 는 여기까지 오지 않는다
}
```

직접 수집해야 한다면, `FamilyInstance.SuperComponent` 가 `null` 이 아닌 객체를 걸러냅니다.

```csharp
/// <summary>
/// 복합 패밀리 안에 들어있는 Sub-Component 인지 검사.
/// SuperComponent(부모)가 있으면 Sub-Component 이므로 기능 대상에서 제외한다.
/// </summary>
private static bool IsSubComponent(Element e)
{
    var fi = e as FamilyInstance;
    return fi != null && fi.SuperComponent != null;
}
```

수집 루프에서는 이름 검사보다 **먼저** 걸러내는 것을 권장합니다.

```csharp
foreach (Element e in collector)
{
    if (IsSubComponent(e)) continue;   // 복합 패밀리 내부 객체는 건너뜀
    if (!NameContains(e, keyword)) continue;
    // ...이후 처리
}
```

### 주의할 점

- **커넥터도 같은 원칙을 따릅니다.** 최상위 패밀리의 `ConnectorManager` 에서 얻은
  커넥터만 사용합니다. Sub-Component 의 커넥터는 후보에서 제외합니다.
- **이동·회전 대상 Id 는 항상 최상위 패밀리의 Id** 입니다.
  `ElementTransformUtils.MoveElement` / `RotateElement` 에 Sub-Component 의 Id 를
  넘기면 안 됩니다.
- **선택(`Selection.SetElementIds`) 결과에도 Sub-Component 를 넣지 않습니다.**
- 바운딩 박스 계산에는 최상위 패밀리의 박스를 씁니다.
  최상위 패밀리의 박스는 이미 Sub-Component 형상까지 포함합니다.
- 반대로 **Sub-Component 를 일부러 다뤄야 하는 경우**(예: 부모를 통해 내부 부품 정보를
  읽어야 할 때)에는 `FamilyInstance.GetSubComponentIds()` 로 부모에서 명시적으로
  내려가서 접근합니다. 전체 수집으로 잡아 쓰지 않습니다.

### 현재 코드의 적용 상태

모든 기능에 이 규칙이 적용되어 있습니다.

| 파일 | 적용 방식 |
| --- | --- |
| `Core/SleeveAdjustHelper.cs` | `ElementUtils.CollectFamilyInstances` 사용 |
| `Core/ScrubberFlangeHelper.cs` | `ElementUtils.CollectFamilyInstances` 사용 |
| `Core/ElbowConnectHelper.cs` | `ElementUtils.CollectFamilyInstances` 사용 |
| `Core/HopperFlangeHelper.cs` | `ElementUtils.CollectFamilyInstances` 사용 |
| `Core/OverlapSelectHelper.cs` | `CollectFamilyInstancesInView` 에서 `IsSubComponent` 로 제외 |

`Core/OverlapSelectHelper.cs` 는 패밀리명뿐 아니라 타입명으로도 찾아야 해서
자체 수집 코드를 갖고 있고, 거기에 `IsSubComponent` 를 끼워 넣었습니다.

`Core/RightAngleConnectHelper.cs` (직각 배관 연결기)와 `Core/DiagonalPipeHelper.cs`
(대각 배관 생성기)는 사용자가 직접 클릭한 배관 두 개만 다루고, 선택 필터로 배관(`Pipe`)
카테고리만 허용하므로 수집 단계가 없습니다.
(`Pipe` 는 `FamilyInstance` 가 아니라 Sub-Component 문제가 생기지 않습니다.)

`Core/RightAnglePipeHelper.cs` / `Commands/RightAnglePipeCommand.cs` (직각 배관 생성기)는
**현재 사용하지 않아 파일 전체를 주석 처리**했고, 리본 버튼(아이콘)도 제거했습니다.
되살리려면 두 파일의 `/* ... */` 주석을 풀고 `App.cs` 의 `공용` 패널에 `AddButton` 을
다시 추가하면 됩니다. 기하 계산도 주석 안에 그대로 들어 있습니다.

### 이름이 비슷한 두 기능을 헷갈리지 마세요

| 기능 | 대상 | 만드는 것 | 연결까지? |
| --- | --- | --- | --- |
| 대각 배관 생성기 | **평행한** 두 배관 | 45도 대각 배관 | 아니오(유저가 Trim) |
| 직각 배관 연결기 | **평행한** 두 배관 | 90도 직각 배관 | 예(Trim + 엘보 2개) |
| ~~직각 배관 생성기~~ (주석 처리됨) | **직각인** 두 배관 | 사잇배관 | 아니오(유저가 Trim) |

평행한 두 배관을 잇는 기하 계산(평행 판정 / 공통수선 위치)은
`Core/PipeGeometryUtils.cs` 에 모아 두었습니다.

---

## 규칙 2. 리본 패널 구성

### 내용

"FBXtoRVT" 탭의 패널은 **왼쪽부터 아래 순서**로 둡니다.

| 패널 | 성격 | 현재 버튼 |
| --- | --- | --- |
| `1.포어라인` | 포어라인 작업 전용 | 타공 슬리브 조정 / 대각 배관 생성기 |
| `2.SCR` | SCR 작업 전용 | SCR장비&플랜지/NUT / 겹침 객체 선택 |
| `공용(연결)` | 부품을 커넥터에 붙이는 기능 | ELBOW&배관/플랜지 / HOPPER&플랜지 / 장비&플랜지/NUT |
| `공용(배관)` | 배관을 새로 만드는 기능 | 직각 배관 연결기 / Flex Pipe 생성기 |
| `공용(뷰/가시성)` | 화면에 무엇을 보여줄지 다루는 기능 | LINK ON/OFF / 선택 Section Box |
| `응원` | 응원 버튼 | (이름별 8개) |

**공용 패널 세 개는 `연결` → `배관` → `뷰/가시성` 순서로 둡니다.**

### 이유

버튼이 계속 늘어나므로, 먼저 **어느 공정에서 쓰는지**로 묶고
(`1.포어라인` / `2.SCR`), 공정과 무관한 기능은 **무엇을 하는 기능인지**로 묶습니다.

공용 기능이 6개까지 늘어나면서 한 패널 안에서 원하는 버튼을 찾기 어려워졌기 때문에,
`공용` 하나를 `연결` / `배관` / `뷰/가시성` 셋으로 나눴습니다.
성격이 다른 `선택 Section Box` 를 담아두던 `기타` 패널은 `공용(뷰/가시성)` 에 흡수되어
없어졌습니다. `LINK ON/OFF` 와 `선택 Section Box` 는 둘 다 "화면에 무엇을 보여줄지"
다루는 기능이라 같은 자리에 두는 편이 찾기 쉽습니다.

### 적용 방법

`App.cs` 의 `OnStartup` 에서 `CreateXxxPanel` 을 호출하는 **순서**가 곧 패널 순서이고,
각 패널 함수 안에서 `AddButton` 을 호출하는 **순서**가 곧 버튼 순서입니다.

새 기능은 성격에 맞는 패널 함수에 `AddButton` 한 줄을 추가하면 됩니다.

---

## 새 기능을 만들 때 체크리스트

- [ ] 수집 단계에서 Sub-Component 를 걸렀는가? (규칙 1)
- [ ] 이동·회전·선택 대상 Id 가 최상위 패밀리 Id 인가? (규칙 1)
- [ ] 성격에 맞는 패널에 버튼을 추가했는가? 공용 패널 순서(`연결` → `배관` → `뷰/가시성`)는 그대로인가? (규칙 2)
- [ ] 로직은 `Core/`, Revit 진입점(문서·뷰 검사, 입력창, Transaction, 결과 요약)은
      `Commands/` 로 나눴는가?
- [ ] `Transaction` 은 `Commands/` 쪽에서만 열었는가?
- [ ] 코드에 한국어 주석을 달았는가?
