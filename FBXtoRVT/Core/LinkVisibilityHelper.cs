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
    /// [처리 순서]
    ///  1) 카테고리 단위로 끌 수 있으면(뷰 템플릿에 잠겨 있지 않고 CanCategoryBeHidden 이 true)
    ///     "Coordination Model" 카테고리 가시성을 토글한다.
    ///  2) 그럴 수 없으면(뷰 템플릿이 가시성을 잠근 경우 등), 좌표조정 모델 <b>객체</b>를
    ///     직접 숨기기/숨기기 해제 한다. 객체 단위 숨기기는 템플릿과 무관하게 항상 가능하다.
    ///  3) 문서에 좌표조정 모델이 하나도 없을 때만 안내 예외를 던진다.
    ///
    /// 예전에는 "RVT Links" 카테고리를 대상으로 삼고,
    /// <c>Category.AllowsVisibilityControl(view)</c> 가 false 이면 곧바로
    /// "현재 뷰에서는 링크 가시성을 제어할 수 없습니다" 창을 띄웠다.
    /// 지금은 대상도 바뀌었고, 카테고리로 안 되면 객체 숨기기로 넘어가므로 그 창이 뜨지 않는다.
    /// </summary>
    public static class LinkVisibilityHelper
    {
        // 대상 카테고리: 좌표조정 모델(Navisworks 링크)
        private const BuiltInCategory CoordinationModelCategory = BuiltInCategory.OST_Coordination_Model;

        /// <summary>토글 결과.</summary>
        public class ToggleResult
        {
            public bool NowVisible;   // 토글 후 켜짐이면 true
            public bool UsedCategory; // true: 카테고리 가시성으로 처리, false: 객체 숨기기로 처리
            public int LinkCount;     // 다룬 좌표조정 모델 수 (카테고리 방식이면 참고값)
        }

        /// <summary>
        /// 좌표조정 모델 링크의 가시성을 토글한다. (외부에서 Transaction 을 열고 호출)
        /// </summary>
        public static ToggleResult ToggleLinkVisibility(Document doc, View view)
        {
            // 문서 전체에서 좌표조정 모델을 모은다.
            //
            // 주의: 뷰를 지정한 FilteredElementCollector(doc, view.Id) 는 "그 뷰에 보이는" 객체만
            // 돌려주므로, 한 번 숨기고 나면 다시 찾지 못해 켤 수가 없다. 그래서 문서 전체로 모은다.
            List<ElementId> linkIds = CollectCoordinationModelIds(doc);

            // 1) 카테고리 단위로 처리할 수 있으면 그렇게 한다.
            Category category = Category.GetCategory(doc, CoordinationModelCategory);

            if (category != null
                && view.CanCategoryBeHidden(category.Id)
                && !IsCoordinationModelLockedByTemplate(doc, view))
            {
                try
                {
                    bool currentlyHidden = view.GetCategoryHidden(category.Id);
                    view.SetCategoryHidden(category.Id, !currentlyHidden);

                    return new ToggleResult
                    {
                        NowVisible = currentlyHidden, // 숨겨져 있었으면 이제 켜진 것
                        UsedCategory = true,
                        LinkCount = linkIds.Count
                    };
                }
                catch (Exception ex)
                {
                    // 카테고리로 못 바꾸면 아래 객체 숨기기로 넘어간다.
                    LogUtils.LogError(ex, "좌표조정 모델 카테고리 가시성 변경 실패. 객체 숨기기로 대신 시도합니다.");
                }
            }

            // 2) 객체 단위 숨기기/해제
            if (linkIds.Count == 0)
                throw new InvalidOperationException(
                    "이 문서에 좌표조정 모델(Coordination Model) 링크가 없어서 켜고 끌 것이 없습니다.");

            var hiddenIds = new List<ElementId>();
            var visibleIds = new List<ElementId>();

            foreach (ElementId id in linkIds)
            {
                Element e = doc.GetElement(id);
                if (e == null) continue;
                if (!e.CanBeHidden(view)) continue; // 숨길 수 없는 객체는 건드리지 않는다

                if (e.IsHidden(view)) hiddenIds.Add(id);
                else visibleIds.Add(id);
            }

            if (hiddenIds.Count == 0 && visibleIds.Count == 0)
                throw new InvalidOperationException(
                    "현재 뷰에서는 좌표조정 모델을 숨기거나 켤 수 없습니다. 뷰 템플릿의 가시성 설정을 확인하세요.");

            // 지금 하나라도 보이면 -> 전부 숨긴다. 전부 숨겨져 있으면 -> 전부 켠다.
            if (visibleIds.Count > 0)
            {
                view.HideElements(visibleIds);

                return new ToggleResult
                {
                    NowVisible = false,
                    UsedCategory = false,
                    LinkCount = visibleIds.Count
                };
            }

            view.UnhideElements(hiddenIds);

            return new ToggleResult
            {
                NowVisible = true,
                UsedCategory = false,
                LinkCount = hiddenIds.Count
            };
        }

        /// <summary>
        /// 문서에 들어있는 좌표조정 모델(Coordination Model) 객체 Id 를 모은다.
        /// </summary>
        private static List<ElementId> CollectCoordinationModelIds(Document doc)
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
                LogUtils.LogError(ex, "좌표조정 모델 수집 실패.");
            }

            return ids;
        }

        /// <summary>
        /// 뷰 템플릿이 "좌표조정 모델 가시성/그래픽(V/G)" 을 잠그고 있는지 검사.
        /// 잠겨 있으면 SetCategoryHidden 을 해도 화면이 바뀌지 않으므로, 객체 숨기기로 돌려야 한다.
        /// </summary>
        private static bool IsCoordinationModelLockedByTemplate(Document doc, View view)
        {
            ElementId templateId = view.ViewTemplateId;
            if (templateId == null || templateId == ElementId.InvalidElementId) return false;

            var template = doc.GetElement(templateId) as View;
            if (template == null) return false;

            // 템플릿이 "제어하지 않는" 항목 목록. 여기에 들어 있으면 뷰에서 자유롭게 바꿀 수 있다.
            ICollection<ElementId> nonControlled = template.GetNonControlledTemplateParameterIds();
            var visibilityParamId = new ElementId(BuiltInParameter.VIS_GRAPHICS_COORDINATION_MODEL);

            return !nonControlled.Any(id => id == visibilityParamId);
        }
    }
}
