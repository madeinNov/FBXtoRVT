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
| `Core/FlangeNutAttachHelper.cs` | `ElementUtils.CollectFamilyInstances` 사용 (부품 수집) |
| `Core/ScrubberFlangeHelper.cs` | `ElementUtils.CollectFamilyInstances` 사용 (장비 수집) |
| `Core/EquipmentFlangeNutHelper.cs` | `ElementUtils.CollectFamilyInstancesByCategory` 사용 (장비 수집) |
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

## 규칙 3. 반복문 안에서 객체를 다시 수집하지 않는다

### 내용

`FilteredElementCollector` 는 **실행 시작 때 한 번만** 돌린다.
"장비마다 / 슬리브마다" 같은 반복문 안에서 다시 돌리면 안 된다.

### 이유

`FilteredElementCollector` 로 뷰 안의 `FamilyInstance` 를 전부 모으고 이름을 비교하는 일은
모델이 커질수록 급격히 느려진다. 이것을 대상 100개짜리 반복문 안에서 돌리면
같은 작업을 100번 하게 되어, 실행 시간이 눈에 띄게 길어진다.

### 적용 방법

후보 목록을 반복문 <b>밖에서</b> 한 번 모아 두고, 반복문 안에서는 그 목록만 훑는다.
처리 도중 값이 바뀌는 것과 안 바뀌는 것을 나눠서 다루면 된다.

| 성질 | 어떻게 다루나 | 예 |
| --- | --- | --- |
| 안 바뀜 | 미리 계산해서 보관 | 배관 끝점, 삭제만 되는 플랜지의 중심점 |
| 바뀜 | 보관한 Id 로 그때그때 다시 조회 | 커넥터 열림/닫힘, 이동한 부품의 위치 |

```csharp
// 후보는 여기서 한 번만 모은다
List<PartRef> parts = CollectParts(doc, view, FamilyKeywords.Flange);

foreach (ElementId equipId in equipmentIds)
{
    foreach (PartRef part in parts)   // 다시 수집하지 않는다
    {
        ...
    }
}
```

객체가 <b>움직이면</b> 보관해 둔 좌표도 그 자리에서 갱신해 준다.
(`Core/FlangeNutAttachHelper.cs` 의 `PartRef.Center` 가 그렇게 되어 있다)

### 주의할 점

- 삭제될 수 있는 객체는 `HashSet<ElementId>` 로 "이미 지운 것" 을 기록해 건너뛴다.
- 커넥터가 열려 있는지(`IsConnected`)는 처리 도중 계속 바뀌므로 **절대 보관하지 않는다.**
  커넥터는 `Core/ConnRef.cs` 로 "객체 Id + 커넥터 Id" 만 기억했다가 쓰기 직전에 다시 조회한다.

---

## 규칙 4. 로깅 정책

### 내용

로그는 `Core/LogUtils.cs` 의 세 가지만 쓴다.

| 함수 | 쓸 곳 | 언제 남나 |
| --- | --- | --- |
| `LogUtils.Log` | 기능 시작 / 종료 요약처럼 **실행당 몇 줄**뿐인 내용 | 항상 |
| `LogUtils.LogDetail` | 객체 하나하나를 따라가는 **상세 진단** | 상세 로그를 켰을 때만 |
| `LogUtils.LogError` | 예외 / 실패 | 항상 |

### 이유

예전에는 모든 로그가 항상 파일에 쓰였다. 객체마다 한 줄씩 남기는 기능에서는
디스크 쓰기와 문자열 만들기 때문에 실행이 느려지고, 로그 파일도 금방 읽기 어려워진다.
그렇다고 상세 로그를 지워 버리면 문제가 생겼을 때 원인을 찾을 수 없다.
그래서 **평소에는 요약만, 필요할 때만 상세하게** 남기도록 나눴다.

### 적용 방법

반복문 안에서 `LogDetail` 을 부를 때는 **반드시** 호출 자체를 감싼다.
그렇지 않으면 로그를 꺼 두어도 `$"..."` 문자열을 만드는 비용이 그대로 든다.

```csharp
if (LogUtils.DetailEnabled)
    LogUtils.LogDetail($"후보 FLANGE(Id={id}, Family={ElementUtils.GetFamilyName(flange)}) ...");
```

일괄 처리 기능은 시작과 종료에 요약 한 줄씩을 남긴다.

