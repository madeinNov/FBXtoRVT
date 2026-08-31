using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "LINK ON/OFF" 기능의 핵심 로직.
    ///
    /// 대상은 <b>좌표조정 모델(Coordination Model) 링크</b> 하나뿐이다.
    /// Navisworks 파일(.nwc / .nwd)을 Insert &gt; Coordination Model 로 붙인 그 링크이며,
    /// Revit 카테고리로는 "Coordination Model"(OST_Coordination_Model) 이다.
    /// <b>RVT 링크는 건드리지 않는다.</b>
    ///
    /// [처리 순서 — 되는 방법을 차례로 시도한다]
    ///  1) 카테고리 가시성 토글.
    ///     카테고리 Id 는 <c>Category.GetCategory</c> 로 찾지 않고
    ///     <c>new ElementId(BuiltInCategory.OST_Coordination_Model)</c> 로 바로 만든다.
    ///     좌표조정 모델은 문서의 카테고리 목록에 안 잡히는 경우가 있어서,
    ///     <c>Category.GetCategory</c> 가 null 을 돌려주면 이 방법을 아예 못 써 보게 되기 때문이다.
    ///  2) 좌표조정 모델 <b>객체</b>를 직접 숨기기 / 숨기기 해제.
    ///  3) 둘 다 안 되면, <b>어디서 막혔는지 진단 내용을 붙여서</b> 안내한다.
    ///     (뷰 이름 / 뷰 종류 / 뷰 템플릿 / 찾은 개수 / 각 단계의 실패 사유)
    ///
    /// [뷰 템플릿이 잠근 경우]
    /// 뷰에 템플릿이 걸려 있고 그 템플릿이 좌표조정 모델 V/G 를 제어하면,
    /// 뷰에서 카테고리를 바꿔도 화면이 바뀌지 않는다(조용히 무시된다).
    /// 그래서 이 경우에는 카테고리 방법을 건너뛰고 객체 숨기기로 넘어간다.
    /// </summary>
    public static class LinkVisibilityHelper
    {
        // 대상 카테고리: 좌표조정 모델(Navisworks 링크)
        private const BuiltInCategory CoordinationModelCategory = BuiltInCategory.OST_Coordination_Model;

        /// <summary>토글 결과.</summary>
        public class ToggleResult
        {
            public bool NowVisible;   // 토글 후 켜짐이면 true
            public bool UsedCategory; // true: 카테고리 가시성으로 처리, false: 그 외 방법
            public int LinkCount;     // 다룬 좌표조정 모델 수 (카테고리 방식이면 참고값)
            public string Method;     // 실제로 통한 방법 (로그/확인용)
        }

        /// <summary>
        /// 좌표조정 모델 링크의 가시성을 토글한다. (외부에서 Transaction 을 열고 호출)
        /// </summary>
        public static ToggleResult ToggleLinkVisibility(Document doc, View view)
        {
            // 실패했을 때 사용자에게 보여줄 진단 메모. 단계마다 한 줄씩 쌓는다.
            var diagnosis = new List<string>();

            var categoryId = new ElementId(CoordinationModelCategory);

            // 문서 전체에서 좌표조정 모델을 모은다.
            //
            // 주의: 뷰를 지정한 FilteredElementCollector(doc, view.Id) 는 "그 뷰에 보이는" 객체만
            // 돌려주므로, 한 번 숨기고 나면 다시 찾지 못해 켤 수가 없다. 그래서 문서 전체로 모은다.
            List<ElementId> linkIds = CollectCoordinationModelIds(doc, diagnosis);

            diagnosis.Insert(0, $"뷰: {view.Name} (종류 {view.ViewType})");

            LogUtils.Log($"===== LINK ON/OFF 시작. 뷰={view.Name}({view.ViewType}) 좌표조정모델={linkIds.Count}개 =====");

            // 어떤 객체인지 / 어떤 파라미터를 갖고 있는지 자세히 남긴다.
            // (좌표조정 모델은 Revit API 로 켜고 끄는 표준 방법이 없어서, 실제 모델을 보고 방법을 찾아야 한다)
            LogCoordinationModelDetails(doc, view, linkIds);

            // 뷰 템플릿이 좌표조정 모델 가시성을 잠그고 있는지 확인
            string templateName;
            bool lockedByTemplate = IsLockedByTemplate(doc, view, out templateName);

            diagnosis.Add(templateName == null
                ? "뷰 템플릿: 없음"
                : $"뷰 템플릿: '{templateName}' ({(lockedByTemplate ? "좌표조정 모델 가시성을 잠그고 있음" : "잠그지 않음")})");

            // ===== 0) 뷰 파라미터 =====
            //
            // V/G 대화상자의 "Coordination Models" 탭에는
            // "Show Coordination Models in this view" 체크박스가 있다.
            // 이건 카테고리도 객체도 아닌 "뷰 자체의 설정" 이므로, 뷰의 파라미터에서 찾아 뒤집는다.
            // 카테고리/객체 방법이 Revit 에서 막혀 있으므로 이 방법을 가장 먼저 시도한다.
            ToggleResult viewParamResult = TryToggleViewParameter(view, linkIds.Count, diagnosis);
            if (viewParamResult != null) return viewParamResult;

            // ===== 1) 카테고리 가시성 =====
            //
            // 후보 카테고리를 여러 개 시도한다.
            //  - 기본 상수 OST_Coordination_Model
            //  - 객체가 실제로 물고 있는 카테고리 (링크 파일마다 하위 카테고리로 갈리는 경우가 있다)
            //  - 그 하위 카테고리의 상위 카테고리
            // 기본 상수 하나만 보고 "안 된다" 고 끝내면, 하위 카테고리로는 되는 경우를 놓친다.
            if (!lockedByTemplate)
            {
                foreach (ElementId candidateId in CollectCandidateCategoryIds(doc, categoryId, linkIds))
                {
                    ToggleResult categoryResult = TryToggleCategory(view, candidateId, linkIds.Count, diagnosis);
                    if (categoryResult != null) return categoryResult;
                }
            }
            else
            {
                diagnosis.Add("카테고리 방법: 건너뜀 (뷰 템플릿이 잠금)");
            }

            // ===== 2) 객체 숨기기 / 해제 =====
            if (linkIds.Count == 0)
                throw new InvalidOperationException(
                    "이 문서에 좌표조정 모델(Coordination Model) 링크가 없어서 켜고 끌 것이 없습니다.\n\n"
                    + BuildDiagnosisText(diagnosis));

            ToggleResult elementResult = TryToggleElements(doc, view, linkIds, diagnosis);
            if (elementResult != null) return elementResult;

            // ===== 3) 둘 다 실패 =====
            string help = lockedByTemplate
                ? $"이 뷰에는 뷰 템플릿 '{templateName}' 이(가) 걸려 있고, 그 템플릿이 좌표조정 모델의 " +
                  "가시성을 제어하고 있습니다.\n" +
                  "뷰 템플릿에서 좌표조정 모델 항목의 체크를 풀거나(그러면 뷰마다 따로 켜고 끌 수 있습니다), " +
                  "템플릿 자체의 좌표조정 모델 가시성을 바꿔야 합니다."
                : "현재 뷰에서는 좌표조정 모델을 켜고 끌 수 없습니다.";

            LogUtils.Log("LINK ON/OFF 실패. " + string.Join(" / ", diagnosis));

            throw new InvalidOperationException(help + "\n\n" + BuildDiagnosisText(diagnosis));
        }

        // ===== 0) 뷰 파라미터 =====

        /// <summary>
        /// 뷰의 파라미터로 좌표조정 모델 표시를 켜고 끈다. 성공하면 결과, 못 하면 null.
        ///
        /// V/G 대화상자 "Coordination Models" 탭의
        /// "Show Coordination Models in this view" 체크박스에 해당하는 파라미터를 찾아 뒤집는다.
        /// </summary>
        private static ToggleResult TryToggleViewParameter(View view, int linkCount, List<string> diagnosis)
        {
            // 뷰가 어떤 파라미터를 갖고 있는지는 진단에 중요하므로 전부 남긴다.
            LogViewParameters(view);

            Parameter target = FindCoordinationModelViewParameter(view);

            if (target == null)
            {
                diagnosis.Add("뷰 파라미터 방법: 좌표조정 모델 표시 파라미터를 찾지 못함");
                return null;
            }

            string paramName = target.Definition != null ? target.Definition.Name : "(이름없음)";

            if (target.StorageType != StorageType.Integer)
            {
                diagnosis.Add($"뷰 파라미터 방법: '{paramName}' 이(가) YES/NO 가 아님 ({target.StorageType})");
                return null;
            }

            if (target.IsReadOnly)
            {
                diagnosis.Add($"뷰 파라미터 방법: '{paramName}' 이(가) 읽기 전용");
                return null;
            }

            try
            {
                int current = target.AsInteger();
                target.Set(current == 0 ? 1 : 0);

                LogUtils.Log($"뷰 파라미터 '{paramName}' 토글 성공. {current} -> {(current == 0 ? 1 : 0)}");

                return new ToggleResult
                {
                    NowVisible = (current == 0), // 0(꺼짐)이었으면 이제 켜진 것
                    UsedCategory = false,
                    LinkCount = linkCount,
                    Method = $"뷰 파라미터 '{paramName}'"
                };
            }
            catch (Exception ex)
            {
                diagnosis.Add($"뷰 파라미터 방법: '{paramName}' 변경 실패 ({ex.Message})");
                LogUtils.LogError(ex, $"뷰 파라미터 '{paramName}' 변경 실패.");
                return null;
            }
        }

        /// <summary>
        /// 뷰에서 "좌표조정 모델 표시" 에 해당하는 파라미터를 찾는다. 없으면 null.
        /// 이름으로 먼저 찾고(영문/한글 UI 모두), 없으면 지정된 BuiltInParameter 를 본다.
        /// </summary>
        private static Parameter FindCoordinationModelViewParameter(View view)
        {
            try
            {
                foreach (Parameter p in view.Parameters)
                {
                    if (p == null || p.Definition == null) continue;
                    if (p.StorageType != StorageType.Integer) continue;

                    string name = p.Definition.Name ?? "";
                    if (name.IndexOf("Coordination", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.Contains("좌표조정"))
                    {
                        return p;
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "뷰 파라미터 이름 검색 실패.");
            }

            try
            {
                return view.get_Parameter(BuiltInParameter.VIS_GRAPHICS_COORDINATION_MODEL);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>뷰가 가진 파라미터를 전부 로그에 남긴다. (진단용)</summary>
        private static void LogViewParameters(View view)
        {
            try
            {
                LogUtils.Log($"[진단] 뷰 '{view.Name}' 파라미터 목록");

                foreach (Parameter p in view.Parameters)
                {
                    if (p == null || p.Definition == null) continue;

                    LogUtils.Log($"[진단]   뷰파라미터 '{p.Definition.Name}' " +
                        $"타입={p.StorageType} 읽기전용={p.IsReadOnly} 값={SafeParamValue(p)}");
                }
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "뷰 파라미터 나열 실패.");
            }
        }

        // ===== 1) 카테고리 가시성 =====

        /// <summary>
        /// 카테고리 가시성으로 토글을 시도한다. 성공하면 결과, 못 하면 null.
        /// CanCategoryBeHidden 이 false 여도 일단 시도해 본다.
        /// (이 값이 실제보다 보수적으로 나오는 경우가 있어, 되는데도 안 해보고 넘어가지 않도록)
        /// </summary>
        private static ToggleResult TryToggleCategory(View view, ElementId categoryId,
            int linkCount, List<string> diagnosis)
        {
            bool canHide;
            try
            {
                canHide = view.CanCategoryBeHidden(categoryId);
            }
            catch (Exception ex)
            {
                canHide = false;
                LogUtils.LogError(ex, "CanCategoryBeHidden 호출 실패.");
            }

            try
            {
                bool currentlyHidden = view.GetCategoryHidden(categoryId);
                view.SetCategoryHidden(categoryId, !currentlyHidden);

                LogUtils.Log($"카테고리 가시성 토글 성공. 이전숨김={currentlyHidden} -> 이제 {(currentlyHidden ? "켜짐" : "꺼짐")}");

                return new ToggleResult
                {
                    NowVisible = currentlyHidden, // 숨겨져 있었으면 이제 켜진 것
                    UsedCategory = true,
                    LinkCount = linkCount
                };
            }
            catch (Exception ex)
            {
                diagnosis.Add($"카테고리 방법: 실패 (CanCategoryBeHidden={canHide}, {ex.Message})");
                LogUtils.LogError(ex, "카테고리 가시성 변경 실패. 객체 숨기기로 대신 시도합니다.");
                return null;
            }
        }

        // ===== 2) 객체 숨기기 / 해제 =====

        /// <summary>
        /// 좌표조정 모델 객체를 직접 숨기거나 켠다. 성공하면 결과, 못 하면 null.
        /// 하나라도 보이면 전부 숨기고, 전부 숨겨져 있으면 전부 켠다.
        /// </summary>
        private static ToggleResult TryToggleElements(Document doc, View view,
            List<ElementId> linkIds, List<string> diagnosis)
        {
            var hiddenIds = new List<ElementId>();
            var visibleIds = new List<ElementId>();
            int cannotHideCount = 0;

            foreach (ElementId id in linkIds)
            {
                Element e = doc.GetElement(id);
                if (e == null) continue;

                bool canBeHidden;
                try
                {
                    canBeHidden = e.CanBeHidden(view);
                }
                catch (Exception ex)
                {
                    canBeHidden = false;
                    LogUtils.LogError(ex, $"CanBeHidden 호출 실패. Id={id}");
                }

                LogUtils.Log($"  좌표조정 모델 Id={id} 클래스={e.GetType().Name} 이름='{e.Name}' " +
                    $"숨김가능={canBeHidden} 현재숨김={SafeIsHidden(e, view)}");

                if (!canBeHidden)
                {
                    cannotHideCount++;
                    continue;
                }

                if (SafeIsHidden(e, view)) hiddenIds.Add(id);
                else visibleIds.Add(id);
            }

            if (hiddenIds.Count == 0 && visibleIds.Count == 0)
            {
                // CanBeHidden 이 false 라고 해서 진짜 안 되는지는 해봐야 안다.
                // (특수한 객체는 이 값이 실제와 다르게 나오는 경우가 있다)
                LogUtils.Log($"CanBeHidden 이 전부 false 지만, 그래도 숨기기를 시도해 봅니다. {linkIds.Count}개");

                foreach (ElementId id in linkIds)
                {
                    Element e = doc.GetElement(id);
                    if (e == null) continue;

                    if (SafeIsHidden(e, view)) hiddenIds.Add(id);
                    else visibleIds.Add(id);
                }

                if (hiddenIds.Count == 0 && visibleIds.Count == 0)
                {
                    diagnosis.Add($"객체 숨기기 방법: 실패 (찾은 {linkIds.Count}개 모두 이 뷰에서 숨길 수 없음)");
                    return null;
                }
            }

            try
            {
                if (visibleIds.Count > 0)
                {
                    view.HideElements(visibleIds);
                    LogUtils.Log($"객체 숨기기 성공. {visibleIds.Count}개를 껐습니다.");

                    return new ToggleResult { NowVisible = false, UsedCategory = false, LinkCount = visibleIds.Count };
                }

                view.UnhideElements(hiddenIds);
                LogUtils.Log($"객체 숨기기 해제 성공. {hiddenIds.Count}개를 켰습니다.");

                return new ToggleResult { NowVisible = true, UsedCategory = false, LinkCount = hiddenIds.Count };
            }
            catch (Exception ex)
            {
                diagnosis.Add($"객체 숨기기 방법: 실패 ({ex.Message})");
                LogUtils.LogError(ex, "객체 숨기기/해제 실패.");
                return null;
            }
        }

        /// <summary>IsHidden 이 예외를 던져도 기능이 멈추지 않도록 감싼다.</summary>
        private static bool SafeIsHidden(Element e, View view)
        {
            try
            {
                return e.IsHidden(view);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ===== 공통 =====

        /// <summary>
        /// 카테고리 가시성을 시도해 볼 카테고리 Id 후보를 모은다.
        /// 기본 상수 → 객체의 실제 카테고리 → 그 상위 카테고리 순. 중복은 뺀다.
        /// </summary>
        private static List<ElementId> CollectCandidateCategoryIds(Document doc,
            ElementId builtInCategoryId, List<ElementId> linkIds)
        {
            var ids = new List<ElementId> { builtInCategoryId };

            foreach (ElementId id in linkIds)
            {
                Element e = doc.GetElement(id);
                Category category = (e != null) ? e.Category : null;
                if (category == null) continue;

                if (!ids.Any(x => x == category.Id)) ids.Add(category.Id);

                Category parent = category.Parent;
                if (parent != null && !ids.Any(x => x == parent.Id)) ids.Add(parent.Id);
            }

            LogUtils.Log("카테고리 후보: " + string.Join(", ", ids.Select(x => x.ToString())));
            return ids;
        }

        /// <summary>
        /// 좌표조정 모델 객체가 어떤 물건인지 로그에 자세히 남긴다.
        /// 표준 API 로는 켜고 끌 수 없는 것으로 보이므로, 실제 모델에서 쓸 수 있는 방법을
        /// 찾기 위한 자료를 모으는 용도다. (클래스 / 카테고리 / 워크셋 / 파라미터 전부)
        /// </summary>
        private static void LogCoordinationModelDetails(Document doc, View view, List<ElementId> linkIds)
        {
            LogUtils.Log($"[진단] 문서 workshared={doc.IsWorkshared}");

            foreach (ElementId id in linkIds)
            {
                Element e = doc.GetElement(id);
                if (e == null) continue;

                Category category = e.Category;
                string categoryText = (category == null)
                    ? "(없음)"
                    : $"{category.Name}(Id={category.Id}, 상위={(category.Parent == null ? "없음" : category.Parent.Name)})";

                LogUtils.Log($"[진단] Id={id} 클래스={e.GetType().FullName} 이름='{e.Name}' 카테고리={categoryText}");
                LogUtils.Log($"[진단]   숨김가능={SafeCanBeHidden(e, view)} 현재숨김={SafeIsHidden(e, view)} " +
                    $"뷰에보임={IsShownInView(doc, view, id)} 워크셋Id={e.WorksetId}");

                // 파라미터 전부 (가시성 관련 파라미터가 있는지 찾기 위함)
                try
                {
                    foreach (Parameter p in e.Parameters)
                    {
                        if (p == null || p.Definition == null) continue;

                        LogUtils.Log($"[진단]   파라미터 '{p.Definition.Name}' " +
                            $"타입={p.StorageType} 읽기전용={p.IsReadOnly} 값={SafeParamValue(p)}");
                    }
                }
                catch (Exception ex)
                {
                    LogUtils.LogError(ex, "파라미터 나열 실패.");
                }
            }
        }

        /// <summary>현재 뷰에 실제로 나오는 객체인지.</summary>
        private static bool IsShownInView(Document doc, View view, ElementId id)
        {
            try
            {
                return new FilteredElementCollector(doc, view.Id)
                    .OfCategory(CoordinationModelCategory)
                    .WhereElementIsNotElementType()
                    .Any(x => x.Id == id);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>파라미터 값을 문자열로. 실패하면 빈 문자열.</summary>
        private static string SafeParamValue(Parameter p)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String: return p.AsString() ?? "";
                    case StorageType.Integer: return p.AsInteger().ToString();
                    case StorageType.Double: return p.AsDouble().ToString("F4");
                    case StorageType.ElementId: return p.AsElementId().ToString();
                    default: return "";
                }
            }
            catch (Exception)
            {
                return "(읽기실패)";
            }
        }

        /// <summary>CanBeHidden 이 예외를 던져도 기능이 멈추지 않도록 감싼다.</summary>
        private static bool SafeCanBeHidden(Element e, View view)
        {
            try
            {
                return e.CanBeHidden(view);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 문서에 들어있는 좌표조정 모델(Coordination Model) 객체 Id 를 모은다.
        /// </summary>
        private static List<ElementId> CollectCoordinationModelIds(Document doc, List<string> diagnosis)
        {
            var ids = new List<ElementId>();

            try
            {
                foreach (Element e in new FilteredElementCollector(doc)
                    .OfCategory(CoordinationModelCategory)
                    .WhereElementIsNotElementType())
                {
                    ids.Add(e.Id);
                }
            }
            catch (Exception ex)
            {
                // 좌표조정 모델 카테고리를 쓸 수 없는 문서라면 빈 목록으로 둔다.
                diagnosis.Add($"좌표조정 모델 수집 실패: {ex.Message}");
                LogUtils.LogError(ex, "좌표조정 모델 수집 실패.");
            }

            diagnosis.Add($"문서에서 찾은 좌표조정 모델: {ids.Count}개");
            return ids;
        }

        /// <summary>
        /// 뷰 템플릿이 "좌표조정 모델 가시성/그래픽(V/G)" 을 잠그고 있는지 검사.
        /// 잠겨 있으면 SetCategoryHidden 을 해도 화면이 바뀌지 않으므로, 객체 숨기기로 돌려야 한다.
        /// </summary>
        /// <param name="templateName">뷰에 걸린 템플릿 이름. 템플릿이 없으면 null.</param>
        private static bool IsLockedByTemplate(Document doc, View view, out string templateName)
        {
            templateName = null;

            ElementId templateId = view.ViewTemplateId;
            if (templateId == null || templateId == ElementId.InvalidElementId) return false;

            var template = doc.GetElement(templateId) as View;
            if (template == null) return false;

            templateName = template.Name;

            try
            {
                // 템플릿이 "제어하지 않는" 항목 목록. 여기에 들어 있으면 뷰에서 자유롭게 바꿀 수 있다.
                ICollection<ElementId> nonControlled = template.GetNonControlledTemplateParameterIds();
                var visibilityParamId = new ElementId(BuiltInParameter.VIS_GRAPHICS_COORDINATION_MODEL);

                return !nonControlled.Any(id => id == visibilityParamId);
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "뷰 템플릿 잠금 여부 확인 실패. 잠기지 않은 것으로 봅니다.");
                return false;
            }
        }

        /// <summary>진단 메모를 사용자에게 보여줄 문단으로 만든다.</summary>
        private static string BuildDiagnosisText(List<string> diagnosis)
        {
            return "[진단 정보]\n· " + string.Join("\n· ", diagnosis) +
                   "\n\n자세한 기록: %AppData%\\FBXtoRVT\\FBXtoRVTLogs";
        }
    }
}