```csharp
LogUtils.Log($"===== 타공 슬리브 조정 실행 시작. 슬리브 {n}개 =====");
...
LogUtils.Log($"===== 타공 슬리브 조정 실행 종료. 상부연결={...} 실패={...} =====");
```

`catch` 로 실패를 삼킬 때는 반드시 `LogUtils.LogError` 를 남긴다.
(결과 요약의 "실패 N건" 이 왜 생겼는지 나중에 확인할 수 있어야 한다)

### 상세 로그 켜는 법

`%AppData%\FBXtoRVT\debug.on` 이라는 **빈 파일**을 만들고 Revit 을 다시 켠다.
파일을 지우면 다시 꺼진다. 다시 빌드할 필요는 없다.
로그 파일은 `%AppData%\FBXtoRVT\FBXtoRVTLogs\log_날짜.txt` 이다.

---

## 규칙 5. 뷰 전체를 훑는 명령은 `ViewCommandBase` 를 쓴다

### 내용

"버튼을 누르면 현재 뷰 전체를 훑어서 한 번에 처리하는" 명령은
`Commands/ViewCommandBase.cs` 를 상속해서 만든다.

### 이유

이런 명령은 (1) 문서/뷰 확인 → (2) Transaction → (3) 결과 대화상자 → (4) 예외 처리 가
매번 똑같다. 명령마다 복사해 두면 예외 처리 하나를 고칠 때 모든 파일을 다 손봐야 한다.

### 적용 방법

`FeatureTitle` 과 `RunInTransaction` 만 채우면 된다.
Transaction 은 부모가 열고 닫으므로, 자식은 Core 로직을 부르고 요약 문구만 돌려준다.

```csharp
[Transaction(TransactionMode.Manual)]
public class SleeveAdjustCommand : ViewCommandBase
{
    protected override string FeatureTitle { get { return "타공 슬리브 조정"; } }

    protected override string RunInTransaction(Document doc, View view)
    {
        SleeveAdjustHelper.RunResult r = SleeveAdjustHelper.Run(doc, view);
        return $"타공 SLEEVE: {r.SleeveCount}개 ...";   // null 을 돌려주면 창을 띄우지 않는다
    }
}
```

- 실행 취소 목록에 다른 이름을 쓰고 싶으면 `TransactionName` 만 추가로 덮어쓴다.
- `[Transaction(TransactionMode.Manual)]` 특성은 **자식 클래스에도 그대로 붙인다.**
  (Revit 이 명령 클래스에서 직접 읽기 때문)
- 사용자가 객체를 먼저 클릭해야 하는 명령(대각 배관 생성기, 직각 배관 연결기,
  Flex Pipe 생성기, 선택 Section Box, 겹침 객체 선택)은 흐름이 달라서 이 뼈대를 쓰지 않는다.

---

## 규칙 6. 플랜지 상/하 판단은 `FlangeSideTable` 에만 적는다

### 내용

플랜지를 무언가에 붙일 때 하는 일은 **어느 기능에서나 똑같다.**

> **지금 붙이는 커넥터 쪽 플랜지를 해제한다.**

그런데 "지금 쓰는 커넥터가 상이냐 하냐" 는 패밀리마다 다르다.
그 **패밀리별 정보만** `Core/FlangeSideTable.cs` 의 표에 적고,
각 기능은 "Primary 커넥터를 쓰는가, 아닌가" 만 넘긴다.

### 이유

예전에는 상/하 판단을 기능마다 따로 적어 두어서 규칙이 갈라져 있었다.
같은 DC FLANGE 인데 장비&플랜지 기능은 "하" 를, HOPPER&플랜지 기능은 "상" 을 해제했다.
표 하나로 모으면 이런 어긋남이 생길 수 없고, 새 패밀리가 생겨도 한 줄만 추가하면 된다.

### 현재 표

| 이름 키워드 | Primary 커넥터가 있는 쪽 |
| --- | --- |
| `BLIND` | 없음 (한쪽뿐이라 해제할 것이 없음) |
| `BELLOWS` | 상 |
| `DC` | 상 |
| `NW` | 하 |
| 그 밖의 이름 | **없음 (아무것도 해제하지 않는다)** |

- **위에서부터 검사해서 먼저 걸리는 줄을 쓴다.** 이름에 두 키워드가 다 들어있을 때
  어느 쪽이 이기는지가 표의 순서로 정해진다. (예: `BLIND DC FLANGE` → BLIND 가 이겨서 "없음")
- **패밀리명을 먼저 보고, 패밀리명에서 못 찾았을 때만 타입명을 본다.**
- 모르는 패밀리에 짐작으로 상/하를 해제하면 엉뚱한 형상이 사라질 수 있으므로,
  표에 없으면 아무것도 하지 않는다.

### 적용 방법

```csharp
// 각 기능은 이 한 줄만 부른다. 해제할 것이 없으면 null 이 돌아온다.
string paramToUncheck = FlangeSideTable.GetParamToUncheck(flange, usingPrimary);

if (paramToUncheck != null && ElementUtils.UncheckYesNoParam(flange, paramToUncheck))
{
    result.ParamUncheckedCount++;
    doc.Regenerate();   // 형상이 바뀌므로 이후 커넥터는 다시 조회한다
}
```

새 플랜지 패밀리가 생기면 `FlangeSideTable.Table` 에 한 줄만 추가한다.
기능 쪽 코드는 손대지 않는다.

---

## 공통 코드가 어디 있는지

같은 계산을 파일마다 다시 만들지 않도록, 아래 파일들을 먼저 확인한다.

| 파일 | 들어있는 것 |
| --- | --- |
| `Core/ElementUtils.cs` | 객체 수집, 월드 바운딩 박스(`WorldBox`), 커넥터 조회, 파라미터 복사 |
| `Core/ConnRef.cs` | "객체 Id + 커넥터 Id" 로 커넥터를 기억해 두는 참조 |
| `Core/Keywords.cs` | 패밀리명 키워드(`FamilyKeywords`) / 파라미터 이름(`ParamNames`) 상수 |
| `Core/ConnectorHelper.cs` | 한쪽 객체를 움직여 상대 커넥터에 맞춘 뒤 연결 |
| `Core/PipeGeometryUtils.cs` | 배관 중심선, 평행 판정, 공통수선 계산 |
| `Core/FlangeNutAttachHelper.cs` | 장비 안의 FLANGE / NUT 을 장비 커넥터에 붙이는 규칙 |
| `Core/FlangeSideTable.cs` | 패밀리별 Primary 커넥터가 상인지 하인지 (규칙 6) |
| `Core/LogUtils.cs` | 로그 (규칙 4) |
| `Commands/ViewCommandBase.cs` | 뷰 전체를 훑는 명령의 공통 뼈대 (규칙 5) |

키워드 문자열("FLANGE", "ELBOW", "FLANGE 상" ...)은 `Core/Keywords.cs` 에만 적는다.
파일마다 따로 적어 두면 한쪽만 고쳤을 때 기능별로 대상이 달라진다.

---

## 새 기능을 만들 때 체크리스트

- [ ] 수집 단계에서 Sub-Component 를 걸렀는가? (규칙 1)
- [ ] 이동·회전·선택 대상 Id 가 최상위 패밀리 Id 인가? (규칙 1)
- [ ] 성격에 맞는 패널에 버튼을 추가했는가? 공용 패널 순서(`연결` → `배관` → `뷰/가시성`)는 그대로인가? (규칙 2)
- [ ] 버튼 툴팁에 적은 숫자(mm 등)가 코드의 상수와 실제로 같은가? (규칙 2)
- [ ] `FilteredElementCollector` 를 반복문 밖에서 한 번만 돌렸는가? (규칙 3)
- [ ] 반복문 안의 `LogDetail` 을 `if (LogUtils.DetailEnabled)` 로 감쌌는가? (규칙 4)
- [ ] `catch` 로 실패를 삼킬 때 `LogUtils.LogError` 를 남겼는가? (규칙 4)
- [ ] 뷰 전체를 훑는 명령이면 `ViewCommandBase` 를 상속했는가? (규칙 5)
- [ ] 플랜지 상/하 판단을 기능 안에 적지 않고 `FlangeSideTable` 에 맡겼는가? (규칙 6)
- [ ] 키워드 문자열을 `Core/Keywords.cs` 에 넣었는가? (파일마다 따로 적지 않았는가)
- [ ] 로직은 `Core/`, Revit 진입점(문서·뷰 검사, 입력창, Transaction, 결과 요약)은
      `Commands/` 로 나눴는가?
- [ ] `Transaction` 은 `Commands/` 쪽에서만 열었는가?
- [ ] 코드에 한국어 주석을 달았는가?
